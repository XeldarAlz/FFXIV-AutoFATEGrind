using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

internal static class SettingsRow
{
    private const float RowHeight = 36f;
    private const float HelpIconGap = 7f;
    private const float TooltipWrapWidth = 300f;
    private const float CaptionFontScale = 0.92f;
    private const float CaptionPullUp = 7f;
    private const float CaptionBottomGap = 8f;
    private const float BlockBottomGap = 6f;
    private const float NoteTopGap = 4f;
    private const float NoteBottomGap = 6f;

    public const float ToggleHeight = 20f;

    private readonly record struct RowArea(Vector2 Origin, float RightEdge, float MiddleY, bool Hovered)
    {
        public float Width => RightEdge - Origin.X;
    }

    public static void Draw(string label, string? help, float controlWidth, Action drawControl, float controlHeight = 0f)
    {
        var area = BeginRow();

        DrawTopDivider(area);
        var labelHovered = DrawLabel(area, label);
        var iconHovered = DrawHelpIcon(area, label, help);

        if (!string.IsNullOrEmpty(help) && (labelHovered || iconHovered))
        {
            HelpTooltip(help);
        }

        DrawControl(area, controlWidth, controlHeight, drawControl);
        EndRow(area);
    }

    // A label header (with hover help) followed by free-flowing block content for lists, radios,
    // add-item rows, and anything that won't fit a single right-aligned control.
    public static void DrawBlock(string label, string? help, Action drawContent)
    {
        Draw(label, help, 0f, static () => { });
        drawContent();
        Styling.VSpace(BlockBottomGap);
    }

    public static void Caption(string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var cursor = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(cursor with { Y = cursor.Y - CaptionPullUp * scale });

        var wrapLocalX = ImGui.GetCursorPosX() + (SettingsGroup.ContentRightEdge - ImGui.GetCursorScreenPos().X);
        ImGui.SetWindowFontScale(CaptionFontScale);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
        {
            ImGui.PushTextWrapPos(wrapLocalX);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }

        ImGui.SetWindowFontScale(1f);
        Styling.VSpace(CaptionBottomGap);
    }

    // Card-padded wrapped text for inline notes, warnings, and previews inside a group.
    public static void Note(string text, Vector4? color = null)
    {
        Styling.VSpace(NoteTopGap);
        var wrapLocalX = ImGui.GetCursorPosX() + (SettingsGroup.ContentRightEdge - ImGui.GetCursorScreenPos().X);
        ImGui.PushTextWrapPos(wrapLocalX);
        using (ImRaii.PushColor(ImGuiCol.Text, color ?? Styling.TextMuted))
        {
            ImGui.TextUnformatted(text);
        }

        ImGui.PopTextWrapPos();
        Styling.VSpace(NoteBottomGap);
    }

    public static void HelpTooltip(string help)
    {
        using (ImRaii.Tooltip())
        {
            ImGui.PushTextWrapPos(TooltipWrapWidth * ImGuiHelpers.GlobalScale);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
            {
                ImGui.TextUnformatted(help);
            }

            ImGui.PopTextWrapPos();
        }
    }

    private static RowArea BeginRow()
    {
        var origin = ImGui.GetCursorScreenPos();
        var rightEdge = SettingsGroup.ContentRightEdge;
        var rowHeight = RowHeight * ImGuiHelpers.GlobalScale;
        var hovered = ImGui.IsMouseHoveringRect(origin, origin + new Vector2(rightEdge - origin.X, rowHeight));
        return new RowArea(origin, rightEdge, origin.Y + rowHeight * 0.5f, hovered);
    }

    private static void DrawTopDivider(RowArea area)
    {
        if (SettingsGroup.RowDrawnInGroup)
        {
            ImGui.GetWindowDrawList().AddLine(area.Origin, area.Origin with { X = area.RightEdge },
                ImGui.GetColorU32(Styling.Hairline), 1f);
        }

        SettingsGroup.RowDrawnInGroup = true;
    }

    private static bool DrawLabel(RowArea area, string label)
    {
        var labelSize = ImGui.CalcTextSize(label);
        ImGui.SetCursorScreenPos(new Vector2(area.Origin.X, area.MiddleY - labelSize.Y * 0.5f));
        using (ImRaii.PushColor(ImGuiCol.Text, area.Hovered ? Styling.TextStrong : Styling.TextSecondary))
        {
            ImGui.TextUnformatted(label);
        }

        return ImGui.IsItemHovered();
    }

    private static bool DrawHelpIcon(RowArea area, string label, string? help)
    {
        if (string.IsNullOrEmpty(help) || !area.Hovered)
        {
            return false;
        }

        var labelWidth = ImGui.CalcTextSize(label).X;
        var iconString = FontAwesomeIcon.InfoCircle.ToIconString();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var iconSize = ImGui.CalcTextSize(iconString);
            ImGui.SetCursorScreenPos(new Vector2(
                area.Origin.X + labelWidth + HelpIconGap * ImGuiHelpers.GlobalScale,
                area.MiddleY - iconSize.Y * 0.5f));
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.WithAlpha(Styling.TextMuted, 0.9f)))
            {
                ImGui.TextUnformatted(iconString);
            }
        }

        return ImGui.IsItemHovered();
    }

    private static void DrawControl(RowArea area, float controlWidth, float controlHeight, Action drawControl)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var resolvedHeight = controlHeight > 0f ? controlHeight * scale : ImGui.GetFrameHeight();
        ImGui.SetCursorScreenPos(new Vector2(area.RightEdge - controlWidth * scale, area.MiddleY - resolvedHeight * 0.5f));
        drawControl();
    }

    private static void EndRow(RowArea area)
    {
        ImGui.SetCursorScreenPos(area.Origin);
        ImGui.Dummy(new Vector2(area.Width, RowHeight * ImGuiHelpers.GlobalScale));
    }
}
