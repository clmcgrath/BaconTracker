using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BaconTracker.App;

public class PanelLayoutSettings
{
    public bool IsVisible { get; set; } = true;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public bool IsCollapsed { get; set; } = false;
}

public static class LayoutSettingsManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BaconTracker",
        "panel_layouts.json"
    );
    private static Dictionary<string, PanelLayoutSettings> _settings = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    static LayoutSettingsManager()
    {
        LoadAll();
    }

    public static void LoadAll()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    _settings = JsonSerializer.Deserialize<Dictionary<string, PanelLayoutSettings>>(json) 
                                ?? new Dictionary<string, PanelLayoutSettings>(StringComparer.OrdinalIgnoreCase);
                    Console.WriteLine($"[LayoutSettingsManager] Loaded layouts for {_settings.Count} panels.");
                }
                else
                {
                    _settings = new Dictionary<string, PanelLayoutSettings>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LayoutSettingsManager] Error loading layout settings: {ex.Message}");
                _settings = new Dictionary<string, PanelLayoutSettings>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public static void SaveAll()
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
                Console.WriteLine($"[LayoutSettingsManager] Layout settings saved to disk at {SettingsPath}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LayoutSettingsManager] Error saving layout settings: {ex.Message}");
            }
        }
    }

    public static PanelLayoutSettings? GetLayout(string panelTitle)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(panelTitle, out var layout))
            {
                return layout;
            }
            return null;
        }
    }

    public static void SaveLayout(string panelTitle, PanelLayoutSettings layout)
    {
        lock (_lock)
        {
            _settings[panelTitle] = layout;
        }
        SaveAll();
    }
}
