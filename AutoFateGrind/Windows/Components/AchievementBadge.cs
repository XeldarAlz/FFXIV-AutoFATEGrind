using AutoFateGrind.Core.Zones;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

internal static class AchievementBadge
{
    public static void Draw(ZoneInfo zone)
    {
        var label = !zone.Unlocked
            ? "Locked"
            : zone.AchievementMax > 0
                ? $"Date with Destiny: {zone.AchievementCurrent}/{zone.AchievementMax}"
                : "Date with Destiny progress unknown";
        var labelColor = zone.Unlocked ? Styling.TextSecondary : Styling.TextMuted;

        using (ImRaii.PushColor(ImGuiCol.Text, labelColor))
            ImGui.TextUnformatted(label);

        DrawProgressBar(zone.AchievementProgress, zone.Unlocked, zone.AchievementDone);
    }

    private static void DrawProgressBar(float fraction, bool active, bool done)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.AchievementBarHeight * ImGuiHelpers.GlobalScale;
        var end = origin + new Vector2(width, height);

        drawList.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBgSoft), 3f);
        if (fraction > 0)
        {
            var fillEnd = new Vector2(origin.X + width * fraction, end.Y);
            var fillColor = done
                ? Styling.AccentMint
                : active ? Styling.AccentTeal : Styling.BorderDim;
            drawList.AddRectFilled(origin, fillEnd, ImGui.GetColorU32(fillColor), 3f);
        }

        if (active)
        {
            var dark = ImGui.GetColorU32(new Vector4(0.08f, 0.06f, 0.04f, 1f));
            var label = done ? "DONE" : $"{(int)MathF.Round(fraction * 100f)}%";
            DrawCenteredBarText(drawList, origin, width, height, label, dark);
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawCenteredBarText(
        ImDrawListPtr drawList, Vector2 origin, float width, float height,
        string text, uint color)
    {
        var size = ImGui.CalcTextSize(text);
        var pos = new Vector2(
            origin.X + (width - size.X) * 0.5f,
            origin.Y + (height - size.Y) * 0.5f);
        drawList.AddText(pos, color, text);
    }
}
