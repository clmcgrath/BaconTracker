using System;
using System.Numerics;
using ImGuiNET;

namespace BaconTracker.App;

public class CounterPanel : OverlayPanel
{
    public string Label { get; set; } = "Counter";
    public int Value { get; set; } = 0;
    public string? IconPath { get; set; }
    public Vector4 ValueColor { get; set; } = new Vector4(0.2f, 0.8f, 0.2f, 1.0f); // Default light green

    public CounterPanel(string title) : base(title)
    {
        DefaultSize = new Vector2(220, 90);
    }

    protected override void OnDraw()
    {
        // 1. Draw Icon if provided
        if (!string.IsNullOrEmpty(IconPath))
        {
            IntPtr textureId = LoadTexture(IconPath);
            if (textureId != IntPtr.Zero)
            {
                ImGui.Image(textureId, new Vector2(32, 32));
                ImGui.SameLine();
            }
        }

        // 2. Lay out the Counter Text
        Vector2 startPos = ImGui.GetCursorPos();
        
        ImGui.Text(Label);
        
        // Render the value in a large font
        ImGui.SetWindowFontScale(2.0f);
        ImGui.PushStyleColor(ImGuiCol.Text, ValueColor);
        ImGui.Text($"{Value}");
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1.0f); // Reset
    }
}
