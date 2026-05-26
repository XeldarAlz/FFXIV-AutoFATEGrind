using AutoFateGrind;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections;

internal static class ZonePicker
{
    public static void Draw(Configuration cfg, AutoFateController controller)
    {
        var scopedToShared = IsSharedScoped(cfg.Mode) && !cfg.ShowAllZonesOverride;

        // Eager refresh so the header's completed-count covers collapsed groups too.
        if (cfg.Mode == GrindMode.MaxFates)
            foreach (var z in ZoneRegistry.Zones)
                if (z.AchievementId != 0) ZoneStateReader.Refresh(z);

        DrawHeader(cfg, scopedToShared);

        foreach (var exp in Enum.GetValues<ExpansionKind>().Reverse())
        {
            if (scopedToShared && exp < ExpansionKind.ShB) continue;

            var zones = ZoneRegistry.ByExpansion(exp).ToArray();
            if (zones.Length == 0) continue;

            DrawGroup(exp, zones, cfg, controller);
        }
    }

    private static void DrawHeader(Configuration cfg, bool scopedToShared)
    {
        var label = scopedToShared
            ? "Zones  (Shared FATE only: ShB / EW / DT)"
            : "Zones  (all expansions)";
        Styling.SectionLabel(label);

        if (IsSharedScoped(cfg.Mode))
        {
            ImGui.SameLine();
            var btnLabel = cfg.ShowAllZonesOverride
                ? "Hide non-Shared FATE zones"
                : "Show all zones manually";
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentVioletSoft))
                if (ImGui.SmallButton($"  {btnLabel}  "))
                {
                    cfg.ShowAllZonesOverride = !cfg.ShowAllZonesOverride;
                    cfg.SaveDebounced();
                }
        }

        if (cfg.Mode == GrindMode.MaxFates)
        {
            var doneCount = ZoneRegistry.Zones.Count(z => z.AchievementDone);
            if (doneCount > 0)
            {
                ImGui.SameLine();
                var t = cfg.ShowCompletedZones
                    ? $"Hide {doneCount} completed"
                    : $"Show {doneCount} completed";
                using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
                    if (ImGui.SmallButton($"  {t}  "))
                    {
                        cfg.ShowCompletedZones = !cfg.ShowCompletedZones;
                        cfg.SaveDebounced();
                    }
            }
        }
        ImGui.Spacing();
    }

    private static void DrawGroup(ExpansionKind exp, ZoneInfo[] zones, Configuration cfg, AutoFateController controller)
    {
        var hideCompleted = cfg.Mode == GrindMode.MaxFates && !cfg.ShowCompletedZones;
        var visible = hideCompleted ? zones.Where(z => !z.AchievementDone).ToArray() : zones;

        if (hideCompleted && visible.Length == 0) return;

        var territoryIds = zones.Select(z => z.TerritoryId).ToHashSet();
        var selected = cfg.SelectedZones.Count(territoryIds.Contains);
        var allSelected = selected == zones.Length;

        var header = $"{exp.DisplayName()}  ({selected}/{zones.Length})###grp_{exp}";
        var open = ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen);

        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - 56f * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(controller.Running))
            if (ImGui.SmallButton($"{(allSelected ? "Clear" : "All")}##sel_{exp}"))
            {
                if (allSelected) cfg.SelectedZones.RemoveAll(territoryIds.Contains);
                else foreach (var id in territoryIds)
                    if (!cfg.SelectedZones.Contains(id)) cfg.SelectedZones.Add(id);
                cfg.SaveDebounced();
            }

        if (!open) return;

        foreach (var zone in visible)
        {
            ZoneStateReader.Refresh(zone);
            DrawRow(zone, cfg, controller);
        }
        ImGui.Spacing();
    }

    private static void DrawRow(ZoneInfo zone, Configuration cfg, AutoFateController controller)
    {
        var sel = cfg.SelectedZones.Contains(zone.TerritoryId);
        var achievementBlocks = cfg.Mode == GrindMode.MaxFates && zone.AchievementDone;
        var disabled = controller.Running || !zone.Unlocked || achievementBlocks;

        ImGui.Indent(8f);

        using (ImRaii.Disabled(disabled))
        {
            var b = sel;
            if (ImGui.Checkbox($"##z_{zone.TerritoryId}", ref b))
            {
                if (b && !cfg.SelectedZones.Contains(zone.TerritoryId))
                    cfg.SelectedZones.Add(zone.TerritoryId);
                else if (!b)
                    cfg.SelectedZones.Remove(zone.TerritoryId);
                cfg.SaveDebounced();
            }
        }

        if (disabled && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(DisabledReason(controller.Running, zone.Unlocked, achievementBlocks));

        ImGui.SameLine();
        var nameColor = (zone.Unlocked, zone.AchievementDone) switch
        {
            (false, _) => Styling.TextMuted,
            (_, true)  => Styling.AccentMint,
            _          => Styling.TextStrong,
        };
        using (ImRaii.PushColor(ImGuiCol.Text, nameColor))
            ImGui.TextUnformatted(zone.Name);

        if (zone.ActiveFateCount > 0)
        {
            var pill = $"{zone.ActiveFateCount}";
            var w = ImGui.CalcTextSize(pill).X + 14f * ImGuiHelpers.GlobalScale;
            ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - w);
            using (ImRaii.PushFont(UiBuilder.IconFont))
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
                ImGui.TextUnformatted(FontAwesomeIcon.Bolt.ToIconString());
            ImGui.SameLine(0, 4f);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
                ImGui.TextUnformatted(pill);
        }

        ImGui.Unindent(8f);
    }

    private static bool IsSharedScoped(GrindMode mode)
        => mode is GrindMode.MaxGemstones or GrindMode.MaxFates;

    private static string DisabledReason(bool running, bool unlocked, bool achievementBlocks)
    {
        if (running) return "Stop the runner to change zone selection.";
        if (!unlocked) return "Locked — attune an aetheryte in this zone first.";
        if (achievementBlocks) return "Free Market Friend already complete here. Switch goal to grind this zone.";
        return "";
    }
}
