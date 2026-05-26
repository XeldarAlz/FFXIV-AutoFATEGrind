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
        var autoSelected = ZoneSelection.IsAutoSelected(cfg);
        var scopedToShared = IsSharedScoped(cfg.Mode);

        // Eager refresh so tab badges cover collapsed expansions too.
        if (cfg.Mode == GrindMode.MaxFates)
            foreach (var z in ZoneRegistry.Zones)
                if (z.AchievementId != 0) ZoneStateReader.Refresh(z);

        using var tabBar = ImRaii.TabBar("##afg_main_tabs", ImGuiTabBarFlags.NoTooltip | ImGuiTabBarFlags.FittingPolicyScroll);
        if (!tabBar) return;

        DrawQueueTab(cfg, controller, autoSelected);

        foreach (var exp in Enum.GetValues<ExpansionKind>().Reverse())
        {
            if (scopedToShared && exp < ExpansionKind.ShB) continue;

            var zones = ZoneRegistry.ByExpansion(exp).ToArray();
            if (zones.Length == 0) continue;

            DrawExpansionTab(exp, zones, cfg, controller, autoSelected);
        }
    }

    private static void DrawQueueTab(Configuration cfg, AutoFateController controller, bool autoSelected)
    {
        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        var queueCount = autoSelected
            ? ZoneSelection.AutoQueue(cfg).Count
            : cfg.SelectedZones.Count(byId.ContainsKey);
        var label = queueCount > 0 ? $"Queue  {queueCount}###tab_queue" : "Queue###tab_queue";

        using var tab = ImRaii.TabItem(label);
        if (!tab) return;

        ImGui.Spacing();
        if (autoSelected) DrawAutoQueue(cfg, controller);
        else              SelectionOrder.Draw(cfg, controller);
    }

    private static void DrawAutoQueue(Configuration cfg, AutoFateController controller)
    {
        var candidates = ZoneSelection.SharedFateCandidates().ToList();
        if (candidates.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No Shared FATE zones discovered.");
            return;
        }

        foreach (var z in candidates) ZoneStateReader.Refresh(z);

        var eligible = ZoneSelection.EligibleSharedFatesOrdered(cfg).ToList();
        var locked = candidates.Where(z => !z.AchievementDone && !z.Unlocked)
            .OrderBy(z => z.Expansion).ThenBy(z => z.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var done = candidates.Where(z => z.AchievementDone)
            .OrderBy(z => z.Expansion).ThenBy(z => z.Name, StringComparer.OrdinalIgnoreCase).ToList();

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted("Auto-queued: every ShB / EW / DT zone whose Shared FATE achievement isn't complete. Use the arrows to set the rotation order; maxed zones drop out automatically.");
        ImGui.Spacing();

        int? moveUp = null, moveDown = null;
        var btnSize = ImGui.GetFrameHeight();
        var spacingX = 4f * ImGuiHelpers.GlobalScale;
        var arrowsBlockWidth = btnSize * 2 + spacingX;

        for (var i = 0; i < eligible.Count; i++)
        {
            var idx = i;
            DrawEligibleRow(eligible[i], i + 1, controller.Running,
                isFirst: i == 0, isLast: i == eligible.Count - 1,
                btnSize, spacingX, arrowsBlockWidth,
                onUp: () => moveUp = idx, onDown: () => moveDown = idx);
        }
        foreach (var z in locked) DrawAutoQueueStaticRow(z, "lock", Styling.TextMuted, arrowsBlockWidth);
        foreach (var z in done)   DrawAutoQueueStaticRow(z, "done", Styling.AccentMint, arrowsBlockWidth);

        if (moveUp is int mu && mu > 0)
        {
            (eligible[mu - 1], eligible[mu]) = (eligible[mu], eligible[mu - 1]);
            cfg.SharedFateOrder = eligible.Select(z => z.TerritoryId).ToList();
            cfg.SaveDebounced();
        }
        else if (moveDown is int md && md < eligible.Count - 1)
        {
            (eligible[md + 1], eligible[md]) = (eligible[md], eligible[md + 1]);
            cfg.SharedFateOrder = eligible.Select(z => z.TerritoryId).ToList();
            cfg.SaveDebounced();
        }
    }

    private static void DrawEligibleRow(
        ZoneInfo zone, int queueNumber, bool running, bool isFirst, bool isLast,
        float btnSize, float spacingX, float arrowsBlockWidth,
        Action onUp, Action onDown)
    {
        ImGui.AlignTextToFramePadding();

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted($"{queueNumber,2}.");
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            ImGui.TextUnformatted(zone.Name);

        var progress = zone.AchievementMax > 0
            ? $"{zone.AchievementCurrent}/{zone.AchievementMax}"
            : "—";
        var progressW = ImGui.CalcTextSize(progress).X + 8f * ImGuiHelpers.GlobalScale;
        var gap = 8f * ImGuiHelpers.GlobalScale;
        var rightStart = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX()
                         - arrowsBlockWidth - gap - progressW;
        ImGui.SameLine(rightStart);

        using (ImRaii.Disabled(running))
        {
            using (ImRaii.Disabled(isFirst))
                if (DrawAutoQueueIconBtn(FontAwesomeIcon.ArrowUp, $"##aq_up_{zone.TerritoryId}", btnSize))
                    onUp();
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(isLast))
                if (DrawAutoQueueIconBtn(FontAwesomeIcon.ArrowDown, $"##aq_dn_{zone.TerritoryId}", btnSize))
                    onDown();
        }

        ImGui.SameLine(0, gap);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(progress);
    }

    private static void DrawAutoQueueStaticRow(ZoneInfo zone, string prefix, Vector4 prefixColor, float arrowsBlockWidth)
    {
        ImGui.AlignTextToFramePadding();

        using (ImRaii.PushColor(ImGuiCol.Text, prefixColor))
            ImGui.TextUnformatted(prefix);
        ImGui.SameLine();

        var done = zone.AchievementDone;
        var nameColor = done
            ? Styling.AccentMint
            : Styling.TextMuted;
        using (ImRaii.PushColor(ImGuiCol.Text, nameColor))
            ImGui.TextUnformatted(zone.Name);

        var progress = zone.AchievementMax > 0
            ? $"{zone.AchievementCurrent}/{zone.AchievementMax}"
            : "—";
        var progressW = ImGui.CalcTextSize(progress).X + 8f * ImGuiHelpers.GlobalScale;
        var gap = 8f * ImGuiHelpers.GlobalScale;
        // Reserve the arrows slot so progress numbers line up with the eligible rows above.
        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX()
                       - arrowsBlockWidth - gap - progressW);
        ImGui.Dummy(new Vector2(arrowsBlockWidth, 0));
        ImGui.SameLine(0, gap);
        using (ImRaii.PushColor(ImGuiCol.Text, done ? Styling.AccentMint : Styling.TextDim))
            ImGui.TextUnformatted(progress);
    }

    private static bool DrawAutoQueueIconBtn(FontAwesomeIcon icon, string id, float size)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGui.Button(icon.ToIconString() + id, new Vector2(size, size));
    }

    private static void DrawExpansionTab(ExpansionKind exp, ZoneInfo[] zones, Configuration cfg, AutoFateController controller, bool autoSelected)
    {
        var territoryIds = zones.Select(z => z.TerritoryId).ToHashSet();
        int selected, total;
        if (autoSelected)
        {
            selected = zones.Count(z => !z.AchievementDone && z.AchievementId != 0);
            total = zones.Count(z => z.AchievementId != 0);
        }
        else
        {
            selected = cfg.SelectedZones.Count(territoryIds.Contains);
            total = zones.Length;
        }

        var label = $"{exp.ShortName()}  {selected}/{total}###tab_{exp}";

        using var tab = ImRaii.TabItem(label);
        if (!tab) return;

        ImGui.Spacing();
        if (!autoSelected)
        {
            DrawExpansionToolbar(exp, zones, territoryIds, selected, cfg, controller);
            ImGui.Spacing();
        }

        var hideCompleted = cfg.Mode == GrindMode.MaxFates && (autoSelected || !cfg.ShowCompletedZones);
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
            if (autoSelected) DrawAutoRow(zone);
            else              DrawRow(zone, cfg, controller);
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

        DrawActiveFatePill(zone);

        ImGui.Unindent(6f);
    }

    private static void DrawAutoRow(ZoneInfo zone)
    {
        ImGui.Indent(6f);
        ImGui.AlignTextToFramePadding();

        var locked = !zone.Unlocked;
        var badgeColor = locked ? Styling.TextMuted : Styling.AccentVioletSoft;
        using (ImRaii.PushColor(ImGuiCol.Text, badgeColor))
            ImGui.TextUnformatted(locked ? "lock" : "queued");

        if (locked && ImGui.IsItemHovered())
            ImGui.SetTooltip("Locked — attune an aetheryte in this zone first.");

        ImGui.SameLine();
        var nameColor = locked ? Styling.TextMuted : Styling.TextStrong;
        using (ImRaii.PushColor(ImGuiCol.Text, nameColor))
            ImGui.TextUnformatted(zone.Name);

        var progress = zone.AchievementMax > 0
            ? $"{zone.AchievementCurrent}/{zone.AchievementMax}"
            : "—";
        var pillW = ImGui.CalcTextSize(progress).X + 8f * ImGuiHelpers.GlobalScale;
        var fateW = zone.ActiveFateCount > 0
            ? ImGui.CalcTextSize($"{zone.ActiveFateCount}").X + 22f * ImGuiHelpers.GlobalScale
            : 0f;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - pillW - fateW);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(progress);

        if (zone.ActiveFateCount > 0)
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
    }

    private static bool IsSharedScoped(GrindMode mode)
        => mode is GrindMode.MaxFates;

    private static string DisabledReason(bool running, bool unlocked, bool achievementBlocks)
    {
        if (running) return "Stop the runner to change zone selection.";
        if (!unlocked) return "Locked — attune an aetheryte in this zone first.";
        if (achievementBlocks) return "Free Market Friend already complete here. Switch goal to grind this zone.";
        return "";
    }
}
