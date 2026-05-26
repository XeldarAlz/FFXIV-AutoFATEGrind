using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

internal static class GoalCard
{
    public static bool Draw(string id, string label, FontAwesomeIcon icon, Vector4 accent, bool selected, Vector2 size)
    {
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();
        var hovered = ImGui.IsMouseHoveringRect(origin, end);

        var bgColor = selected
            ? Vector4.Lerp(Styling.CardBg, accent, 0.20f)
            : hovered ? Styling.CardBgHover : Styling.CardBgSoft;
        var borderColor = selected ? accent : hovered ? accent * 0.55f : Styling.BorderDim;
        var borderThickness = selected ? 2.5f : 1.2f;

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
        using (ImRaii.PushColor(ImGuiCol.Text, selected ? accent : Styling.TextSecondary))
            ImGui.TextUnformatted(iconStr);

        var labelSize = ImGui.CalcTextSize(label);
        var labelPos = new Vector2(
            origin.X + (size.X - labelSize.X) * 0.5f,
            origin.Y + size.Y * 0.68f);
        ImGui.SetCursorScreenPos(labelPos);
        using (ImRaii.PushColor(ImGuiCol.Text, selected ? Styling.TextStrong : Styling.TextSecondary))
            ImGui.TextUnformatted(label);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                return true;
        }
        return false;
    }
}
