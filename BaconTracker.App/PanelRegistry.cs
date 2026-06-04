using System;
using System.Collections.Generic;

namespace BaconTracker.App;

public static class PanelRegistry
{
    private static readonly List<IOverlayPanel> _panels = new();
    private static readonly object _lock = new();

    public static event Action? OnPanelsChanged;

    public static IReadOnlyList<IOverlayPanel> ActivePanels
    {
        get
        {
            lock (_lock)
            {
                return new List<IOverlayPanel>(_panels);
            }
        }
    }

    public static void Register(IOverlayPanel panel)
    {
        if (panel == null) return;

        lock (_lock)
        {
            if (!_panels.Contains(panel))
            {
                _panels.Add(panel);
                Console.WriteLine($"[PanelRegistry] Registered panel: {panel.Title}");
                
                // Automatically load layout for the registered panel if it is an OverlayPanel
                if (panel is OverlayPanel overlayPanel)
                {
                    overlayPanel.LoadLayout();
                }
            }
        }
        OnPanelsChanged?.Invoke();
    }

    public static void Unregister(IOverlayPanel panel)
    {
        if (panel == null) return;

        lock (_lock)
        {
            if (_panels.Remove(panel))
            {
                Console.WriteLine($"[PanelRegistry] Unregistered panel: {panel.Title}");
                
                // Automatically save layout and dispose (clean up events and assets) when unregistering
                if (panel is OverlayPanel overlayPanel)
                {
                    overlayPanel.SaveLayout();
                    overlayPanel.Dispose();
                }
            }
        }
        OnPanelsChanged?.Invoke();
    }

    public static void Clear()
    {
        List<IOverlayPanel> copy;
        lock (_lock)
        {
            copy = new List<IOverlayPanel>(_panels);
            _panels.Clear();
        }

        foreach (var panel in copy)
        {
            if (panel is OverlayPanel overlayPanel)
            {
                overlayPanel.SaveLayout();
                overlayPanel.Dispose();
            }
        }
        OnPanelsChanged?.Invoke();
    }
}
