using AutoFateGrind;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class ZonePicker
{
    public static void Draw(Configuration cfg, AutoFateController controller)
    {
        QueueStrip.Draw(cfg, controller);
        ImGui.Spacing();
        Divider();
        ImGui.Spacing();

        using var tabBar = ImRaii.TabBar("##afg_main_tabs", ImGuiTabBarFlags.NoTooltip | ImGuiTabBarFlags.FittingPolicyScroll);
        if (!tabBar) return;

        foreach (var exp in Enum.GetValues<ExpansionKind>().Reverse())
        {
            var zones = ZoneRegistry.ByExpansion(exp).ToArray();
            if (zones.Length == 0) continue;

            DrawExpansionTab(exp, zones, cfg, controller);
        }
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

        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        var queued = cfg.SelectedZones.Where(byId.ContainsKey).ToList();
        var positions = new Dictionary<uint, int>();
        for (var i = 0; i < queued.Count; i++) positions[queued[i]] = i + 1;

        // Cap the checklist height so a long expansion doesn't push the Run-until step and START off-screen.
        using var list = ImRaii.Child($"##zlist_{exp}", new Vector2(-1, Layout.ZoneListHeight * ImGuiHelpers.GlobalScale), false);
        foreach (var zone in zones)
        {
            ZoneStateReader.Refresh(zone);
            DrawRow(zone, cfg, controller, positions.GetValueOrDefault(zone.TerritoryId));
        }
    }

    private static void DrawExpansionToolbar(
        ExpansionKind exp, ZoneInfo[] zones, HashSet<uint> territoryIds, int selected,
        Configuration cfg, AutoFateController controller)
    {
        var allSelected = selected == zones.Length;

        var rightLabel = allSelected ? "Clear all" : "Select all";
        var rightWidth = ImGui.CalcTextSize(rightLabel).X + ImGui.GetStyle().FramePadding.X * 2;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - rightWidth);

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

    private static void DrawRow(ZoneInfo zone, Configuration cfg, AutoFateController controller, int queuePos)
    {
        var sel = cfg.SelectedZones.Contains(zone.TerritoryId);
        var disabled = controller.Running || !zone.Unlocked;

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
            ImGui.SetTooltip(controller.Running
                ? "Stop the runner to change zone selection."
                : "Locked — attune an aetheryte in this zone first.");

        ImGui.SameLine();
        var nameColor = zone.Unlocked ? Styling.TextStrong : Styling.TextMuted;
        using (ImRaii.PushColor(ImGuiCol.Text, nameColor))
            ImGui.TextUnformatted(zone.Name);

        if (queuePos > 0)
        {
            ImGui.SameLine(0, 6f);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentVioletSoft))
                ImGui.TextUnformatted($"#{queuePos}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Position {queuePos} in the grind order.");
        }

        DrawActiveFatePill(zone);

        ImGui.Unindent(6f);
    }

    private static void DrawActiveFatePill(ZoneInfo zone)
    {
        if (zone.ActiveFateCount <= 0) return;
        var pill = $"{zone.ActiveFateCount}";
        var w = ImGui.CalcTextSize(pill).X + 14f * ImGuiHelpers.GlobalScale;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - w);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
            ImGui.TextUnformatted(FontAwesomeIcon.Bolt.ToIconString());
        ImGui.SameLine(0, 4f);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
            ImGui.TextUnformatted(pill);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(zone.ActiveFateCount == 1
                ? "1 FATE active here right now."
                : $"{zone.ActiveFateCount} FATEs active here right now.");
    }

    private static void Divider()
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        dl.AddLine(p, p + new Vector2(w, 0), ImGui.GetColorU32(Styling.Hairline));
        ImGui.Dummy(new Vector2(w, 1f));
    }
}
