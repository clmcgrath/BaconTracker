using System;
using System.Numerics;
using ImGuiNET;

namespace BaconTracker.App;

public class LuaImGui
{
    public void Text(string text) => ImGui.Text(text);

    public void TextColored(double r, double g, double b, double a, string text)
    {
        ImGui.TextColored(new Vector4((float)r, (float)g, (float)b, (float)a), text);
    }

    public void Separator() => ImGui.Separator();

    public void Spacing() => ImGui.Spacing();

    public void BulletText(string text) => ImGui.BulletText(text);

    public bool Button(string label) => ImGui.Button(label);

    public bool Checkbox(string label, bool value)
    {
        bool val = value;
        ImGui.Checkbox(label, ref val);
        return val;
    }

    public void ProgressBar(double fraction, string overlay)
    {
        ImGui.ProgressBar((float)fraction, new Vector2(-1, 0), overlay);
    }

    public void SameLine() => ImGui.SameLine();
}
