using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;

namespace BaconTracker.App;

public class LuaTrackerProxy
{
    private readonly LuaScriptedPanel _ownerPanel;
    private readonly List<IOverlayPanel> _registeredPanels = new();

    public IReadOnlyList<IOverlayPanel> RegisteredPanels => _registeredPanels;

    public LuaTrackerProxy(LuaScriptedPanel ownerPanel)
    {
        _ownerPanel = ownerPanel;
    }

    /// <summary>
    /// Registers a custom or template panel created inside the Lua script.
    /// Exposed as Tracker.RegisterPanel(panel)
    /// </summary>
    public void RegisterPanel(IOverlayPanel panel)
    {
        if (panel != null)
        {
            if (panel is OverlayPanel op)
            {
                op.Register();
            }
            else
            {
                PanelRegistry.Register(panel);
            }
            _registeredPanels.Add(panel);
            Console.WriteLine($"[LuaTrackerProxy] Registered panel: '{panel.Title}'");
        }
    }

    /// <summary>
    /// Loads a PNG/JPG asset from disk, registers it as an OpenGL texture, and returns the ImGui texture ID.
    /// Exposed to Lua as Tracker.LoadAsset("path/to/icon.png")
    /// </summary>
    public string LoadAsset(string path)
    {
        return _ownerPanel.LoadAsset(path);
    }

    /// <summary>
    /// Subscribes a Lua function to a game event.
    /// Exposed to Lua as Tracker.Subscribe("OnCardPlayed", callbackFunction)
    /// </summary>
    public void Subscribe(string eventName, DynValue callback)
    {
        _ownerPanel.Subscribe(eventName, callback);
    }
}
