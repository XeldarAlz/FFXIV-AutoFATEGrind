using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;
using clib.Utils;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Enums;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class FilterSettings
{
    private const int MinTimeMinSec = 30;
    private const int MinTimeMaxSec = 600;
    private const int MaxProgressMin = 50;
    private const int MaxProgressMax = 99;
    private const int MaxDurationMinSec = 60;
    private const int MaxDurationMaxSec = 1800;
    private const int MinLevel = 1;
    private const int MaxLevel = 100;
    private const int AutoBlacklistDeathsMin = 1;
    private const int AutoBlacklistDeathsMax = 10;
    private const string PercentFormat = "%d%%";

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

    private static readonly FateSortCriterion[] missingCriteria = new FateSortCriterion[sortCriterionLabels.Length];
    private static readonly string[] missingLabels = new string[sortCriterionLabels.Length];
    private static readonly SearchablePicker blacklistPicker = new(true);

    private static int blacklistAddSelection;
    private static int sortAddSelection;

    public static void Draw(Configuration cfg)
    {
        DrawEligibilityGroup(cfg);
        DrawSkipTypesGroup(cfg);
        DrawPriorityGroup(cfg);
        DrawBlacklistGroup(cfg);
    }

    private static void DrawEligibilityGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.FiltersEligibility)))
        {
            DrawEligibilityRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawEligibilityRows(Configuration cfg)
    {
        var minTime = cfg.MinTimeRemainingSec;
        if (SettingsControls.SliderRow(L.Settings.MinTime, L.Settings.MinTimeHelp, ref minTime, MinTimeMinSec, MinTimeMaxSec, Loc.T(L.Settings.MinTimeFormat)))
        {
            cfg.MinTimeRemainingSec = minTime;
            cfg.SaveDebounced();
        }

        var maxProgress = cfg.MaxProgressPct;
        if (SettingsControls.SliderRow(L.Settings.MaxProgress, L.Settings.MaxProgressHelp, ref maxProgress, MaxProgressMin, MaxProgressMax, PercentFormat))
        {
            cfg.MaxProgressPct = maxProgress;
            cfg.SaveDebounced();
        }

        DrawMaxDurationRows(cfg);
        DrawLevelWindowRows(cfg);
        DrawLevelRangeRows(cfg);
    }

    private static void DrawMaxDurationRows(Configuration cfg)
    {
        var enabled = cfg.MaxDurationFilterEnabled;
        if (SettingsControls.ToggleRow(L.Settings.MaxDuration, L.Settings.MaxDurationHelp, ref enabled))
        {
            cfg.MaxDurationFilterEnabled = enabled;
            cfg.SaveDebounced();
        }

        if (!cfg.MaxDurationFilterEnabled)
        {
            SettingsControls.Note(Loc.T(L.Settings.MaxDurationOff));
            return;
        }

        var limit = cfg.MaxFateDurationSec;
        if (SettingsControls.SliderRow(L.Settings.MaxDurationLimit, L.Settings.MaxDurationLimitHelp, ref limit, MaxDurationMinSec, MaxDurationMaxSec, Loc.T(L.Settings.MinTimeFormat)))
        {
            cfg.MaxFateDurationSec = limit;
            cfg.SaveDebounced();
        }
    }

    private static void DrawLevelWindowRows(Configuration cfg)
    {
        var enabled = cfg.LevelWindowEnabled;
        if (SettingsControls.ToggleRow(L.Settings.LevelWindow, L.Settings.LevelWindowHelp, ref enabled))
        {
            cfg.LevelWindowEnabled = enabled;
            cfg.SaveDebounced();
        }

        if (!cfg.LevelWindowEnabled)
        {
            SettingsControls.Note(Loc.T(L.Settings.LevelWindowOff));
            return;
        }

        var lowest = cfg.MinFateLevel;
        if (SettingsControls.SliderRow(L.Settings.LevelLowest, L.Settings.LevelLowestHelp, ref lowest, MinLevel, MaxLevel, Loc.T(L.Settings.LevelFormat)))
        {
            cfg.MinFateLevel = lowest;
            cfg.MaxFateLevel = Math.Max(cfg.MaxFateLevel, lowest);
            cfg.SaveDebounced();
        }

        var highest = cfg.MaxFateLevel;
        if (SettingsControls.SliderRow(L.Settings.LevelHighest, L.Settings.LevelHighestHelp, ref highest, MinLevel, MaxLevel, Loc.T(L.Settings.LevelFormat)))
        {
            cfg.MaxFateLevel = highest;
            cfg.MinFateLevel = Math.Min(cfg.MinFateLevel, highest);
            cfg.SaveDebounced();
        }
    }

    private static void DrawLevelRangeRows(Configuration cfg)
    {
        var enabled = cfg.LevelRangeFilterEnabled;
        if (SettingsControls.ToggleRow(L.Settings.LevelRange, L.Settings.LevelRangeHelp, ref enabled))
        {
            cfg.LevelRangeFilterEnabled = enabled;
            cfg.SaveDebounced();
        }

        if (!cfg.LevelRangeFilterEnabled)
        {
            SettingsControls.Note(Loc.T(L.Settings.LevelRangeOff));
            return;
        }

        var below = cfg.MaxLevelBelow;
        if (SettingsControls.SliderRow(L.Settings.LevelBelow, L.Settings.LevelBelowHelp, ref below, 0, MaxLevel, Loc.T(L.Settings.LevelFormat)))
        {
            cfg.MaxLevelBelow = below;
            cfg.SaveDebounced();
        }

        var above = cfg.MaxLevelAbove;
        if (SettingsControls.SliderRow(L.Settings.LevelAbove, L.Settings.LevelAboveHelp, ref above, 0, MaxLevel, Loc.T(L.Settings.LevelFormat)))
        {
            cfg.MaxLevelAbove = above;
            cfg.SaveDebounced();
        }
    }

    private static void DrawSkipTypesGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.FiltersTypes)))
        {
            SettingsControls.TitleRow(L.Settings.SkipTypes, L.Settings.SkipTypesHelp);
            DrawFateRuleSkipList(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawFateRuleSkipList(Configuration cfg)
    {
        for (var index = 0; index < fateRuleRows.Length; index++)
        {
            var (rule, label, helper) = fateRuleRows[index];
            var key = (int)rule;
            var skipped = cfg.SkippedFateRules.Contains(key);
            Gw.BeginRow(Loc.T(label), Loc.T(helper));
            if (Gw.Toggle(ref skipped, "##value"u8))
            {
                if (skipped)
                {
                    cfg.SkippedFateRules.Add(key);
                }
                else
                {
                    cfg.SkippedFateRules.Remove(key);
                }

                cfg.SaveDebounced();
            }

            Gw.EndRow();

            if (rule == PublicEvent.FateRule.Collect && !skipped && ExternalPlugins.IsInstalledButDisabled(ExternalPlugin.TextAdvance))
            {
                SettingsControls.Note(Loc.T(L.Settings.CollectTextAdvanceNote), Frame.Look.Warning);
            }
        }
    }

    private static void DrawPriorityGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.FiltersPriority)))
        {
            SettingsControls.TitleRow(L.Settings.FatePriority, L.Settings.FatePriorityHelp);
            DrawSortOrderList(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawSortOrderList(Configuration cfg)
    {
        if (cfg.FateSortOrder.Count == 0)
        {
            cfg.FateSortOrder = DefaultSortOrder();
        }

        int? moveUp = null, moveDown = null, remove = null;
        var count = cfg.FateSortOrder.Count;
        for (var index = 0; index < count; index++)
        {
            ImGui.PushID(index);
            switch (DrawSortOrderRow(cfg, cfg.FateSortOrder[index], index, count))
            {
                case SettingsControls.ListAction.MoveUp: moveUp = index; break;
                case SettingsControls.ListAction.MoveDown: moveDown = index; break;
                case SettingsControls.ListAction.Remove: remove = index; break;
            }

            ImGui.PopID();
        }

        if (ListReorder.Apply(cfg.FateSortOrder, count, moveUp, moveDown, remove))
        {
            cfg.SaveDebounced();
        }

        DrawSortAddRow(cfg);
        if (Gw.Button(Loc.T(L.Settings.ResetRecommended), ButtonStyle.Destructive, ButtonSize.Small))
        {
            cfg.FateSortOrder = DefaultSortOrder();
            cfg.SaveDebounced();
        }
    }

    private static SettingsControls.ListAction DrawSortOrderRow(Configuration cfg, FateSortEntry entry, int index, int count)
    {
        Gw.BeginRow($"{index + 1}. {LabelFor(entry.Criterion)}");
        var directionGlyph = entry.Descending ? SettingsControls.SortDescendingGlyph : SettingsControls.SortAscendingGlyph;
        var directionTooltip = entry.Descending ? Loc.T(L.Common.Descending) : Loc.T(L.Common.Ascending);
        if (Gw.IconButton(directionGlyph, ButtonStyle.Plain, ButtonSize.Small, directionTooltip))
        {
            entry.Descending = !entry.Descending;
            cfg.SaveDebounced();
        }

        ImGui.SameLine();
        var action = SettingsControls.ReorderButtons(index > 0, index < count - 1, count > 1);
        Gw.EndRow();
        return action;
    }

    private static void DrawSortAddRow(Configuration cfg)
    {
        var missing = CollectMissingCriteria(cfg);
        if (missing == 0)
        {
            return;
        }

        sortAddSelection = Math.Clamp(sortAddSelection, 0, missing - 1);
        SettingsControls.ComboBox(ref sortAddSelection, missingLabels.AsSpan(0, missing), SettingsControls.RowComboWidth);
        ImGui.SameLine();
        if (Gw.Button(Loc.T(L.Common.Add), ButtonStyle.Prominent, ButtonSize.Small))
        {
            cfg.FateSortOrder.Add(new FateSortEntry { Criterion = missingCriteria[sortAddSelection], Descending = true });
            cfg.SaveDebounced();
        }
    }

    private static int CollectMissingCriteria(Configuration cfg)
    {
        var count = 0;
        for (var index = 0; index < sortCriterionLabels.Length; index++)
        {
            var criterion = sortCriterionLabels[index].Criterion;
            if (IsOrdered(cfg, criterion))
            {
                continue;
            }

            missingCriteria[count] = criterion;
            missingLabels[count] = Loc.T(sortCriterionLabels[index].Label);
            count++;
        }

        return count;
    }

    private static bool IsOrdered(Configuration cfg, FateSortCriterion criterion)
    {
        for (var index = 0; index < cfg.FateSortOrder.Count; index++)
        {
            if (cfg.FateSortOrder[index].Criterion == criterion)
            {
                return true;
            }
        }

        return false;
    }

    private static string LabelFor(FateSortCriterion criterion)
    {
        for (var index = 0; index < sortCriterionLabels.Length; index++)
        {
            if (sortCriterionLabels[index].Criterion == criterion)
            {
                return Loc.T(sortCriterionLabels[index].Label);
            }
        }

        return criterion.ToString();
    }

    private static List<FateSortEntry> DefaultSortOrder()
        => FateScanner.DefaultSortOrder.Select(entry => new FateSortEntry { Criterion = entry.Criterion, Descending = entry.Descending }).ToList();

    private static void DrawBlacklistGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.FiltersBlacklist)))
        {
            DrawAutoBlacklistRows(cfg);
            DrawBlacklistPicker(cfg);
            SettingsControls.TitleRow(L.Settings.Blacklisted, L.Settings.BlacklistedHelp);
            DrawBlacklistList(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawAutoBlacklistRows(Configuration cfg)
    {
        var enabled = cfg.AutoBlacklistOnDeaths;
        if (SettingsControls.ToggleRow(L.Settings.AutoBlacklistDeaths, L.Settings.AutoBlacklistDeathsHelp, ref enabled))
        {
            cfg.AutoBlacklistOnDeaths = enabled;
            cfg.SaveDebounced();
        }

        if (!cfg.AutoBlacklistOnDeaths)
        {
            SettingsControls.Note(Loc.T(L.Settings.AutoBlacklistDeathsOff));
            return;
        }

        var deaths = cfg.AutoBlacklistDeathCount;
        if (SettingsControls.SliderRow(L.Settings.DeathsBeforeBlacklist, L.Settings.DeathsBeforeBlacklistHelp, ref deaths,
                AutoBlacklistDeathsMin, AutoBlacklistDeathsMax, Loc.T(L.Settings.DeathsFormat)))
        {
            cfg.AutoBlacklistDeathCount = deaths;
            cfg.SaveDebounced();
        }
    }

    private static void DrawBlacklistPicker(Configuration cfg)
    {
        var catalog = FateCatalog.All;
        if (catalog.Length == 0)
        {
            SettingsControls.TitleRow(L.Settings.AddByName, L.Settings.AddByNameHelp);
            SettingsControls.Note(Loc.T(L.Settings.NoFatesInData), Frame.Look.Destructive);
            return;
        }

        blacklistAddSelection = Math.Clamp(blacklistAddSelection, 0, catalog.Length - 1);
        blacklistPicker.Draw(L.Settings.AddByName, L.Settings.AddByNameHelp, FateCatalog.Labels, ref blacklistAddSelection);
        ImGui.SameLine();
        ImGui.BeginDisabled(!blacklistPicker.HasMatches);
        if (Gw.Button(Loc.T(L.Common.Add), ButtonStyle.Prominent, ButtonSize.Small))
        {
            FateBlacklist.Add(cfg, FateType.Normal, catalog[blacklistAddSelection].FateIds);
        }

        ImGui.EndDisabled();
    }

    private static void DrawBlacklistList(Configuration cfg)
    {
        var groups = FateBlacklist.All(cfg);
        if (groups.Count == 0)
        {
            SettingsControls.Note(Loc.T(L.Settings.NoBlacklisted));
            return;
        }

        BlacklistedFateGroup? removeGroup = null;
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            ImGui.PushID(index);
            Gw.BeginRow(group.Name);
            if (SettingsControls.RemoveButton(true, Loc.T(L.Settings.RemoveFromBlacklist)))
            {
                removeGroup = group;
            }

            Gw.EndRow();
            ImGui.PopID();
        }

        if (removeGroup is { } groupToRemove)
        {
            FateBlacklist.Remove(cfg, groupToRemove);
        }
    }
}
