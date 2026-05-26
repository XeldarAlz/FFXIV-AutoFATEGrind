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

        // Eager refresh so the tab badges' completed-count covers collapsed expansions too.
        if (cfg.Mode == GrindMode.MaxFates)
            foreach (var z in ZoneRegistry.Zones)
                if (z.AchievementId != 0) ZoneStateReader.Refresh(z);

        using var tabBar = ImRaii.TabBar("##afg_main_tabs", ImGuiTabBarFlags.NoTooltip | ImGuiTabBarFlags.FittingPolicyScroll);
        if (!tabBar) return;

        DrawQueueTab(cfg, controller);

        foreach (var exp in Enum.GetValues<ExpansionKind>().Reverse())
        {
            if (scopedToShared && exp < ExpansionKind.ShB) continue;

            var zones = ZoneRegistry.ByExpansion(exp).ToArray();
            if (zones.Length == 0) continue;

            DrawExpansionTab(exp, zones, cfg, controller);
        }
    }

    private static void DrawQueueTab(Configuration cfg, AutoFateController controller)
    {
        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        var queueCount = cfg.SelectedZones.Count(byId.ContainsKey);
        var label = queueCount > 0 ? $"Queue  {queueCount}###tab_queue" : "Queue###tab_queue";

        using var tab = ImRaii.TabItem(label);
        if (!tab) return;

        ImGui.Spacing();
        SelectionOrder.Draw(cfg, controller);
    }

    private static void DrawExpansionTab(ExpansionKind exp, ZoneInfo[] zones, Configuration cfg, AutoFateController controller)
    {
        var territoryIds = zones.Select(z => z.TerritoryId).ToHashSet();
        var selected = cfg.SelectedZones.Count(territoryIds.Contains);

        var label = $"{exp.ShortName()}  {selected}/{zones.Length}###tab_{exp}";

        using var tab = ImRaii.TabItem(label);
        if (!tab) return;

        ImGui.Spacing();
        DrawExpansionToolbar(exp, zones, territoryIds, selected, cfg, controller);
        ImGui.Spacing();

        var hideCompleted = cfg.Mode == GrindMode.MaxFates && !cfg.ShowCompletedZones;
        var visible = hideCompleted ? zones.Where(z => !z.AchievementDone).ToArray() : zones;

        if (visible.Length == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted(hideCompleted
                    ? "All zones in this expansion are complete."
                    : "No zones available.");
            return;
        }

        foreach (var zone in visible)
        {
            ZoneStateReader.Refresh(zone);
            DrawRow(zone, cfg, controller);
        }
    }

    private static void DrawExpansionToolbar(
        ExpansionKind exp, ZoneInfo[] zones, HashSet<uint> territoryIds, int selected,
        Configuration cfg, AutoFateController controller)
    {
        var allSelected = selected == zones.Length;
        var noneSelected = selected == 0;

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted($"{selected} of {zones.Length} selected");

        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var rightLabel = allSelected ? "Clear all" : "Select all";
        var rightWidth = ImGui.CalcTextSize(rightLabel).X + ImGui.GetStyle().FramePadding.X * 2;

        // "Hide completed" appears only on the Shared-FATE expansions during MaxFates.
        var canHideCompleted = cfg.Mode == GrindMode.MaxFates && zones.Any(z => z.AchievementDone);
        string? toggleLabel = canHideCompleted
            ? (cfg.ShowCompletedZones ? "Hide done" : "Show done")
            : null;
        var toggleWidth = toggleLabel is null
            ? 0
            : ImGui.CalcTextSize(toggleLabel).X + ImGui.GetStyle().FramePadding.X * 2 + spacingX;

        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - rightWidth - toggleWidth);

        if (toggleLabel is not null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
                if (ImGui.SmallButton($"{toggleLabel}##done_{exp}"))
                {
                    cfg.ShowCompletedZones = !cfg.ShowCompletedZones;
                    cfg.SaveDebounced();
                }
            ImGui.SameLine();
        }

        using (ImRaii.Disabled(controller.Running))
        using (ImRaii.PushColor(ImGuiCol.Text, allSelected ? Styling.AccentRose : Styling.AccentVioletSoft))
            if (ImGui.SmallButton($"{rightLabel}##sel_{exp}"))
            {
                if (allSelected) cfg.SelectedZones.RemoveAll(territoryIds.Contains);
                else foreach (var id in territoryIds)
                    if (!cfg.SelectedZones.Contains(id)) cfg.SelectedZones.Add(id);
                cfg.SaveDebounced();
            }
    }

    private static void DrawRow(ZoneInfo zone, Configuration cfg, AutoFateController controller)
    {
        var sel = cfg.SelectedZones.Contains(zone.TerritoryId);
        var achievementBlocks = cfg.Mode == GrindMode.MaxFates && zone.AchievementDone;
        var disabled = controller.Running || !zone.Unlocked || achievementBlocks;

        ImGui.Indent(6f);

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

        ImGui.Unindent(6f);
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
