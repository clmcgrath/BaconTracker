using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace BaconTracker.App;

public class PluginManager
{
    private static PluginManager? _instance;
    public static PluginManager Instance
    {
        get => _instance ?? throw new InvalidOperationException("PluginManager not initialized via dependency injection.");
        internal set => _instance = value;
    }

    private readonly List<IOverlayPanel> _loadedPluginPanels = new();
    public IReadOnlyList<IOverlayPanel> ActivePanels => _loadedPluginPanels;

    public string PluginsDirectory { get; private set; }
    private readonly ILogger<PluginManager> _logger;

    public PluginManager(ILogger<PluginManager> logger)
    {
        _logger = logger;
        _instance = this;
        // Place plugins directory in the execution base directory
        PluginsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
    }

    /// <summary>
    /// Scans the plugins folder for Lua scripts and loads them.
    /// Creates the directory if it does not exist.
    /// </summary>
    public void LoadPlugins()
    {
        _logger.LogDebug("Unloading previous plugins...");
        // Unregister and dispose previous plugin panels
        foreach (var panel in _loadedPluginPanels)
        {
            PanelRegistry.Unregister(panel);
        }
        _loadedPluginPanels.Clear();

        _logger.LogInformation("Loading plugins from: {PluginsDirectory}", PluginsDirectory);

        if (!Directory.Exists(PluginsDirectory))
        {
            try
            {
                Directory.CreateDirectory(PluginsDirectory);
                _logger.LogInformation("Created empty plugins folder.");
                
                // Create a sample demo plugin for the developer
                CreateDemoPlugin();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create plugins directory");
                return;
            }
        }

        // Scan for Lua scripts
        try
        {
            string[] luaFiles = Directory.GetFiles(PluginsDirectory, "*.lua");
            foreach (string file in luaFiles)
            {
                _logger.LogDebug("Found script: {FileName}", Path.GetFileName(file));
                var scriptPanel = new LuaScriptedPanel(file);
                
                if (scriptPanel.CustomPanels.Count > 0)
                {
                    // The script registered its own custom panels (e.g., CounterPanel or InfoPanel)
                    foreach (var customPanel in scriptPanel.CustomPanels)
                    {
                        _loadedPluginPanels.Add(customPanel);
                    }
                }
                else if (scriptPanel.HasOnDrawCallback)
                {
                    // Fallback to the default single OnDraw scripted panel
                    scriptPanel.Register();
                    _loadedPluginPanels.Add(scriptPanel);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning files");
        }

        _logger.LogInformation("Total plugin panels loaded: {Count}", _loadedPluginPanels.Count);
    }

    private void CreateDemoPlugin()
    {
        string demoPath = Path.Combine(PluginsDirectory, "demo_tracker.lua");
        string demoCode = @"-- Demo BaconTracker Lua Addon
plugin = {
    name = ""Lobby Analyst (Lua)"",
    author = ""PluginDeveloper"",
    version = ""1.0""
}

-- 1. Create Counter Panel via Declarative Table API
local spellCounter = CounterPanel {
    title = ""Spell Tracker"",
    label = ""Spells Cast This Match"",
    value = 3,
    valueColor = { 0.2, 0.7, 0.9, 1.0 }, -- RGBA Vector (light blue)
    position = { 100, 350 }
}

-- 2. Create Info Panel via Declarative Table API
local tribesPanel = InfoPanel {
    title = ""Active Lobby Tribes"",
    position = { 100, 480 },
    size = { 300, 160 },
    rows = {
        { key = ""Quilboars"", value = ""Active"", color = { 0.2, 0.8, 0.2, 1.0 } },
        { key = ""Murlocs"", value = ""Banned"", color = { 0.9, 0.2, 0.2, 1.0 } },
        { key = ""Nagas"", value = ""Active"", color = { 0.2, 0.8, 0.2, 1.0 } }
    }
}

-- 3. Register our panels to the central overlay manager
Tracker.RegisterPanel(spellCounter)
Tracker.RegisterPanel(tribesPanel)

-- 4. Set up an event handler callback on the panel
spellCounter:Subscribe(""OnSpellPlayed"", function()
    spellCounter.value = spellCounter.value + 1
    print(""[Lua Demo] A spell was played! Incrementing counter."")
end)
";
        try
        {
            File.WriteAllText(demoPath, demoCode);
            _logger.LogInformation("Created template demo plugin at: {DemoPath}", demoPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create demo plugin");
        }
    }
}
