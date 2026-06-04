using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace BaconTracker.App;

public class InfoPanel : OverlayPanel
{
    public class InfoRow
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public Vector4 Color { get; set; }

        public InfoRow(string key, string value, Vector4 color)
        {
            Key = key;
            Value = value;
            Color = color;
        }
    }

    private readonly List<InfoRow> _rows = new();
    private readonly object _lock = new();

    public InfoPanel(string title) : base(title)
    {
        DefaultSize = new Vector2(300, 200);
    }

    public void AddRow(string key, string value)
    {
        AddRow(key, value, new Vector4(1, 1, 1, 1));
    }

    public void AddRow(string key, string value, Vector4 color)
    {
        lock (_lock)
        {
            _rows.Add(new InfoRow(key, value, color));
        }
    }

    public void ClearRows()
    {
        lock (_lock)
        {
            _rows.Clear();
        }
    }

    protected override void OnDraw()
    {
        if (ImGui.BeginTable("InfoPanelTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            // Setup columns
            ImGui.TableSetupColumn("Property");
            ImGui.TableSetupColumn("Value");
            ImGui.TableHeadersRow();

            lock (_lock)
            {
                foreach (var row in _rows)
                {
                    ImGui.TableNextRow();
                    
                    // Col 0: Key
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text(row.Key);

                    // Col 1: Value (Colored)
                    ImGui.TableSetColumnIndex(1);
                    ImGui.PushStyleColor(ImGuiCol.Text, row.Color);
                    ImGui.Text(row.Value);
                    ImGui.PopStyleColor();
                }
            }

            ImGui.EndTable();
        }
    }
}
