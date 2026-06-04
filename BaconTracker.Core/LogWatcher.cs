using System;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace BaconTracker.Core;

public class LogWatcher : IDisposable
{
    private static LogWatcher? _instance;
    public static LogWatcher Instance
    {
        get => _instance ?? throw new InvalidOperationException("LogWatcher not initialized via dependency injection.");
        internal set => _instance = value;
    }

    private Thread? _thread;
    private bool _isRunning;
    private string? _logsDirectory;

    // Entity and Hero tracking fields for Battlegrounds
    private readonly System.Collections.Generic.Dictionary<int, string> _entityCardIds = new();
    private readonly System.Collections.Generic.Dictionary<int, int> _entityControllers = new();
    private readonly System.Collections.Generic.Dictionary<int, string> _playerNames = new();
    private readonly System.Collections.Generic.HashSet<int> _heroEntities = new();
    private int _currentEntityId = -1;
    private string _currentCardId = string.Empty;
    private string _localPlayerName = string.Empty;

    private readonly WinePrefixDetector _prefixDetector;
    private readonly ILogger<LogWatcher> _logger;

    public LogWatcher(WinePrefixDetector prefixDetector, ILogger<LogWatcher> logger)
    {
        _prefixDetector = prefixDetector;
        _logger = logger;
        _instance = this;
    }

    /// <summary>
    /// Initializes and starts monitoring the Hearthstone logs.
    /// Uses settings if configured, otherwise runs WinePrefixDetector to find the prefix.
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;

        // 1. Resolve logs directory
        _logsDirectory = SettingsManager.Settings.HearthstoneLogDirectory;

        if (string.IsNullOrEmpty(_logsDirectory) || !Directory.Exists(_logsDirectory))
        {
            _logger.LogDebug("HearthstoneLogDirectory not set or invalid. Running auto-detector...");
            _logsDirectory = _prefixDetector.DetectLogsDirectory();
            if (!string.IsNullOrEmpty(_logsDirectory))
            {
                SettingsManager.UpdateLogDirectory(_logsDirectory);
            }
        }

        if (string.IsNullOrEmpty(_logsDirectory))
        {
            _logger.LogWarning("Could not detect Hearthstone Logs directory automatically.");
            return;
        }

        _logger.LogInformation("Monitoring Hearthstone logs at: {LogsDirectory}", _logsDirectory);

        // 2. Ensure log directory exists (create it if game hasn't run yet)
        if (!Directory.Exists(_logsDirectory))
        {
            try
            {
                Directory.CreateDirectory(_logsDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Logs directory");
                return;
            }
        }

        // 3. Ensure log.config is written to enable Power.log logging
        EnsureLoggingEnabled(_logsDirectory);

        // 4. Start background tailer thread
        string powerLogPath = Path.Combine(_logsDirectory, "Power.log");
        StartTailing(powerLogPath);
    }

    private void EnsureLoggingEnabled(string logsPath)
    {
        try
        {
            string? hearthstoneDir = Path.GetDirectoryName(logsPath);
            if (string.IsNullOrEmpty(hearthstoneDir) || !Directory.Exists(hearthstoneDir)) return;

            string configPath = Path.Combine(hearthstoneDir, "log.config");
            if (!File.Exists(configPath))
            {
                string configContent = @"[Power]
LogLevel=1
FilePrinting=true
ConsolePrinting=true
ScreenPrinting=false
";
                File.WriteAllText(configPath, configContent);
                _logger.LogInformation("Generated default log.config to enable game tracking at: {ConfigPath}", configPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write log.config");
        }
    }

    private void StartTailing(string filePath)
    {
        _isRunning = true;
        _thread = new Thread(() =>
        {
            try
            {
                long lastLength = 0;

                // Wait for Power.log to be created by the game client
                while (_isRunning && !File.Exists(filePath))
                {
                    Thread.Sleep(1000);
                }

                if (!_isRunning) return;

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, Encoding.UTF8);

                // Seek to the end of the file to ignore old historical sessions
                fs.Seek(0, SeekOrigin.End);
                lastLength = fs.Length;

                _logger.LogInformation("Tailer started. Waiting for game updates...");

                while (_isRunning)
                {
                    if (fs.Length < lastLength)
                    {
                        // File was truncated/cleared by the game engine
                        fs.Seek(0, SeekOrigin.Begin);
                        reader.DiscardBufferedData();
                        _logger.LogInformation("Log file was reset.");
                    }
                    lastLength = fs.Length;

                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        ProcessLogLine(line);
                    }

                    Thread.Sleep(100); // Prevent thread starvation
                }
            }
            catch (ThreadAbortException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Power.log");
            }
        })
        {
            IsBackground = true,
            Name = "LogWatcherTailerThread"
        };
        _thread.Start();
    }

    private void ProcessLogLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        // 1. Check for Game Start
        if (line.Contains("CREATE_GAME"))
        {
            lock (_entityCardIds)
            {
                _entityCardIds.Clear();
                _entityControllers.Clear();
                _playerNames.Clear();
                _heroEntities.Clear();
                _currentEntityId = -1;
                _currentCardId = string.Empty;
                _localPlayerName = string.Empty;
            }
            _logger.LogInformation("Game start detected. Resetting state.");
            EventBus.Publish("OnGameStart");
            return;
        }

        // 2. Track entity creation blocks (SHOW_ENTITY / FULL_ENTITY)
        if (line.Contains("SHOW_ENTITY") || line.Contains("FULL_ENTITY"))
        {
            lock (_entityCardIds)
            {
                _currentEntityId = ExtractEntityId(line);
                _currentCardId = ExtractCardId(line);
                if (_currentEntityId != -1 && !string.IsNullOrEmpty(_currentCardId))
                {
                    _entityCardIds[_currentEntityId] = _currentCardId;
                }
            }
            return;
        }

        // 3. Match Player initialization lines
        if (line.StartsWith("Player EntityID="))
        {
            lock (_entityCardIds)
            {
                int pEntityId = ExtractEntityId(line);
                string pName = ExtractValue(line, "Name=");
                if (pEntityId != -1 && !string.IsNullOrEmpty(pName))
                {
                    _playerNames[pEntityId] = pName;
                }
            }
            return;
        }

        // 4. Match tag definitions inside indented SHOW_ENTITY / FULL_ENTITY blocks
        if (line.TrimStart().StartsWith("tag="))
        {
            lock (_entityCardIds)
            {
                if (_currentEntityId != -1)
                {
                    string innerTag = ExtractValue(line, "tag=");
                    string innerVal = ExtractValue(line, "value=");
                    if (innerTag == "CARDTYPE" && innerVal == "HERO")
                    {
                        _heroEntities.Add(_currentEntityId);
                        TriggerHeroAssignment(_currentEntityId);
                    }
                    else if (innerTag == "CONTROLLER" && int.TryParse(innerVal, out int ctrlId))
                    {
                        _entityControllers[_currentEntityId] = ctrlId;
                        TriggerHeroAssignment(_currentEntityId);
                    }
                }
            }
            return;
        }

        // Reset creation context on any other non-indented log line
        if (!line.StartsWith(" ") && !line.StartsWith("\t"))
        {
            _currentEntityId = -1;
            _currentCardId = string.Empty;
        }

        // 5. Parse tag changes (Turns, Gold, stats, etc.)
        if (line.Contains("TAG_CHANGE"))
        {
            ParseTagChange(line);
        }

        // 6. Parse action blocks (e.g. Card Play / Spell Casts)
        if (line.Contains("BlockType=PLAY") && line.Contains("CardType=SPELL", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Spell cast detected.");
            EventBus.Publish("OnSpellPlayed");
        }
    }

    private void TriggerHeroAssignment(int heroEntityId)
    {
        // Try to associate player name with hero card ID
        if (_entityControllers.TryGetValue(heroEntityId, out int playerEntityId) &&
            _playerNames.TryGetValue(playerEntityId, out string? playerName) &&
            _heroEntities.Contains(heroEntityId) &&
            _entityCardIds.TryGetValue(heroEntityId, out string? cardId))
        {
            if (playerName != null && cardId != null)
            {
                EventBus.Publish("OnPlayerHeroSelected", playerName, cardId);
            }
        }
    }

    private void ParseTagChange(string line)
    {
        try
        {
            string entity = ExtractValue(line, "Entity=");
            string tag = ExtractValue(line, "tag=");
            string valStr = ExtractValue(line, "value=");

            if (int.TryParse(valStr, out int value))
            {
                if (entity == "GameEntity" && tag == "TURN")
                {
                    // Turn values increment by 2 in Battlegrounds (1 for round 1 shop, 3 for round 2 shop, etc.)
                    int bgTurn = (value + 1) / 2;
                    _logger.LogInformation("Turn changed to: {BgTurn}", bgTurn);
                    EventBus.Publish("OnTurnChanged", bgTurn);
                }
                else if (tag == "RESOURCES")
                {
                    // If we receive resources, that entity is the local player!
                    if (!string.IsNullOrEmpty(entity) && entity != "1" && !entity.Contains("Player"))
                    {
                        if (_localPlayerName != entity)
                        {
                            _localPlayerName = entity;
                            EventBus.Publish("OnLocalPlayerNameDetected", entity);
                        }
                    }
                    
                    _logger.LogDebug("Local player gold resources updated: {Value}", value);
                    EventBus.Publish("OnGoldChanged", value);
                }
                else if (tag == "PLAYER_TECH_LEVEL")
                {
                    EventBus.Publish("OnOpponentTavernTierChanged", entity, value);
                }
                else if (tag == "TRIPLES_EARNED")
                {
                    EventBus.Publish("OnOpponentTriplesChanged", entity, value);
                }
                else if (tag == "DAMAGE" || tag == "ARMOR")
                {
                    // Track damage/armor changes.
                    // For health, we can compute Health = 30 - Damage (default BG hero base health)
                    // Let's resolve the current health/armor details
                    int damage = tag == "DAMAGE" ? value : 0;
                    int armor = tag == "ARMOR" ? value : 0;
                    
                    // Publish both updates so GameState can reconcile them
                    if (tag == "DAMAGE")
                    {
                        int computedHealth = 30 - damage;
                        // Hearthstone starts with 30 health in BG, but patch updates might alter max.
                        // We publish OnOpponentHealthChanged with health and current armor
                        EventBus.Publish("OnOpponentHealthChanged", entity, computedHealth, -1);
                    }
                    else if (tag == "ARMOR")
                    {
                        EventBus.Publish("OnOpponentHealthChanged", entity, -1, armor);
                    }
                }
            }
        }
        catch { }
    }

    private int ExtractEntityId(string line)
    {
        int idx = line.IndexOf("EntityID=");
        if (idx == -1) return -1;
        int start = idx + "EntityID=".Length;
        int end = line.IndexOfAny(new[] { ' ', ']' }, start);
        if (end == -1) end = line.Length;
        if (int.TryParse(line.Substring(start, end - start), out int id))
        {
            return id;
        }
        return -1;
    }

    private string ExtractCardId(string line)
    {
        int idx = line.IndexOf("CardID=");
        if (idx == -1) return string.Empty;
        int start = idx + "CardID=".Length;
        int end = line.IndexOfAny(new[] { ' ', ']' }, start);
        if (end == -1) end = line.Length;
        return line.Substring(start, end - start).Trim();
    }

    private string ExtractValue(string line, string key)
    {
        int idx = line.IndexOf(key);
        if (idx == -1) return string.Empty;
        int start = idx + key.Length;
        
        // Handle double-quoted values (e.g. Name="Bob Name")
        if (start < line.Length && line[start] == '"')
        {
            int endQuote = line.IndexOf('"', start + 1);
            if (endQuote != -1)
            {
                return line.Substring(start + 1, endQuote - start - 1);
            }
        }

        int end = line.IndexOf(' ', start);
        if (end == -1)
        {
            return line.Substring(start).Trim();
        }
        return line.Substring(start, end - start).Trim();
    }

    public void Stop()
    {
        _isRunning = false;
        _thread?.Interrupt();
        _thread = null;
        _logger.LogInformation("Tailer thread stopped.");
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
