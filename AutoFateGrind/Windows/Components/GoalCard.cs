using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

internal static class GoalCard
{
    public static bool Draw(string id, string label, FontAwesomeIcon icon, Vector4 accent, bool selected, Vector2 size, string? tooltip = null, bool disabled = false)
    {
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();
        var rawHovered = ImGui.IsMouseHoveringRect(origin, end);
        var hovered = !disabled && rawHovered;

        var bgColor = disabled ? Styling.CardBgSoft
            : selected ? Vector4.Lerp(Styling.CardBg, accent, 0.20f)
            : hovered ? Styling.CardBgHover : Styling.CardBgSoft;
        var borderColor = disabled ? Styling.BorderDim
            : selected ? accent : hovered ? accent * 0.55f : Styling.BorderDim;
        var borderThickness = !disabled && selected ? 2.5f : 1.2f;

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(bgColor), 8f);
        dl.AddRect(origin, end, ImGui.GetColorU32(borderColor), 8f, ImDrawFlags.None, borderThickness);

        var iconStr = icon.ToIconString();
        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            iconSize = ImGui.CalcTextSize(iconStr);

        var iconPos = new Vector2(
            origin.X + (size.X - iconSize.X) * 0.5f,
            origin.Y + size.Y * 0.28f - iconSize.Y * 0.5f);
        ImGui.SetCursorScreenPos(iconPos);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, disabled ? Styling.TextMuted : selected ? accent : Styling.TextSecondary))
            ImGui.TextUnformatted(iconStr);

        var labelColor = disabled ? Styling.TextMuted : selected ? Styling.TextStrong : Styling.TextSecondary;
        DrawCentered(label, origin, size, 0.62f, labelColor);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        if (rawHovered && !string.IsNullOrEmpty(tooltip))
            ImGui.SetTooltip(tooltip);

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                return true;
        }
        return false;
    }

    private static void DrawCentered(string text, Vector2 origin, Vector2 size, float yFraction, Vector4 color)
    {
        var maxWidth = size.X - 10f;
        var rawWidth = ImGui.CalcTextSize(text).X;
        var scale = rawWidth > maxWidth ? maxWidth / rawWidth : 1f;

        if (scale < 1f) ImGui.SetWindowFontScale(scale);
        var textSize = ImGui.CalcTextSize(text);
        var pos = new Vector2(origin.X + (size.X - textSize.X) * 0.5f, origin.Y + size.Y * yFraction);
        ImGui.SetCursorScreenPos(pos);
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
        if (scale < 1f) ImGui.SetWindowFontScale(1f);
    }
}
