using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using MoonSharp.Interpreter;
using BaconTracker.Core;

namespace BaconTracker.App;

public enum PanelAnchor
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center
}

public abstract class OverlayPanel : IOverlayPanel, IDisposable
{
    public string Title { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsCollapsed { get; set; } = false;
    public virtual bool IsInteractive => false;

    // Position and Size stored in Virtual Canvas Space (referenced to 1920x1080)
    public Vector2 DefaultPosition { get; set; } = new Vector2(100, 100);
    public Vector2 DefaultSize { get; set; } = new Vector2(300, 200);

    // Actual screen coordinates computed in the last Draw call (used for OS input region detection)
    public Vector2 ScreenPosition { get; private set; } = Vector2.Zero;
    public Vector2 ScreenSize { get; private set; } = Vector2.Zero;

    // Anchor-based positioning properties (defined in Virtual Space)
    public PanelAnchor? Anchor { get; set; } = null;
    public Vector2 AnchorOffset { get; set; } = Vector2.Zero;

    // Window background & border colors when overlay is locked (translucent glass style by default)
    public Vector4 LockedBgColor { get; set; } = new Vector4(0.08f, 0.08f, 0.10f, 0.75f);
    public Vector4 LockedBorderColor { get; set; } = new Vector4(0.20f, 0.20f, 0.25f, 0.40f);

    // Event subscription trackers for self-management
    private readonly List<(string EventName, Action<object[]> Handler)> _subscriptions = new();
    private readonly Dictionary<DynValue, Action<object[]>> _luaCallbackMap = new();
    private readonly object _subscriptionLock = new();

    // Asset tracking
    private readonly List<string> _loadedAssets = new();
    private readonly object _assetLock = new();

    protected OverlayPanel(string title)
    {
        Title = title;
    }

    public virtual bool ShouldDisplay(GameState state) => true;

    /// <summary>
    /// Registers this panel to the global PanelRegistry.
    /// </summary>
    public void Register()
    {
        PanelRegistry.Register(this);
    }

    /// <summary>
    /// Unregisters this panel from the global PanelRegistry, which also disposes it.
    /// </summary>
    public void Unregister()
    {
        PanelRegistry.Unregister(this);
    }

    /// <summary>
    /// Subscribes a C# event handler to the EventBus, tracking it for auto-cleanup.
    /// </summary>
    public void Subscribe(string eventName, Action<object[]> handler)
    {
        if (handler == null) return;
        lock (_subscriptionLock)
        {
            EventBus.Subscribe(eventName, handler);
            _subscriptions.Add((eventName, handler));
        }
        Console.WriteLine($"[OverlayPanel:{Title}] Subscribed C# handler to '{eventName}'");
    }

    /// <summary>
    /// Unsubscribes a C# event handler.
    /// </summary>
    public void Unsubscribe(string eventName, Action<object[]> handler)
    {
        if (handler == null) return;
        lock (_subscriptionLock)
        {
            EventBus.Unsubscribe(eventName, handler);
            _subscriptions.Remove((eventName, handler));
        }
        Console.WriteLine($"[OverlayPanel:{Title}] Unsubscribed C# handler from '{eventName}'");
    }

    /// <summary>
    /// Subscribes a Lua function to the EventBus, wrapping and tracking it for auto-cleanup.
    /// </summary>
    public void Subscribe(string eventName, DynValue callback)
    {
        if (callback == null || callback.Type != DataType.Function)
        {
            throw new ArgumentException("[OverlayPanel] Callback must be a Lua function.");
        }

        Script script = callback.Function.OwnerScript;
        if (script == null)
        {
            throw new InvalidOperationException("[OverlayPanel] Could not resolve script owner for the callback.");
        }

        Action<object[]> csharpHandler = (args) =>
        {
            try
            {
                script.Call(callback, args);
            }
            catch (ScriptRuntimeException ex)
            {
                Console.WriteLine($"[OverlayPanel:{Title}] Lua runtime error during event '{eventName}': {ex.DecoratedMessage}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OverlayPanel:{Title}] Error during event '{eventName}': {ex.Message}");
            }
        };

        lock (_subscriptionLock)
        {
            _luaCallbackMap[callback] = csharpHandler;
        }

        Subscribe(eventName, csharpHandler);
    }

    /// <summary>
    /// Unsubscribes a Lua function from the EventBus.
    /// </summary>
    public void Unsubscribe(string eventName, DynValue callback)
    {
        if (callback == null) return;

        Action<object[]>? csharpHandler = null;
        lock (_subscriptionLock)
        {
            if (_luaCallbackMap.TryGetValue(callback, out csharpHandler))
            {
                _luaCallbackMap.Remove(callback);
            }
        }

        if (csharpHandler != null)
        {
            Unsubscribe(eventName, csharpHandler);
        }
    }

    /// <summary>
    /// Cleans up all active event subscriptions registered by this panel.
    /// </summary>
    public void UnsubscribeAll()
    {
        lock (_subscriptionLock)
        {
            foreach (var sub in _subscriptions)
            {
                EventBus.Unsubscribe(sub.EventName, sub.Handler);
            }
            _subscriptions.Clear();
            _luaCallbackMap.Clear();
        }
        Console.WriteLine($"[OverlayPanel:{Title}] Unsubscribed all events.");
    }

    /// <summary>
    /// Loads a local PNG/JPG asset from disk, registers it as an OpenGL texture, and returns the ImGui texture ID.
    /// Tracks the loaded asset path.
    /// </summary>
    public IntPtr LoadTexture(string path)
    {
        IntPtr id = AssetManager.Instance.LoadTexture(path);
        lock (_assetLock)
        {
            if (!_loadedAssets.Contains(path))
            {
                _loadedAssets.Add(path);
            }
        }
        return id;
    }

    /// <summary>
    /// Loads a local PNG/JPG asset from disk, registers it as an OpenGL texture, and returns the ImGui texture ID as a string.
    /// Tracks the loaded asset path.
    /// </summary>
    public string LoadAsset(string path)
    {
        return LoadTexture(path).ToString();
    }

    /// <summary>
    /// Saves this panel's current layout configuration.
    /// </summary>
    public void SaveLayout()
    {
        Console.WriteLine($"[OverlayPanel:{Title}] Saving layout: Pos({DefaultPosition.X}, {DefaultPosition.Y}) Size({DefaultSize.X}, {DefaultSize.Y}) Collapsed({IsCollapsed})");
        var settings = new PanelLayoutSettings
        {
            IsVisible = IsVisible,
            X = DefaultPosition.X,
            Y = DefaultPosition.Y,
            Width = DefaultSize.X,
            Height = DefaultSize.Y,
            IsCollapsed = IsCollapsed
        };
        LayoutSettingsManager.SaveLayout(Title, settings);
    }

    /// <summary>
    /// Loads this panel's layout configuration if it was persisted.
    /// Calculates anchor coordinates if no settings exist.
    /// </summary>
    public void LoadLayout()
    {
        // Capture defaults in case loading fails
        Vector2 defaultPos = DefaultPosition;
        Vector2 defaultSize = DefaultSize;
        bool defaultVisible = IsVisible;
        bool defaultCollapsed = IsCollapsed;

        try
        {
            var settings = LayoutSettingsManager.GetLayout(Title);
            if (settings != null)
            {
                IsVisible = settings.IsVisible;
                DefaultPosition = new Vector2(settings.X, settings.Y);
                DefaultSize = new Vector2(settings.Width, settings.Height);
                IsCollapsed = settings.IsCollapsed;
                Console.WriteLine($"[OverlayPanel:{Title}] Loaded layout: Pos({settings.X}, {settings.Y}) Size({settings.Width}, {settings.Height}) Collapsed({settings.IsCollapsed})");
            }
            else if (Anchor.HasValue)
            {
                ApplyAnchorPositioning();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OverlayPanel:{Title}] Error loading layout: {ex.Message}. Reverting to defaults.");
            // Restore defaults
            IsVisible = defaultVisible;
            DefaultPosition = defaultPos;
            DefaultSize = defaultSize;
            IsCollapsed = defaultCollapsed;

            if (Anchor.HasValue)
            {
                try
                {
                    ApplyAnchorPositioning();
                }
                catch {}
            }
        }
    }

    private void ApplyAnchorPositioning()
    {
        if (!Anchor.HasValue) return;

        float x = AnchorOffset.X;
        float y = AnchorOffset.Y;

        switch (Anchor.Value)
        {
            case PanelAnchor.TopLeft:
                break;
            case PanelAnchor.TopRight:
                x = 1920f - DefaultSize.X + AnchorOffset.X;
                break;
            case PanelAnchor.BottomLeft:
                y = 1080f - DefaultSize.Y + AnchorOffset.Y;
                break;
            case PanelAnchor.BottomRight:
                x = 1920f - DefaultSize.X + AnchorOffset.X;
                y = 1080f - DefaultSize.Y + AnchorOffset.Y;
                break;
            case PanelAnchor.Center:
                x = (1920f - DefaultSize.X) / 2 + AnchorOffset.X;
                y = (1080f - DefaultSize.Y) / 2 + AnchorOffset.Y;
                break;
        }

        DefaultPosition = new Vector2(x, y);
    }

    /// <summary>
    /// Called by the host window manager to draw the panel wrapper.
    /// Handles position scaling, sizing, and resolution containment.
    /// </summary>
    public void Draw(bool isOverlayLocked)
    {
        if (!IsVisible) return;

        var displaySize = ImGui.GetIO().DisplaySize;
        float hudScale = SettingsManager.Settings.HudScale;

        // Resolve scale ratios (using 1920x1080 reference resolution)
        float rx = displaySize.X > 0 ? (displaySize.X / 1920f) : 1f;
        float ry = displaySize.Y > 0 ? (displaySize.Y / 1080f) : 1f;

        // Clamp coordinates in virtual canvas coordinates to prevent off-screen orphaning
        float maxAllowedX = Math.Max(0, 1920f - DefaultSize.X);
        float maxAllowedY = Math.Max(0, 1080f - DefaultSize.Y);

        if (DefaultPosition.X < 0 || DefaultPosition.X > maxAllowedX ||
            DefaultPosition.Y < 0 || DefaultPosition.Y > maxAllowedY)
        {
            float clampedX = Math.Clamp(DefaultPosition.X, 0, maxAllowedX);
            float clampedY = Math.Clamp(DefaultPosition.Y, 0, maxAllowedY);
            DefaultPosition = new Vector2(clampedX, clampedY);
        }

        // Convert virtual coordinates to actual screen coordinates (scaled by aspect ratio & zoom)
        Vector2 screenPos = new Vector2(DefaultPosition.X * rx, DefaultPosition.Y * ry);
        Vector2 screenSize = new Vector2(DefaultSize.X * rx * hudScale, DefaultSize.Y * ry * hudScale);

        ScreenPosition = screenPos;
        ScreenSize = screenSize;

        // Position window in ImGui
        if (isOverlayLocked)
        {
            ImGui.SetNextWindowPos(screenPos, ImGuiCond.Always);
        }
        else
        {
            ImGui.SetNextWindowPos(screenPos, ImGuiCond.FirstUseEver);
        }

        // Enforce size setting (locked uses forced scaling, unlocked allows manual adjustment)
        ImGui.SetNextWindowSize(screenSize, isOverlayLocked ? ImGuiCond.Always : ImGuiCond.FirstUseEver);

        // Enforce collapsed state (locked uses force, unlocked applies saved state on load/appear)
        ImGui.SetNextWindowCollapsed(IsCollapsed, isOverlayLocked ? ImGuiCond.Always : ImGuiCond.Appearing);

        // Configure window flags based on lock state
        ImGuiWindowFlags flags = ImGuiWindowFlags.None;
        if (isOverlayLocked)
        {
            flags |= ImGuiWindowFlags.NoTitleBar |
                     ImGuiWindowFlags.NoResize |
                     ImGuiWindowFlags.NoMove |
                     ImGuiWindowFlags.NoCollapse |
                     ImGuiWindowFlags.NoScrollbar;

            ImGui.PushStyleColor(ImGuiCol.WindowBg, LockedBgColor);
            ImGui.PushStyleColor(ImGuiCol.Border, LockedBorderColor);
        }

        // Render the ImGui window wrapper
        bool opened = ImGui.Begin(Title, flags);

        if (isOverlayLocked)
        {
            ImGui.PopStyleColor(2);
        }

        if (opened)
        {
            // Apply custom zoom scaling to panel fonts and elements
            ImGui.SetWindowFontScale(hudScale);

            // Execute custom rendering inside the panel
            OnDraw();

            // Reset font scale
            ImGui.SetWindowFontScale(1.0f);
        }

        // Back-propagate manual drags/resizes when unlocked into virtual coordinates
        if (!isOverlayLocked)
        {
            IsCollapsed = ImGui.IsWindowCollapsed();

            var pos = ImGui.GetWindowPos();
            Vector2 newVirtualPos = new Vector2(pos.X / rx, pos.Y / ry);
            if (newVirtualPos != DefaultPosition)
            {
                DefaultPosition = newVirtualPos;
            }

            if (!IsCollapsed)
            {
                var size = ImGui.GetWindowSize();
                Vector2 newVirtualSize = new Vector2(size.X / (rx * hudScale), size.Y / (ry * hudScale));
                if (newVirtualSize != DefaultSize)
                {
                    DefaultSize = newVirtualSize;
                }
            }
        }

        ImGui.End();
    }

    /// <summary>
    /// Custom widget rendering logic implemented by plugins or built-in overlays.
    /// Use ImGui.Text, ImGui.Button, etc. inside this method.
    /// </summary>
    protected abstract void OnDraw();

    public virtual void Dispose()
    {
        UnsubscribeAll();
        GC.SuppressFinalize(this);
    }
}
