using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace BaconTracker.Core;

public class TrackerSettings
{
    public string? HearthstoneLogDirectory { get; set; }
    public float HudScale { get; set; } = 1.0f;
}

public static class SettingsManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BaconTracker",
        "settings.json"
    );
    private static TrackerSettings _settings = new();
    private static readonly object _lock = new();

    static SettingsManager()
    {
        Load();
    }

    public static TrackerSettings Settings
    {
        get
        {
            lock (_lock)
            {
                return _settings;
            }
        }
    }

    public static void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    _settings = JsonSerializer.Deserialize<TrackerSettings>(json) ?? new TrackerSettings();
                    Log.Debug("Settings loaded successfully.");
                }
                else
                {
                    _settings = new TrackerSettings();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading settings");
                _settings = new TrackerSettings();
            }
        }
    }

    public static void Save()
    {
        lock (_lock)
        {
            try
            {
                string? dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(SettingsPath, json);
                Log.Debug("Settings saved to disk at {SettingsPath}.", SettingsPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving settings");
            }
        }
    }

    public static void UpdateLogDirectory(string path)
    {
        lock (_lock)
        {
            _settings.HearthstoneLogDirectory = path;
        }
        Save();
    }
}
