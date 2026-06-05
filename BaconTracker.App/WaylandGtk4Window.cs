using System;
using System.Runtime.InteropServices;
using Gtk;
using Gdk;
using ImGuiNET;
using Silk.NET.OpenGL;
using BaconTracker.Core;
using Microsoft.Extensions.Logging;

namespace BaconTracker.App;

public class WaylandGtk4Window : IOverlayWindow
{
    private Gtk.Application? _app;
    private Gtk.Window? _window;
    private Gtk.GLArea? _glArea;
    private ImGuiController? _imGuiController;
    private GL? _gl;
    private bool _isInitialized;
    private DateTime _lastFrameTime = DateTime.UtcNow;
    private bool _clickThrough = true;
    private readonly GameState _gameState = new();
    private bool _layerShellSupported;

    private readonly LogWatcher _logWatcher;
    private readonly AssetManager _assetManager;
    private readonly PluginManager _pluginManager;
    private readonly ILogger<WaylandGtk4Window> _logger;

    public WaylandGtk4Window(
        LogWatcher logWatcher,
        AssetManager assetManager,
        PluginManager pluginManager,
        ILogger<WaylandGtk4Window> logger)
    {
        _logWatcher = logWatcher;
        _assetManager = assetManager;
        _pluginManager = pluginManager;
        _logger = logger;
    }

    [DllImport("libgtk-4.so.1", EntryPoint = "gtk_gl_area_set_has_alpha")]
    private static extern void GtkGlAreaSetHasAlpha(IntPtr glarea, [MarshalAs(UnmanagedType.Bool)] bool has_alpha);

    [DllImport("libgtk-4.so.1", EntryPoint = "gtk_widget_set_focusable")]
    private static extern void GtkWidgetSetFocusable(IntPtr widget, [MarshalAs(UnmanagedType.Bool)] bool focusable);

    [DllImport("libgtk-4.so.1", EntryPoint = "gtk_widget_set_focus_on_click")]
    private static extern void GtkWidgetSetFocusOnClick(IntPtr widget, [MarshalAs(UnmanagedType.Bool)] bool focus_on_click);

    [DllImport("libgtk-4.so.1", EntryPoint = "gtk_native_get_surface")]
    private static extern IntPtr GtkNativeGetSurface(IntPtr native);

    [DllImport("libgtk-4.so.1", EntryPoint = "gdk_surface_set_input_region")]
    private static extern void GdkSurfaceSetInputRegion(IntPtr surface, IntPtr region);

    [DllImport("libcairo.so.2", EntryPoint = "cairo_region_create")]
    private static extern IntPtr CairoRegionCreate();

    [DllImport("libcairo.so.2", EntryPoint = "cairo_region_destroy")]
    private static extern void CairoRegionDestroy(IntPtr region);

    [DllImport("libcairo.so.2", EntryPoint = "cairo_region_union_rectangle")]
    private static extern int CairoRegionUnionRectangle(IntPtr region, ref CairoRectangleInt rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct CairoRectangleInt
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    public void Initialize()
    {
        _app = Gtk.Application.New("com.bacontracker.app", Gio.ApplicationFlags.FlagsNone);
        _app.OnActivate += OnAppActivate;
    }

    public void Run()
    {
        if (_app == null)
            throw new InvalidOperationException("Window not initialized");

        _app.RunWithSynchronizationContext(null);
    }

    private void OnAppActivate(object? sender, EventArgs e)
    {
        var display = Display.GetDefault();
        if (display != null)
        {
            _logger.LogInformation("Active GDK Display Backend: {DisplayType}", display.GetType().Name);
        }

        // 1. Create transparent CSS provider
        var cssProvider = CssProvider.New();
        cssProvider.LoadFromData("window { background-color: rgba(0, 0, 0, 0); }", -1);
        StyleContext.AddProviderForDisplay(
            display ?? Display.GetDefault()!,
            cssProvider,
            600 // PriorityApplication
        );

        // 2. Create the window
        _window = Gtk.Window.New();
        _window.Application = _app;
        _window.Title = "Bacon Tracker Overlay";
        _window.SetDefaultSize(1920, 1080);
        _window.Resizable = false;
        _window.Decorated = false;

        // Set window/taskbar icon
        try
        {
            var currentDisplay = display ?? Display.GetDefault();
            if (currentDisplay != null)
            {
                var iconTheme = IconTheme.GetForDisplay(currentDisplay);
                iconTheme.AddSearchPath(AppDomain.CurrentDomain.BaseDirectory);
                _window.IconName = "bacontracker";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set GTK4 window icon");
        }

        // Apply Layer Shell if supported
        bool layerShellSupported = false;
        try
        {
            if (NativeLibrary.TryLoad("libgtk4-layer-shell.so.0", out IntPtr handle))
            {
                NativeLibrary.Free(handle);
                if (Gtk4LayerShell.IsSupported())
                {
                    layerShellSupported = true;
                }
            }
        }
        catch
        {
            // Ignore loading failures
        }
        _layerShellSupported = layerShellSupported;

        if (_layerShellSupported)
        {
            _logger.LogInformation("Wayland Layer Shell is supported. Initializing...");
            IntPtr rawWindowHandle = _window.Handle.DangerousGetHandle();
            InitializeLayerShell(rawWindowHandle);
        }
        else
        {
            _logger.LogWarning("Wayland Layer Shell is NOT supported or not installed on this compositor. Running as standard window.");
        }

        // 3. Create GLArea
        _glArea = GLArea.New();
        
        try
        {
            GtkGlAreaSetHasAlpha(_glArea.Handle.DangerousGetHandle(), true);
        }
        catch (EntryPointNotFoundException)
        {
            Console.WriteLine("[WaylandGtk4Window] Note: gtk_gl_area_set_has_alpha not found in libgtk-4.so.1 (requires GTK 4.8+). Running without GLArea alpha channel.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WaylandGtk4Window] Note: Failed to set GLArea alpha: {ex.Message}");
        }

        _glArea.HasDepthBuffer = false;
        _glArea.HasStencilBuffer = false;
        _glArea.AutoRender = true;

        _glArea.OnRealize += OnGLAreaRealize;
        _glArea.OnUnrealize += OnGLAreaUnrealize;
        _glArea.OnResize += OnGLAreaResize;
        _glArea.OnRender += OnGLAreaRender;

        _window.SetChild(_glArea);

        // 4. Attach input controllers
        SetupInputControllers();

        // 5. Apply initial click-through
        ApplyClickThrough();

        // 6. Show window
        _window.Present();

        // 7. For X11 fallback, request window manager to keep above after mapped
        if (!layerShellSupported)
        {
            GLib.Functions.TimeoutAdd(0, 500, new GLib.SourceFunc(() =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "wmctrl",
                        Arguments = "-r \"Bacon Tracker Overlay\" -b add,above",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WaylandGtk4Window] X11 Keep-Above wmctrl failed: {ex.Message}");
                }
                return false; // Run once
            }));
        }

        // 8. Add frame timer loop for drawing (approx 60 FPS = 16ms)
        GLib.Functions.TimeoutAdd(0, 16, new GLib.SourceFunc(OnTickRedraw));
    }

    private void SetupInputControllers()
    {
        if (_glArea == null) return;

        // Pointer Motion - Track always so tooltips work even when overlay is locked
        var motionCtrl = EventControllerMotion.New();
        motionCtrl.OnMotion += (sender, args) =>
        {
            var io = ImGui.GetIO();
            io.AddMousePosEvent((float)args.X, (float)args.Y);
        };
        _glArea.AddController(motionCtrl);

        // Mouse Clicks - Register if unlocked OR if hovering over an active ImGui widget
        var clickCtrl = GestureClick.New();
        clickCtrl.SetButton(0); // Listen to all buttons
        clickCtrl.OnPressed += (sender, args) =>
        {
            var io = ImGui.GetIO();
            if (!_clickThrough || io.WantCaptureMouse)
            {
                int button = (int)clickCtrl.GetCurrentButton();
                int imguiButton = button switch
                {
                    1 => 0, // Left
                    3 => 1, // Right
                    2 => 2, // Middle
                    _ => -1
                };
                if (imguiButton != -1)
                {
                    io.AddMouseButtonEvent(imguiButton, true);
                }
            }
        };
        clickCtrl.OnReleased += (sender, args) =>
        {
            var io = ImGui.GetIO();
            if (!_clickThrough || io.WantCaptureMouse)
            {
                int button = (int)clickCtrl.GetCurrentButton();
                int imguiButton = button switch
                {
                    1 => 0, // Left
                    3 => 1, // Right
                    2 => 2, // Middle
                    _ => -1
                };
                if (imguiButton != -1)
                {
                    io.AddMouseButtonEvent(imguiButton, false);
                }
            }
        };
        _glArea.AddController(clickCtrl);

        // Scroll - Scroll if unlocked OR if hovering over scrollable ImGui widgets
        var scrollCtrl = EventControllerScroll.New(EventControllerScrollFlags.Vertical);
        scrollCtrl.OnScroll += (sender, args) =>
        {
            var io = ImGui.GetIO();
            if (!_clickThrough || io.WantCaptureMouse)
            {
                io.AddMouseWheelEvent(0, -(float)args.Dy);
                return true;
            }
            return false;
        };
        _glArea.AddController(scrollCtrl);

        // Keyboard & Interactivity Hotkeys - Accept keyboard input when focused/active
        var keyCtrl = EventControllerKey.New();
        keyCtrl.OnKeyPressed += (sender, args) =>
        {
            // F12 (0xffc9) or Grave Accent / Tilde (0x0060) toggles global overlay interactivity
            if (args.Keyval == 0xffc9 || args.Keyval == 0x0060)
            {
                bool nextState = !_clickThrough;
                SetClickThrough(nextState);
                Console.WriteLine($"[WaylandGtk4Window] Hotkey Pressed! Interactive Mode = {!nextState}");
                return true;
            }

            var io = ImGui.GetIO();
            if (!_clickThrough || io.WantCaptureKeyboard)
            {
                io.AddKeyEvent(ConvertGdkKeyToImGuiKey((uint)args.Keyval), true);
                if (args.Keyval >= 32 && args.Keyval <= 126)
                {
                    io.AddInputCharacter((uint)args.Keyval);
                }
                return true;
            }
            return false;
        };
        keyCtrl.OnKeyReleased += (sender, args) =>
        {
            var io = ImGui.GetIO();
            if (!_clickThrough || io.WantCaptureKeyboard)
            {
                io.AddKeyEvent(ConvertGdkKeyToImGuiKey((uint)args.Keyval), false);
            }
        };
        _glArea.AddController(keyCtrl);
    }

    private void OnGLAreaRealize(object? sender, EventArgs e)
    {
        if (_glArea == null) return;
        _glArea.MakeCurrent();

        _gl = GL.GetApi(GlLoader.GetProcAddress);
        _assetManager.Initialize(_gl);
        
        int w = _glArea.GetAllocatedWidth();
        int h = _glArea.GetAllocatedHeight();

        _imGuiController = new ImGuiController(_gl, w, h);
        _isInitialized = true;
        _lastFrameTime = DateTime.UtcNow;

        // Initialize Card Database
        CardDatabase.Initialize();

        // Mock some game state data for template panel testing
        _gameState.ActiveTribes.Add("Quilboars");
        _gameState.ActiveTribes.Add("Beasts");
        _gameState.ActiveTribes.Add("Nagas");
        _gameState.SpellsPlayed = 4;
        _gameState.PlayerGold = 8;

        // Initialize panels (C# Native)
        new LobbyAnalystPanel().Register();
        new MinionBrowserPanel().Register();
        new BoardHistoryPanel(_gameState).Register();

        // Load and Register Lua Scripted Panels (Unified Registry Pattern)
        _pluginManager.LoadPlugins();

        // Start tracking Hearthstone log events in the background
        _logWatcher.Start();
    }

    private void OnGLAreaUnrealize(object? sender, EventArgs e)
    {
        // Stop background log watcher thread
        _logWatcher.Stop();

        _isInitialized = false;
        _imGuiController?.Dispose();
        _imGuiController = null;
        _assetManager.Dispose();
        _gl?.Dispose();
        _gl = null;
        PanelRegistry.Clear();
    }

    private void OnGLAreaResize(object? sender, GLArea.ResizeSignalArgs e)
    {
        _imGuiController?.Resize(e.Width, e.Height);
    }

    private bool OnGLAreaRender(object? sender, GLArea.RenderSignalArgs e)
    {
        if (!_isInitialized || _imGuiController == null)
            return false;

        var now = DateTime.UtcNow;
        float delta = (float)(now - _lastFrameTime).TotalSeconds;
        _lastFrameTime = now;

        if (_gl != null)
        {
            if (!_clickThrough)
            {
                // A subtle semi-transparent dark tint when HUD is unlocked to make the customization mode obvious
                _gl.ClearColor(0.02f, 0.02f, 0.05f, 0.40f);
            }
            else
            {
                _gl.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
            }
            _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        }

        _imGuiController.Update(delta);

        // Draw all panels
        RenderOverlayInterface();

        _imGuiController.Render();

        UpdateInputRegion();

        return true;
    }

    private void RenderOverlayInterface()
    {
        // 1. Draw registered panels (virtual widgets on the single-window canvas)
        foreach (var panel in PanelRegistry.ActivePanels)
        {
            try
            {
                // Only draw if visible and the plugin display logic evaluates to true
                if (panel.IsVisible && panel.ShouldDisplay(_gameState))
                {
                    panel.Draw(_clickThrough);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WaylandGtk4Window] Error drawing panel {panel.Title}: {ex.Message}");
            }
        }

        // 2. Draw global toggle button at (10, 10) in both states
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);

        ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10));
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(32, 32));
        ImGuiWindowFlags toggleFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground;
        
        bool toggleOpened = ImGui.Begin("##HUDLockToggleWindow", toggleFlags);
        ImGui.PopStyleVar(2); // Pop WindowPadding and WindowBorderSize

        if (toggleOpened)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(0, 0));
            // Make button background fully transparent in both states, with subtle hover/active highlights
            ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(1.0f, 1.0f, 1.0f, 0.15f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new System.Numerics.Vector4(1.0f, 1.0f, 1.0f, 0.3f));

            System.Numerics.Vector2 btnPos = ImGui.GetCursorScreenPos();
            if (ImGui.Button("##HUDLockToggleBtn", new System.Numerics.Vector2(32, 32)))
            {
                // Toggle locked state (layout saving is handled inside SetClickThrough)
                SetClickThrough(!_clickThrough);
            }

            // Draw custom padlock vector graphics on top of the button based on current lock state (size 24, centered with 4px margin)
            DrawPadlockIcon(btnPos + new System.Numerics.Vector2(4, 4), 24, _clickThrough);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_clickThrough ? "Unlock HUD Layout (F12)" : "Lock HUD Layout (F12)");
            }
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar();
        }
        ImGui.End();

        // 3. Draw global controller info panel if unlocked (positioned below the toggle button to avoid overlapping)
        if (!_clickThrough)
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 50), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(250, 140), ImGuiCond.FirstUseEver);
            bool opened = ImGui.Begin("BaconTracker Controller");
            if (opened)
            {
                ImGui.Text("HUD Status: UNLOCKED");
                ImGui.Text("Drag panels to position.");
                ImGui.Spacing();
                ImGui.TextColored(new System.Numerics.Vector4(0.2f, 0.9f, 0.2f, 1.0f), "Click padlock in top-left to lock.");
                var display = Display.GetDefault();
                bool isWayland = display?.GetType().Name.Contains("Wayland", StringComparison.OrdinalIgnoreCase) ?? false;
                if (isWayland && !_layerShellSupported)
                {
                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1.0f, 0.4f, 0.4f, 1.0f));
                    ImGui.TextWrapped("Warning: Wayland Layer Shell is inactive. If the overlay disappears when you click the game, you must manually right-click the window titlebar (or press Alt+Space) and select 'Always on Top' / 'Pin'.");
                    ImGui.Spacing();
                    ImGui.TextWrapped("Tip: Launch with: GDK_BACKEND=x11 ./BaconTracker.App to force XWayland mode, which enables automatic keep-above.");
                    ImGui.PopStyleColor();
                }
            }
            ImGui.End();
        }
    }

    private void DrawPadlockIcon(System.Numerics.Vector2 screenPos, float size, bool locked)
    {
        var drawList = ImGui.GetWindowDrawList();
        
        // Padlock body (bottom half)
        System.Numerics.Vector2 bodyMin = screenPos + new System.Numerics.Vector2(size * 0.25f, size * 0.5f);
        System.Numerics.Vector2 bodyMax = screenPos + new System.Numerics.Vector2(size * 0.75f, size * 0.9f);
        uint bodyColor = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.9f, 0.7f, 0.1f, 1.0f)); // Gold body
        drawList.AddRectFilled(bodyMin, bodyMax, bodyColor, 2.0f);
        
        // Padlock shackle (top half arc)
        System.Numerics.Vector2 center = screenPos + new System.Numerics.Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.18f;
        uint shackleColor = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f)); // Silver shackle
        
        // If locked: draw standard shackle arc.
        // If unlocked: draw the shackle open (shifted or rotated up).
        float shackleOffset = locked ? 0.0f : -size * 0.15f;
        
        int segments = 8;
        for (int i = 0; i <= segments; i++)
        {
            float angle1 = (float)Math.PI + (i * (float)Math.PI / segments);
            float angle2 = (float)Math.PI + ((i + 1) * (float)Math.PI / segments);
            
            System.Numerics.Vector2 p1 = center + new System.Numerics.Vector2((float)Math.Cos(angle1) * radius, (float)Math.Sin(angle1) * radius + shackleOffset);
            System.Numerics.Vector2 p2 = center + new System.Numerics.Vector2((float)Math.Cos(angle2) * radius, (float)Math.Sin(angle2) * radius + shackleOffset);
            
            if (locked)
            {
                if (i == 0)
                {
                    drawList.AddLine(p1, p1 + new System.Numerics.Vector2(0, size * 0.15f), shackleColor, 2.0f);
                }
                if (i == segments)
                {
                    drawList.AddLine(p1, p1 + new System.Numerics.Vector2(0, size * 0.15f), shackleColor, 2.0f);
                }
            }
            else
            {
                // Unlocked shackle: left side is open (doesn't connect to body), right side connects or hangs open
                if (i == 0)
                {
                    // Shackle hangs open: draw a longer left leg but don't connect
                    drawList.AddLine(p1, p1 + new System.Numerics.Vector2(0, size * 0.1f), shackleColor, 2.0f);
                }
                if (i == segments)
                {
                    // Right leg extends all the way to the body
                    drawList.AddLine(p1, p1 + new System.Numerics.Vector2(0, size * 0.3f), shackleColor, 2.0f);
                }
            }
            
            drawList.AddLine(p1, p2, shackleColor, 2.0f);
        }
        
        // Small keyhole dot
        System.Numerics.Vector2 keyhole = screenPos + new System.Numerics.Vector2(size * 0.5f, size * 0.68f);
        drawList.AddCircleFilled(keyhole, 1.5f, ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.1f, 0.1f, 0.1f, 1.0f)));
    }

    private bool OnTickRedraw()
    {
        if (_glArea != null && _window != null)
        {
            _glArea.QueueDraw();
        }
        return true; // Keep timer running
    }

    public void SetClickThrough(bool clickThrough)
    {
        bool stateChanged = (_clickThrough != clickThrough);
        _clickThrough = clickThrough;
        ApplyClickThrough();

        if (stateChanged && _clickThrough)
        {
            Console.WriteLine("[WaylandGtk4Window] Overlay locked. Saving panel layouts...");
            foreach (var panel in PanelRegistry.ActivePanels)
            {
                if (panel is OverlayPanel op)
                {
                    try
                    {
                        op.SaveLayout();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WaylandGtk4Window] Error saving layout for panel {panel.Title}: {ex.Message}");
                    }
                }
            }
        }
    }

    private void ApplyClickThrough()
    {
        if (_window == null) return;

        IntPtr rawWindowHandle = _window.Handle.DangerousGetHandle();
        IntPtr rawGlAreaHandle = _glArea != null ? _glArea.Handle.DangerousGetHandle() : IntPtr.Zero;

        if (_clickThrough)
        {
            Console.WriteLine("[WaylandGtk4Window] Setting Click-Through: TRUE");
            try
            {
                Gtk4LayerShell.SetKeyboardMode(rawWindowHandle, (int)Gtk4LayerShell.KeyboardMode.None);
            }
            catch {}
            try
            {
                GtkWidgetSetFocusable(rawWindowHandle, false);
                GtkWidgetSetFocusOnClick(rawWindowHandle, false);
                if (rawGlAreaHandle != IntPtr.Zero)
                {
                    GtkWidgetSetFocusable(rawGlAreaHandle, false);
                    GtkWidgetSetFocusOnClick(rawGlAreaHandle, false);
                }
            }
            catch {}
        }
        else
        {
            Console.WriteLine("[WaylandGtk4Window] Setting Click-Through: FALSE");
            try
            {
                Gtk4LayerShell.SetKeyboardMode(rawWindowHandle, (int)Gtk4LayerShell.KeyboardMode.OnDemand);
            }
            catch {}
            try
            {
                GtkWidgetSetFocusable(rawWindowHandle, true);
                GtkWidgetSetFocusOnClick(rawWindowHandle, true);
                if (rawGlAreaHandle != IntPtr.Zero)
                {
                    GtkWidgetSetFocusable(rawGlAreaHandle, true);
                    GtkWidgetSetFocusOnClick(rawGlAreaHandle, true);
                }
            }
            catch {}
        }
        UpdateInputRegion();
    }

    private void UpdateInputRegion()
    {
        if (_window == null || _glArea == null) return;

        IntPtr nativeWin = _window.Handle.DangerousGetHandle();
        if (nativeWin == IntPtr.Zero) return;

        IntPtr surface = GtkNativeGetSurface(nativeWin);
        if (surface == IntPtr.Zero) return;

        IntPtr region = CairoRegionCreate();
        if (region == IntPtr.Zero) return;

        try
        {
            if (!_clickThrough || ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup))
            {
                // Full screen region when overlay is unlocked OR a popup is open
                int w = _glArea.GetAllocatedWidth();
                int h = _glArea.GetAllocatedHeight();
                var rect = new CairoRectangleInt { X = 0, Y = 0, Width = w, Height = h };
                CairoRegionUnionRectangle(region, ref rect);
            }
            else
            {
                // When click-through is enabled (locked), the input region covers the unlock button
                var rect = new CairoRectangleInt { X = 10, Y = 10, Width = 32, Height = 32 };
                CairoRegionUnionRectangle(region, ref rect);

                // AND any active interactive panels (like the Minion Browser)
                foreach (var panel in PanelRegistry.ActivePanels)
                {
                    if (panel.IsVisible && panel.ShouldDisplay(_gameState) && panel.IsInteractive)
                    {
                        var panelRect = new CairoRectangleInt
                        {
                            X = (int)panel.ScreenPosition.X,
                            Y = (int)panel.ScreenPosition.Y,
                            Width = (int)panel.ScreenSize.X,
                            Height = (int)panel.ScreenSize.Y
                        };
                        CairoRegionUnionRectangle(region, ref panelRect);
                    }
                }
            }

            GdkSurfaceSetInputRegion(surface, region);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WaylandGtk4Window] Error updating input region: {ex.Message}");
        }
        finally
        {
            CairoRegionDestroy(region);
        }
    }

    private ImGuiKey ConvertGdkKeyToImGuiKey(uint keyval)
    {
        // Simple mapping for common keys
        return keyval switch
        {
            0xff08 => ImGuiKey.Backspace,
            0xff09 => ImGuiKey.Tab,
            0xff0d => ImGuiKey.Enter,
            0xff1b => ImGuiKey.Escape,
            0xff51 => ImGuiKey.LeftArrow,
            0xff52 => ImGuiKey.UpArrow,
            0xff53 => ImGuiKey.RightArrow,
            0xff54 => ImGuiKey.DownArrow,
            _ => ImGuiKey.None
        };
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void InitializeLayerShell(IntPtr rawWindowHandle)
    {
        Gtk4LayerShell.InitForToplevel(rawWindowHandle);
        Gtk4LayerShell.SetNamespace(rawWindowHandle, "notification");
        Gtk4LayerShell.SetLayer(rawWindowHandle, (int)Gtk4LayerShell.Layer.Overlay);
        
        // Anchor to all edges to span the full screen
        Gtk4LayerShell.SetAnchor(rawWindowHandle, (int)Gtk4LayerShell.Edge.Left, true);
        Gtk4LayerShell.SetAnchor(rawWindowHandle, (int)Gtk4LayerShell.Edge.Right, true);
        Gtk4LayerShell.SetAnchor(rawWindowHandle, (int)Gtk4LayerShell.Edge.Top, true);
        Gtk4LayerShell.SetAnchor(rawWindowHandle, (int)Gtk4LayerShell.Edge.Bottom, true);
        
        // Keyboard interaction mode OnDemand allows typing search queries and filters
        Gtk4LayerShell.SetKeyboardMode(rawWindowHandle, (int)Gtk4LayerShell.KeyboardMode.OnDemand);
    }

    public void Close()
    {
        _window?.Destroy();
    }

    public void Dispose()
    {
        Close();
    }
}
