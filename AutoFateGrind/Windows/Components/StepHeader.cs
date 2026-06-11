using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

// Numbered step marker for the guided main-window flow: a small violet badge + uppercase title, with an
// optional right-aligned status (e.g. "3 selected"). Hand-drawn so the badge and title share a baseline.
internal static class StepHeader
{
    public static void Draw(int number, string title, string? rightText = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var lineH = ImGui.GetTextLineHeight();
        var diameter = lineH + 6f * scale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var midY = origin.Y + diameter * 0.5f;
        var dl = ImGui.GetWindowDrawList();

        var center = new Vector2(origin.X + diameter * 0.5f, midY);
        var radius = diameter * 0.5f;
        dl.AddCircleFilled(center, radius, ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, Styling.AccentViolet, 0.55f)));
        dl.AddCircle(center, radius, ImGui.GetColorU32(Styling.AccentViolet), 0, 1.4f * scale);

        var num = number.ToString();
        var ns = ImGui.CalcTextSize(num);
        dl.AddText(new Vector2(center.X - ns.X * 0.5f, midY - ns.Y * 0.5f), ImGui.GetColorU32(Styling.TextStrong), num);

        var up = title.ToUpperInvariant();
        var ts = ImGui.CalcTextSize(up);
        dl.AddText(new Vector2(origin.X + diameter + 8f * scale, midY - ts.Y * 0.5f),
            ImGui.GetColorU32(Styling.TextStrong), up);

        if (!string.IsNullOrEmpty(rightText))
        {
            var rs = ImGui.CalcTextSize(rightText);
            dl.AddText(new Vector2(origin.X + width - rs.X, midY - rs.Y * 0.5f),
                ImGui.GetColorU32(Styling.TextDim), rightText);
        }

        ImGui.Dummy(new Vector2(width, diameter));
    }
}
