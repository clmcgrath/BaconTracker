using System;
using System.IO;
using System.Numerics;
using ImGuiNET;
using MoonSharp.Interpreter;
using BaconTracker.Core;

namespace BaconTracker.App;

public class LuaScriptedPanel : OverlayPanel
{
    private readonly Script _script;
    private readonly DynValue? _onDrawCallback;
    private readonly DynValue? _shouldDisplayCallback;
    private readonly LuaTrackerProxy _trackerProxy;
    private bool _isInteractive = false;

    public override bool IsInteractive => _isInteractive;

    public System.Collections.Generic.IReadOnlyList<IOverlayPanel> CustomPanels => _trackerProxy.RegisteredPanels;

    public bool HasOnDrawCallback => _onDrawCallback != null && _onDrawCallback.Type == DataType.Function;

    static LuaScriptedPanel()
    {
        // Register types with MoonSharp UserData registry
        UserData.RegisterType<LuaImGui>();
        UserData.RegisterType<LuaGameProxy>();
        UserData.RegisterType<LuaTrackerProxy>();
        UserData.RegisterType<OverlayPanel>();
        UserData.RegisterType<CounterPanel>();
        UserData.RegisterType<InfoPanel>();
        UserData.RegisterType<LuaScriptedPanel>();
        UserData.RegisterType<PanelAnchor>();

        // Register custom table converters for Vector2 and Vector4
        Script.GlobalOptions.CustomConverters.SetScriptToClrCustomConversion(DataType.Table, typeof(Vector2), val =>
        {
            var table = val.Table;
            float x = (float)(table.Get(1).CastToNumber() ?? 0.0);
            float y = (float)(table.Get(2).CastToNumber() ?? 0.0);
            return new Vector2(x, y);
        });

        Script.GlobalOptions.CustomConverters.SetScriptToClrCustomConversion(DataType.Table, typeof(Vector4), val =>
        {
            var table = val.Table;
            float r = (float)(table.Get(1).CastToNumber() ?? 1.0);
            float g = (float)(table.Get(2).CastToNumber() ?? 1.0);
            float b = (float)(table.Get(3).CastToNumber() ?? 1.0);
            float a = (float)(table.Get(4).CastToNumber() ?? 1.0);
            return new Vector4(r, g, b, a);
        });
    }

    public LuaScriptedPanel(string luaFilePath) : base(Path.GetFileNameWithoutExtension(luaFilePath))
    {
        if (!File.Exists(luaFilePath))
        {
            throw new FileNotFoundException("Lua script not found", luaFilePath);
        }

        // Default layout parameters for scripted panel
        DefaultPosition = new Vector2(150, 150);
        DefaultSize = new Vector2(300, 200);

        // Initialize script engine with standard sandboxed core modules
        _script = new Script(CoreModules.Preset_SoftSandbox);

        // Expose our APIs
        _trackerProxy = new LuaTrackerProxy(this);
        _script.Globals["ImGui"] = new LuaImGui();
        _script.Globals["Game"] = new LuaGameProxy();
        _script.Globals["Tracker"] = _trackerProxy;
        _script.Globals["PanelAnchor"] = typeof(PanelAnchor);

        // Bind panel templates as factory methods (supporting both string title and declarative table constructors)
        _script.Globals["CounterPanel"] = (Func<DynValue, CounterPanel>)(val =>
        {
            if (val.Type == DataType.Table)
            {
                var table = val.Table;
                string title = table.Get("title").String ?? "Counter";
                var panel = new CounterPanel(title);
                ConfigureBasePanel(panel, table);
                
                var labelVal = table.Get("label");
                if (labelVal.Type == DataType.String) panel.Label = labelVal.String;
                
                var valueVal = table.Get("value");
                if (valueVal.Type == DataType.Number) panel.Value = (int)valueVal.Number;
                
                var colorVal = table.Get("valueColor");
                if (colorVal.Type == DataType.Table) panel.ValueColor = colorVal.ToObject<Vector4>();
                
                var iconVal = table.Get("iconPath");
                if (iconVal.Type == DataType.String) panel.IconPath = iconVal.String;
                
                return panel;
            }
            return new CounterPanel(val.String ?? "Counter");
        });

        _script.Globals["InfoPanel"] = (Func<DynValue, InfoPanel>)(val =>
        {
            if (val.Type == DataType.Table)
            {
                var table = val.Table;
                string title = table.Get("title").String ?? "Info";
                var panel = new InfoPanel(title);
                ConfigureBasePanel(panel, table);
                
                var rowsVal = table.Get("rows");
                if (rowsVal.Type == DataType.Table)
                {
                    foreach (var rowVal in rowsVal.Table.Values)
                    {
                        if (rowVal.Type == DataType.Table)
                        {
                            var row = rowVal.Table;
                            string k = row.Get("key").String ?? "";
                            string v = row.Get("value").String ?? "";
                            var colorVal = row.Get("color");
                            if (colorVal.Type == DataType.Table)
                            {
                                panel.AddRow(k, v, colorVal.ToObject<Vector4>());
                            }
                            else
                            {
                                panel.AddRow(k, v);
                            }
                        }
                    }
                }
                return panel;
            }
            return new InfoPanel(val.String ?? "Info");
        });

        try
        {
            // Execute the script to evaluate metadata and define functions
            string code = File.ReadAllText(luaFilePath);
            _script.DoString(code);

            // Read plugin metadata from global 'plugin' table if present
            var pluginTable = _script.Globals.Get("plugin");
            if (pluginTable.Type == DataType.Table)
            {
                var table = pluginTable.Table;
                
                string? name = table.Get("name").String;
                if (!string.IsNullOrEmpty(name))
                {
                    Title = name;
                }

                var isVisibleVal = table.Get("is_visible");
                if (isVisibleVal.Type == DataType.Boolean)
                {
                    IsVisible = isVisibleVal.Boolean;
                }

                var isInteractiveVal = table.Get("is_interactive");
                if (isInteractiveVal.Type == DataType.Boolean)
                {
                    _isInteractive = isInteractiveVal.Boolean;
                }

                double defX = table.Get("default_x").Number;
                double defY = table.Get("default_y").Number;
                if (defX > 0 || defY > 0)
                {
                    DefaultPosition = new Vector2((float)defX, (float)defY);
                }

                double defW = table.Get("default_width").Number;
                double defH = table.Get("default_height").Number;
                if (defW > 0 || defH > 0)
                {
                    DefaultSize = new Vector2((float)defW, (float)defH);
                }
            }

            // Get reference to the OnDraw function callback
            _onDrawCallback = _script.Globals.Get("OnDraw");
            if (_onDrawCallback == null || _onDrawCallback.Type != DataType.Function)
            {
                Console.WriteLine($"[LuaScriptedPanel] Warning: Script '{luaFilePath}' does not define an 'OnDraw' function.");
            }

            // Get reference to the ShouldDisplay function callback
            _shouldDisplayCallback = _script.Globals.Get("PluginShouldDisplay");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LuaScriptedPanel] Error loading script '{luaFilePath}': {ex.Message}");
            Title += " (Load Error)";
        }
    }

    public override bool ShouldDisplay(GameState state)
    {
        if (_shouldDisplayCallback != null && _shouldDisplayCallback.Type == DataType.Function)
        {
            try
            {
                var result = _script.Call(_shouldDisplayCallback, state);
                return result.Boolean;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LuaScriptedPanel] Error evaluating ShouldDisplay for '{Title}': {ex.Message}");
                return true; // Default fallback
            }
        }
        return base.ShouldDisplay(state);
    }

    protected override void OnDraw()
    {
        if (_onDrawCallback != null && _onDrawCallback.Type == DataType.Function)
        {
            try
            {
                // Run the script's OnDraw function
                _script.Call(_onDrawCallback);
            }
            catch (ScriptRuntimeException ex)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Lua Script Error:");
                ImGui.TextWrapped(ex.DecoratedMessage ?? ex.Message);
            }
            catch (Exception ex)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "C# Script Host Error:");
                ImGui.TextWrapped(ex.Message);
            }
        }
    }

    private static void ConfigureBasePanel(OverlayPanel panel, Table table)
    {
        var visibleVal = table.Get("visible");
        if (visibleVal.Type == DataType.Boolean) panel.IsVisible = visibleVal.Boolean;
        
        var positionVal = table.Get("position");
        if (positionVal.Type == DataType.Table) panel.DefaultPosition = positionVal.ToObject<Vector2>();
        
        var sizeVal = table.Get("size");
        if (sizeVal.Type == DataType.Table) panel.DefaultSize = sizeVal.ToObject<Vector2>();
        
        var anchorVal = table.Get("anchor");
        if (anchorVal.Type == DataType.Number)
        {
            panel.Anchor = (PanelAnchor)(int)anchorVal.Number;
        }
        else if (anchorVal.Type == DataType.UserData)
        {
            try
            {
                panel.Anchor = anchorVal.ToObject<PanelAnchor>();
            }
            catch {}
        }
        
        var anchorOffsetVal = table.Get("anchorOffset");
        if (anchorOffsetVal.Type == DataType.Table) panel.AnchorOffset = anchorOffsetVal.ToObject<Vector2>();
    }
}
