using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game;
using AutoFateGrind.Windows.Components;
using clib.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class FilterSettings
{
    public static void Draw(Configuration cfg)
    {
        SettingsRow.Draw("Minimum time remaining",
            "Skip FATEs that have less than this many seconds left. Keeps you off corpse-FATEs other players are finishing.",
            () =>
            {
                var v = cfg.MinTimeRemainingSec;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##filt_mintime", ref v, 30, 600, "%d seconds"))
                { cfg.MinTimeRemainingSec = v; cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Maximum progress",
            "Skip FATEs already past this percent. Keeps you off near-finished FATEs others are clearing.",
            () =>
            {
                var v = cfg.MaxProgressPct;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##filt_maxprog", ref v, 50, 99, "%d %%"))
                { cfg.MaxProgressPct = v; cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Skip FATE types",
            "Toggle on a type to skip every FATE of that kind. Useful if you don't enjoy escorts or collect hand-ins.",
            () => DrawFateRuleSkipList(cfg));

        SettingsRow.Draw("FATE priority",
            "Order the rules used to pick the next FATE. Top rule wins; ties fall through to the next. Reset restores the recommended order.",
            () => DrawSortOrderList(cfg));
    }

    private static readonly (FateSortCriterion Criterion, string Label)[] sortCriterionLabels =
    [
        (FateSortCriterion.HasBonusWithTwist,   "Bonus FATE (skip while Twist active)"),
        (FateSortCriterion.Progress,            "Progress %"),
        (FateSortCriterion.HasBonus,            "Bonus FATE"),
        (FateSortCriterion.TimeRemainingUrgent, "About to expire"),
        (FateSortCriterion.Distance,            "Closest to me"),
        (FateSortCriterion.TimeRemaining,       "Time remaining"),
        (FateSortCriterion.Level,               "Level"),
        (FateSortCriterion.Name,                "Name"),
    ];

    private static string LabelFor(FateSortCriterion c)
    {
        foreach (var (crit, label) in sortCriterionLabels)
            if (crit == c) return label;
        return c.ToString();
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

            var rowRightWidth = btnSize * 4 + spacingX * 3 + Layout.RowRightMargin * ImGuiHelpers.GlobalScale;
            var rightStart = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - rowRightWidth;
            ImGui.SameLine(rightStart);

            var dirIcon = entry.Descending ? FontAwesomeIcon.SortAmountDown : FontAwesomeIcon.SortAmountUp;
            if (IconButton.Draw(dirIcon, $"##sort_dir_{i}", btnSize))
            { entry.Descending = !entry.Descending; cfg.SaveDebounced(); }
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(i == 0))
                if (IconButton.Draw(FontAwesomeIcon.ArrowUp, $"##sort_up_{i}", btnSize)) moveUp = i;
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(i == cfg.FateSortOrder.Count - 1))
                if (IconButton.Draw(FontAwesomeIcon.ArrowDown, $"##sort_dn_{i}", btnSize)) moveDown = i;
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(cfg.FateSortOrder.Count <= 1))
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                if (IconButton.Draw(FontAwesomeIcon.Times, $"##sort_rm_{i}", btnSize)) remove = i;
        }

        if (ListReorder.Apply(cfg.FateSortOrder, cfg.FateSortOrder.Count, moveUp, moveDown, remove))
            cfg.SaveDebounced();

        var missing = sortCriterionLabels.Where(l => cfg.FateSortOrder.All(e => e.Criterion != l.Criterion)).ToArray();
        if (missing.Length > 0)
        {
            ImGui.Spacing();
            var labels = missing.Select(m => m.Label).ToArray();
            sortAddSelection = Math.Clamp(sortAddSelection, 0, labels.Length - 1);
            ImGui.SetNextItemWidth(260);
            ImGui.Combo("##sort_add_pick", ref sortAddSelection, labels, labels.Length);
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
                if (ImGui.SmallButton("Add##sort_add"))
                { cfg.FateSortOrder.Add(new FateSortEntry { Criterion = missing[sortAddSelection].Criterion, Descending = true }); cfg.SaveDebounced(); }
        }

        ImGui.Spacing();
        if (ImGui.SmallButton("Reset to recommended##sort_reset"))
        {
            cfg.FateSortOrder = FateScanner.DefaultSortOrder.Select(e => new FateSortEntry { Criterion = e.Criterion, Descending = e.Descending }).ToList();
            cfg.SaveDebounced();
        }
    }

    private static readonly (PublicEvent.FateRule Rule, string Label, string Helper)[] fateRuleRows =
    [
        (PublicEvent.FateRule.Normal,          "Slay enemies",      "Kill the target mobs in the FATE ring."),
        (PublicEvent.FateRule.Collect,         "Collect / hand-in", "Gather items off mobs or nodes and turn them in."),
        (PublicEvent.FateRule.Escort,          "Escort",            "Protect an NPC that walks a fixed path."),
        (PublicEvent.FateRule.Defend,          "Defend",            "Hold a point or NPC against waves."),
        (PublicEvent.FateRule.EventFate,       "Talk to NPC",       "Dialogue-style FATE that starts by interacting with an NPC."),
        (PublicEvent.FateRule.Chase,           "Chase",             "Pursue a moving enemy across the zone."),
        (PublicEvent.FateRule.ConcertedWorks,  "Boss",              "Single-boss encounter (notorious monster style)."),
        (PublicEvent.FateRule.Fete,            "Fete",              "Special seasonal / community FATE."),
    ];

    private static void DrawFateRuleSkipList(Configuration cfg)
    {
        foreach (var (rule, label, helper) in fateRuleRows)
        {
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
                ImGui.TextUnformatted(label);
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("— " + helper);

            if (rule == PublicEvent.FateRule.Collect && !skipped
                && ExternalPlugins.IsInstalledButDisabled(ExternalPlugin.TextAdvance))
            {
                var pad = ImGui.GetFrameHeight() + 8f * ImGuiHelpers.GlobalScale;
                ImGui.Indent(pad);
                using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
                    ImGui.TextWrapped(
                        "TextAdvance is installed but disabled. Collect hand-ins usually work anyway "
                        + "(AFG drives the turn-in directly), but enabling TextAdvance's \"Enable plugin\" "
                        + "toggle is the safe fallback if a hand-in stalls.");
                ImGui.Unindent(pad);
            }
        }
    }
}
