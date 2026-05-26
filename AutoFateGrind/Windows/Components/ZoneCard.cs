using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

internal static class ZoneCard
{
    public static void Draw(ZoneInfo zone, AutoFateController controller, Configuration cfg)
    {
        var selected = cfg.SelectedZones.Contains(zone.TerritoryId);
        var selectable = zone.Unlocked && !zone.AchievementDone && !controller.Running;

        var startScreen = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.ZoneCardHeight * ImGuiHelpers.GlobalScale;
        var endScreen = startScreen + new Vector2(width, height);
        var hovered = ImGui.IsMouseHoveringRect(startScreen, endScreen);

        var border = ResolveBorder(zone, selected, controller.Running);
        var bg = ResolveBg(zone, selected, hovered);

        using (Card.Begin($"##zone_{zone.TerritoryId}", new Vector2(-1, height), bg, border, selected ? 1.8f : 1.2f))
        {
            DrawHeader(zone);
            ImGui.Spacing();
            AchievementBadge.Draw(zone);
        }

        if (hovered)
        {
            DrawTooltip(zone, selected);
            if (selectable)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    if (selected) cfg.SelectedZones.Remove(zone.TerritoryId);
                    else if (!cfg.SelectedZones.Contains(zone.TerritoryId))
                        cfg.SelectedZones.Add(zone.TerritoryId);
                    cfg.SaveDebounced();
                }
            }
        }
    }

    private static void DrawHeader(ZoneInfo zone)
    {
        ZoneIcon.Draw(zone);
        ImGui.SameLine();

        ImGui.SetWindowFontScale(1.10f);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(zone.Name);
        }
        ImGui.SetWindowFontScale(1.0f);

        var pillLabel = ActiveCountPill.GetLabel(zone);
        var pillWidth = ImGui.CalcTextSize(pillLabel).X + 16 * ImGuiHelpers.GlobalScale;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - pillWidth);
        ActiveCountPill.Draw(zone);
    }

    private static void DrawTooltip(ZoneInfo zone, bool selected)
    {
        using var tt = ImRaii.Tooltip();
        if (!zone.Unlocked)
            ImGui.TextUnformatted($"Zone not yet unlocked. Reach level {zone.MinLevel} content first.");
        else if (zone.AchievementDone)
            ImGui.TextUnformatted("Date with Destiny achievement already complete in this zone.");
        else if (zone.ActiveFateCount <= 0)
            ImGui.TextUnformatted($"No FATEs active in {zone.Name} right now. Selecting it queues it; the plugin will rotate to it.");
        else if (selected)
            ImGui.TextUnformatted($"{zone.ActiveFateCount} FATE(s) active. Click to remove from batch run.");
        else
            ImGui.TextUnformatted($"{zone.ActiveFateCount} FATE(s) active. Click to add to batch run.");
    }

    private static Vector4 ResolveBg(ZoneInfo zone, bool selected, bool hovered)
    {
        if (!zone.Unlocked) return Styling.CardBg * 0.6f;
        if (zone.AchievementDone) return Styling.CardBg * 0.6f;
        if (selected && hovered) return Vector4.Lerp(Styling.CardBgHover, Styling.AccentTeal, 0.15f);
        if (selected) return Vector4.Lerp(Styling.CardBg, Styling.AccentTeal, 0.10f);
        if (hovered) return Styling.CardBgHover;
        return Vector4.Lerp(Styling.CardBg, Styling.ExpansionTint(zone.Expansion), 1f);
    }

    private static Vector4 ResolveBorder(ZoneInfo zone, bool selected, bool running)
    {
        if (!zone.Unlocked) return Styling.BorderLocked;
        if (running) return Styling.PulseColor(Styling.BorderActive, Styling.AccentTealSoft, Styling.PulseMedium);
        if (selected) return Styling.AccentTeal;
        if (zone.AchievementDone) return Styling.BorderDim;
        return Styling.BorderActive * 0.65f;
    }
}
