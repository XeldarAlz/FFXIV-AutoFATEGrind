using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

// One row in LiveFateTracker. v1 reads straight from IFate; once FateScanner is in,
// it will pass an enriched record with eligibility reasons + distance.
internal static class FateRow
{
    public static void Draw(IFate fate)
    {
        var progress = fate.Progress / 100f;
        var label = $"L{fate.Level}  {fate.Name}";
        var timeLeft = fate.TimeRemaining;

        var rowHeight = Layout.FateRowHeight * ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var end = origin + new Vector2(width, rowHeight);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBgSoft), 4f);

        var fillEnd = new Vector2(origin.X + width * Math.Clamp(progress, 0f, 1f), end.Y);
        var fillColor = fate.HasBonus ? Styling.AccentAmber : Styling.AccentTeal;
        drawList.AddRectFilled(origin, fillEnd, ImGui.GetColorU32(fillColor * 0.55f), 4f);

        var textPadX = 8f * ImGuiHelpers.GlobalScale;
        var textY = origin.Y + (rowHeight - ImGui.GetTextLineHeight()) * 0.5f;
        var textColor = ImGui.GetColorU32(Styling.TextStrong);
        drawList.AddText(new Vector2(origin.X + textPadX, textY), textColor, label);

        var rightLabel = $"{(int)progress * 100}%  {FormatTime(timeLeft)}";
        var rightSize = ImGui.CalcTextSize(rightLabel);
        drawList.AddText(new Vector2(end.X - rightSize.X - textPadX, textY), textColor, rightLabel);

        ImGui.Dummy(new Vector2(width, rowHeight));
    }

    private static string FormatTime(long secs)
    {
        if (secs <= 0) return "--:--";
        var m = secs / 60;
        var s = secs % 60;
        return $"{m}:{s:D2}";
    }
}
