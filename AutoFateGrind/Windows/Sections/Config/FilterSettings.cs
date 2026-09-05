using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;
using clib.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Enums;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class FilterSettings
{
    public static void Draw(Configuration cfg)
    {
        DrawEligibilityGroup(cfg);
        DrawSkipTypesGroup(cfg);
        DrawPriorityGroup(cfg);
        DrawBlacklistGroup(cfg);
    }

    private static void DrawEligibilityGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.FiltersEligibility));

        SettingsRow.Draw(Loc.T(L.Settings.MinTime),
            Loc.T(L.Settings.MinTimeHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##filt_mintime",
                () => cfg.MinTimeRemainingSec, v => cfg.MinTimeRemainingSec = v, 30, 600, Loc.T(L.Settings.MinTimeFormat)));

        SettingsRow.Draw(Loc.T(L.Settings.MaxProgress),
            Loc.T(L.Settings.MaxProgressHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##filt_maxprog",
                () => cfg.MaxProgressPct, v => cfg.MaxProgressPct = v, 50, 99, "%d%%"));
    }

    private static void DrawSkipTypesGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.FiltersTypes));

        SettingsRow.DrawBlock(Loc.T(L.Settings.SkipTypes),
            Loc.T(L.Settings.SkipTypesHelp),
            () => DrawFateRuleSkipList(cfg));
    }

    private static void DrawPriorityGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.FiltersPriority));

        SettingsRow.DrawBlock(Loc.T(L.Settings.FatePriority),
            Loc.T(L.Settings.FatePriorityHelp),
            () => DrawSortOrderList(cfg));
    }

    private static void DrawBlacklistGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.FiltersBlacklist));

        SettingsRow.DrawBlock(Loc.T(L.Settings.AddByName),
            Loc.T(L.Settings.AddByNameHelp),
            () => DrawBlacklistPicker(cfg));

        SettingsRow.DrawBlock(Loc.T(L.Settings.Blacklisted),
            Loc.T(L.Settings.BlacklistedHelp),
            () => DrawBlacklistList(cfg));
    }

    private static int blacklistAddSelection;

    private static void DrawBlacklistPicker(Configuration cfg)
    {
        var catalog = FateCatalog.All;
        if (catalog.Length == 0)
        {
            SettingsRow.Note(Loc.T(L.Settings.NoFatesInData), Styling.AccentRose);
            return;
        }

        blacklistAddSelection = Math.Clamp(blacklistAddSelection, 0, catalog.Length - 1);
        var labels = FateCatalog.Labels;
        SettingsControls.DrawSearchableCombo("##bl_pick", labels[blacklistAddSelection], labels,
            ref blacklistAddSelection, 380f);

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
            if (ImGui.SmallButton($"{Loc.T(L.Common.Add)}##bl_add"))
                FateBlacklist.Add(cfg, FateType.Normal, catalog[blacklistAddSelection].FateIds);
    }

    private static void DrawBlacklistList(Configuration cfg)
    {
        var groups = FateBlacklist.All(cfg);
        if (groups.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted(Loc.T(L.Settings.NoBlacklisted));
            return;
        }

        BlacklistedFateGroup? removeGroup = null;
        var buttonSize = ImGui.GetFrameHeight();

        for (var index = 0; index < groups.Count; index++)
        {
            var entryGroup = groups[index];
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                ImGui.TextUnformatted(entryGroup.Name);

            ImGui.SameLine(SettingsGroup.InnerRightLocalX() - buttonSize);
            if (IconButton.Draw(FontAwesomeIcon.Times, $"##bl_rm_{entryGroup.Name}", buttonSize, Styling.AccentRose, Loc.T(L.Settings.RemoveFromBlacklist)))
                removeGroup = entryGroup;
        }

        if (removeGroup is { } groupToRemove)
            FateBlacklist.Remove(cfg, groupToRemove);
    }

    private static readonly (FateSortCriterion Criterion, LocString Label)[] sortCriterionLabels =
    [
        (FateSortCriterion.HasBonusWithTwist,   L.Settings.SortBonusTwist),
        (FateSortCriterion.Progress,            L.Settings.SortProgress),
        (FateSortCriterion.HasBonus,            L.Settings.SortBonus),
        (FateSortCriterion.TimeRemainingUrgent, L.Settings.SortUrgent),
        (FateSortCriterion.Distance,            L.Settings.SortDistance),
        (FateSortCriterion.TimeRemaining,       L.Settings.SortTimeRemaining),
        (FateSortCriterion.Level,               L.Settings.SortLevel),
        (FateSortCriterion.Name,                L.Settings.SortName),
    ];

    private static string LabelFor(FateSortCriterion criterion)
    {
        for (var index = 0; index < sortCriterionLabels.Length; index++)
        {
            if (sortCriterionLabels[index].Criterion == criterion) return Loc.T(sortCriterionLabels[index].Label);
        }

        return criterion.ToString();
    }

    private static int sortAddSelection;

    private static void DrawSortOrderList(Configuration cfg)
    {
        if (cfg.FateSortOrder.Count == 0)
            cfg.FateSortOrder = FateScanner.DefaultSortOrder.Select(e => new FateSortEntry { Criterion = e.Criterion, Descending = e.Descending }).ToList();

        int? moveUp = null, moveDown = null, remove = null;
        var btnSize = ImGui.GetFrameHeight();
        var spacingX = 4f * ImGuiHelpers.GlobalScale;

        for (var i = 0; i < cfg.FateSortOrder.Count; i++)
        {
            var entry = cfg.FateSortOrder[i];
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted($"{i + 1}.");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                ImGui.TextUnformatted(LabelFor(entry.Criterion));

            var rowRightWidth = btnSize * 4 + spacingX * 3;
            ImGui.SameLine(SettingsGroup.InnerRightLocalX() - rowRightWidth);

            var dirIcon = entry.Descending ? FontAwesomeIcon.SortAmountDown : FontAwesomeIcon.SortAmountUp;
            if (IconButton.Draw(dirIcon, $"##sort_dir_{i}", btnSize, tooltip: entry.Descending ? Loc.T(L.Common.Descending) : Loc.T(L.Common.Ascending)))
            { entry.Descending = !entry.Descending; cfg.SaveDebounced(); }
            ImGui.SameLine(0, spacingX);
            if (IconButton.Draw(FontAwesomeIcon.ArrowUp, $"##sort_up_{i}", btnSize, tooltip: Loc.T(L.Common.MoveUp), enabled: i > 0)) moveUp = i;
            ImGui.SameLine(0, spacingX);
            if (IconButton.Draw(FontAwesomeIcon.ArrowDown, $"##sort_dn_{i}", btnSize, tooltip: Loc.T(L.Common.MoveDown), enabled: i < cfg.FateSortOrder.Count - 1)) moveDown = i;
            ImGui.SameLine(0, spacingX);
            if (IconButton.Draw(FontAwesomeIcon.Times, $"##sort_rm_{i}", btnSize, Styling.AccentRose, Loc.T(L.Common.Remove), enabled: cfg.FateSortOrder.Count > 1)) remove = i;
        }

        if (ListReorder.Apply(cfg.FateSortOrder, cfg.FateSortOrder.Count, moveUp, moveDown, remove))
            cfg.SaveDebounced();

        var missing = sortCriterionLabels.Where(l => cfg.FateSortOrder.All(e => e.Criterion != l.Criterion)).ToArray();
        if (missing.Length > 0)
        {
            ImGui.Spacing();
            var labels = missing.Select(m => Loc.T(m.Label)).ToArray();
            sortAddSelection = Math.Clamp(sortAddSelection, 0, labels.Length - 1);
            SettingsControls.DrawPlainCombo("##sort_add_pick", ref sortAddSelection, labels, 260f);
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
                if (ImGui.SmallButton($"{Loc.T(L.Common.Add)}##sort_add"))
                { cfg.FateSortOrder.Add(new FateSortEntry { Criterion = missing[sortAddSelection].Criterion, Descending = true }); cfg.SaveDebounced(); }
        }

        ImGui.Spacing();
        if (ImGui.SmallButton($"{Loc.T(L.Settings.ResetRecommended)}##sort_reset"))
        {
            cfg.FateSortOrder = FateScanner.DefaultSortOrder.Select(e => new FateSortEntry { Criterion = e.Criterion, Descending = e.Descending }).ToList();
            cfg.SaveDebounced();
        }
    }

    private static readonly (PublicEvent.FateRule Rule, LocString Label, LocString Helper)[] fateRuleRows =
    [
        (PublicEvent.FateRule.Normal,          L.Settings.RuleNormal,  L.Settings.RuleNormalHelp),
        (PublicEvent.FateRule.Collect,         L.Settings.RuleCollect, L.Settings.RuleCollectHelp),
        (PublicEvent.FateRule.Escort,          L.Settings.RuleEscort,  L.Settings.RuleEscortHelp),
        (PublicEvent.FateRule.Defend,          L.Settings.RuleDefend,  L.Settings.RuleDefendHelp),
        (PublicEvent.FateRule.EventFate,       L.Settings.RuleEvent,   L.Settings.RuleEventHelp),
        (PublicEvent.FateRule.Chase,           L.Settings.RuleChase,   L.Settings.RuleChaseHelp),
        (PublicEvent.FateRule.ConcertedWorks,  L.Settings.RuleBoss,    L.Settings.RuleBossHelp),
        (PublicEvent.FateRule.Fete,            L.Settings.RuleFete,    L.Settings.RuleFeteHelp),
    ];

    private static void DrawFateRuleSkipList(Configuration cfg)
    {
        for (var index = 0; index < fateRuleRows.Length; index++)
        {
            var (rule, label, helper) = fateRuleRows[index];
            var key = (int)rule;
            var skipped = cfg.SkippedFateRules.Contains(key);
            var id = $"##filt_rule_{key}";
            if (ToggleSwitch.Draw(id, ref skipped))
            {
                if (skipped) cfg.SkippedFateRules.Add(key);
                else         cfg.SkippedFateRules.Remove(key);
                cfg.SaveDebounced();
            }
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                ImGui.TextUnformatted(Loc.T(label));
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted(Loc.T(helper));

            if (rule == PublicEvent.FateRule.Collect && !skipped
                && ExternalPlugins.IsInstalledButDisabled(ExternalPlugin.TextAdvance))
            {
                var pad = ImGui.GetFrameHeight() + 8f * ImGuiHelpers.GlobalScale;
                ImGui.Indent(pad);
                using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
                    ImGui.TextWrapped(Loc.T(L.Settings.CollectTextAdvanceNote));
                ImGui.Unindent(pad);
            }
        }
    }
}
