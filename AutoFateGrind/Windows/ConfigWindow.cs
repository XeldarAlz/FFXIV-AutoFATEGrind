using AutoFateGrind.Core.Game;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using clib.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoFateGrind.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private enum Tab { General, Filters, Classes, Gemstones, Repair, Humanize, GmAlert }

    private readonly Plugin plugin;
    private Tab activeTab = Tab.General;
    private int classPickerSelection;

    public ConfigWindow(Plugin plugin) : base("Auto FATE Grind — Settings###AutoFateGrindConfig")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(620, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 380),
            MaximumSize = new Vector2(900, 900),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        using var style = Styling.PushWindowStyle();

        var sidebarWidth = 168f * ImGuiHelpers.GlobalScale;

        using (ImRaii.Child("##cfg_sidebar", new Vector2(sidebarWidth, -1), border: false))
            DrawSidebar();

        ImGui.SameLine();

        using (ImRaii.Child("##cfg_content", new Vector2(-1, -1), border: false))
            DrawContent(cfg);
    }

    private void DrawSidebar()
    {
        ImGui.Spacing();
        if (SidebarTab.Draw("General",      FontAwesomeIcon.Cog,        Styling.AccentViolet,     activeTab == Tab.General)) activeTab = Tab.General;
        if (SidebarTab.Draw("FATE filters", FontAwesomeIcon.Filter,     Styling.AccentVioletSoft, activeTab == Tab.Filters)) activeTab = Tab.Filters;
        if (SidebarTab.Draw("Class queue",  FontAwesomeIcon.UserShield, Styling.AccentMint,       activeTab == Tab.Classes)) activeTab = Tab.Classes;
        if (SidebarTab.Draw("Gemstones",    FontAwesomeIcon.Gem,        Styling.AccentPink,       activeTab == Tab.Gemstones)) activeTab = Tab.Gemstones;
        if (SidebarTab.Draw("Repair",       FontAwesomeIcon.Wrench,     Styling.AccentRose,       activeTab == Tab.Repair))    activeTab = Tab.Repair;
        if (SidebarTab.Draw("Humanizer",    FontAwesomeIcon.Walking,    Styling.AccentMint,       activeTab == Tab.Humanize))  activeTab = Tab.Humanize;
        if (SidebarTab.Draw("GM alert",     FontAwesomeIcon.UserSecret, Styling.AccentAmber,      activeTab == Tab.GmAlert))   activeTab = Tab.GmAlert;
    }

    private void DrawContent(Configuration cfg)
    {
        ImGui.Spacing();
        switch (activeTab)
        {
            case Tab.General: DrawHeader("General", "Window and behavior preferences."); DrawGeneralTab(cfg); break;
            case Tab.Filters: DrawHeader("FATE filters", "Keeps the plugin off dying or late FATEs."); DrawFiltersTab(cfg); break;
            case Tab.Classes: DrawHeader("Class queue", "Switch gearsets on start, and advance to the next class when one hits its level cap."); DrawClassesTab(cfg); break;
            case Tab.Gemstones: DrawHeader("Gemstones", "Auto-spend Bicolor Gemstones once the wallet hits your threshold."); DrawGemstonesTab(cfg); break;
            case Tab.Repair:    DrawHeader("Repair",    "Auto-repair gear when equipped item condition drops below the threshold."); DrawRepairTab(cfg); break;
            case Tab.Humanize:  DrawHeader("Humanizer", "Take periodic city breaks between FATEs — teleport to a random hub and wander around for a few minutes before resuming."); DrawHumanizeTab(cfg); break;
            case Tab.GmAlert:   DrawHeader("GM alert",  "Detects nearby Game Masters and reacts — stop the bot, ping you, or take more drastic action.");  DrawGmAlertTab(cfg); break;
        }
    }

    private static void DrawHeader(string title, string subtitle)
    {
        ImGui.SetWindowFontScale(1.55f);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1.0f);

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextUnformatted(subtitle);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private static void DrawGeneralTab(Configuration cfg)
    {
        SettingsRow.Draw("Open this window on login",
            "Pop the main window automatically the next time you log in.",
            () => DrawToggle(cfg, () => cfg.AutoShowOnLogin, v => cfg.AutoShowOnLogin = v, "##gen_autoshow", Styling.AccentViolet));

        SettingsRow.Draw("Swap zones when current is empty",
            "When the current zone runs out of eligible FATEs, jump to the next zone in your priority order.",
            () => DrawToggle(cfg, () => cfg.SwapZonesWhenEmpty, v => cfg.SwapZonesWhenEmpty = v, "##gen_swap", Styling.AccentViolet));

        SettingsRow.Draw("Live FATE tracker popout",
            "Show the live FATE tracker as a small overlay window so you can keep it visible while the main window is closed.",
            () => DrawToggle(cfg, () => cfg.ShowLivePopout, v =>
            {
                cfg.ShowLivePopout = v;
                Plugin.Instance.LiveFateWindow.IsOpen = v;
            }, "##gen_popout", Styling.AccentViolet));
    }

    private static void DrawFiltersTab(Configuration cfg)
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

            var rowRightWidth = btnSize * 4 + spacingX * 3 + 8f * ImGuiHelpers.GlobalScale;
            var rightStart = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - rowRightWidth;
            ImGui.SameLine(rightStart);

            var dirIcon = entry.Descending ? FontAwesomeIcon.SortAmountDown : FontAwesomeIcon.SortAmountUp;
            if (DrawSortIconBtn(dirIcon, $"##sort_dir_{i}", btnSize))
            { entry.Descending = !entry.Descending; cfg.SaveDebounced(); }
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(i == 0))
                if (DrawSortIconBtn(FontAwesomeIcon.ArrowUp, $"##sort_up_{i}", btnSize)) moveUp = i;
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(i == cfg.FateSortOrder.Count - 1))
                if (DrawSortIconBtn(FontAwesomeIcon.ArrowDown, $"##sort_dn_{i}", btnSize)) moveDown = i;
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(cfg.FateSortOrder.Count <= 1))
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                if (DrawSortIconBtn(FontAwesomeIcon.Times, $"##sort_rm_{i}", btnSize)) remove = i;
        }

        if (moveUp is int mu && mu > 0)
        { (cfg.FateSortOrder[mu - 1], cfg.FateSortOrder[mu]) = (cfg.FateSortOrder[mu], cfg.FateSortOrder[mu - 1]); cfg.SaveDebounced(); }
        else if (moveDown is int md && md < cfg.FateSortOrder.Count - 1)
        { (cfg.FateSortOrder[md + 1], cfg.FateSortOrder[md]) = (cfg.FateSortOrder[md], cfg.FateSortOrder[md + 1]); cfg.SaveDebounced(); }
        else if (remove is int r)
        { cfg.FateSortOrder.RemoveAt(r); cfg.SaveDebounced(); }

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

    private static int sortAddSelection;

    private static bool DrawSortIconBtn(FontAwesomeIcon icon, string id, float size)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGui.Button(icon.ToIconString() + id, new Vector2(size, size));
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
            if (ToggleSwitch.Draw(id, ref skipped, Styling.AccentVioletSoft))
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
        }
    }

    private void DrawClassesTab(Configuration cfg)
    {
        SettingsRow.Draw("Switch class when run starts",
            "Equip the first eligible gearset below when you press Start. Disable to leave the run on whatever class you're currently on.",
            () => DrawToggle(cfg, () => cfg.ApplyClassOnStart, v => cfg.ApplyClassOnStart = v, "##cls_apply", Styling.AccentMint));

        if (!cfg.ApplyClassOnStart)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("Class switching is off. Enable the toggle above to configure the queue.");
            return;
        }

        SettingsRow.Draw("When all classes are done",
            "After every queued class has hit its level cap, either keep grinding on the last one or stop the run.",
            () =>
            {
                var keep = cfg.AfterClassQueueDone == AfterClassQueueDone.KeepGrindingOnLast;
                if (ImGui.RadioButton("Keep grinding on the last class", keep))
                { cfg.AfterClassQueueDone = AfterClassQueueDone.KeepGrindingOnLast; cfg.SaveDebounced(); }
                if (ImGui.RadioButton("Stop the run", !keep))
                { cfg.AfterClassQueueDone = AfterClassQueueDone.StopRun; cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Add a gearset",
            "Use the gear-set number shown in your in-game Gear Set list (1–100). Class is resolved automatically.",
            () => DrawAddClassRow(cfg));

        SettingsRow.Draw("Queue",
            "Order matters: top entry runs first, then advances when its level cap is hit.",
            () => DrawClassQueueList(cfg));
    }

    private void DrawAddClassRow(Configuration cfg)
    {
        var gearsets = ClassSwitcher.EnumerateGearsets();
        if (gearsets.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No gearsets found. Save one in-game (Character → Gear Set List) first.");
            return;
        }

        var alreadyQueued = cfg.ClassQueue.Select(e => e.GearsetIndex).ToHashSet();
        var labels = gearsets.Select(g =>
        {
            var job = ClassSwitcher.JobNameForJobId(g.JobId);
            var name = string.IsNullOrWhiteSpace(g.Name) ? "" : $" — {g.Name}";
            var taken = alreadyQueued.Contains(g.UserIndex) ? "  (queued)" : "";
            return $"{g.UserIndex,3}. {job}{name}{taken}";
        }).ToArray();

        classPickerSelection = Math.Clamp(classPickerSelection, 0, gearsets.Count - 1);

        ImGui.SetNextItemWidth(360);
        ImGui.Combo("##cls_picker", ref classPickerSelection, labels, labels.Length);

        var picked = gearsets[classPickerSelection];
        var duplicate = alreadyQueued.Contains(picked.UserIndex);

        ImGui.SameLine();
        using (ImRaii.Disabled(duplicate))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
            if (ImGui.SmallButton("Add##cls_add"))
            {
                var maxLevel = ClassSwitcher.GameMaxLevel;
                var atCap = ClassSwitcher.UnsyncedLevelForJobId(picked.JobId) >= maxLevel;
                cfg.ClassQueue.Add(new ClassQueueEntry
                {
                    GearsetIndex = picked.UserIndex,
                    JobId = picked.JobId,
                    StopAtLevel = atCap ? 0 : maxLevel,
                });
                cfg.SaveDebounced();
                var nextFree = gearsets.FindIndex(g => !alreadyQueued.Contains(g.UserIndex) && g.UserIndex != picked.UserIndex);
                if (nextFree >= 0) classPickerSelection = nextFree;
            }

        if (duplicate)
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("Already in the queue.");
    }

    private static void DrawClassQueueList(Configuration cfg)
    {
        if (cfg.ClassQueue.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No classes queued. Automation will use whatever class you're on.");
            return;
        }

        int? moveUp = null, moveDown = null, remove = null;
        var btnSize = ImGui.GetFrameHeight();
        var spacingX = 4f * ImGuiHelpers.GlobalScale;
        var rowRightWidth = btnSize * 3 + spacingX * 2 + 8f * ImGuiHelpers.GlobalScale;

        for (var i = 0; i < cfg.ClassQueue.Count; i++)
        {
            var entry = cfg.ClassQueue[i];
            DrawClassQueueRow(i, cfg.ClassQueue.Count, entry, cfg, btnSize, spacingX, rowRightWidth,
                onUp: () => moveUp = i,
                onDown: () => moveDown = i,
                onRemove: () => remove = i);
        }

        if (moveUp is int mu && mu > 0)
        {
            (cfg.ClassQueue[mu - 1], cfg.ClassQueue[mu]) = (cfg.ClassQueue[mu], cfg.ClassQueue[mu - 1]);
            cfg.SaveDebounced();
        }
        else if (moveDown is int md && md < cfg.ClassQueue.Count - 1)
        {
            (cfg.ClassQueue[md + 1], cfg.ClassQueue[md]) = (cfg.ClassQueue[md], cfg.ClassQueue[md + 1]);
            cfg.SaveDebounced();
        }
        else if (remove is int r)
        {
            cfg.ClassQueue.RemoveAt(r);
            cfg.SaveDebounced();
        }
    }

    private static void DrawClassQueueRow(
        int index, int total, ClassQueueEntry entry, Configuration cfg,
        float btnSize, float spacingX, float rowRightWidth,
        Action onUp, Action onDown, Action onRemove)
    {
        var running = Plugin.Instance.Controller.Running;
        using (ImRaii.Disabled(running))
        {
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted($"{index + 1}.");
            ImGui.SameLine();
            var jobName = ClassSwitcher.JobNameForUserIndex(entry.GearsetIndex);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                ImGui.TextUnformatted($"{jobName} · gearset {entry.GearsetIndex}");

            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            {
                var jobId = ClassSwitcher.JobIdForUserIndex(entry.GearsetIndex);
                var lvl = ClassSwitcher.UnsyncedLevelForJobId(jobId);
                ImGui.TextUnformatted($"  (lvl {lvl})");
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            var cap = entry.StopAtLevel;
            if (ImGui.SliderInt($"##cls_cap_{index}", ref cap, 0, ClassSwitcher.GameMaxLevel, cap == 0 ? "no cap" : "Stop at %d Level"))
            { entry.StopAtLevel = cap; cfg.SaveDebounced(); }

            var rightStart = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - rowRightWidth;
            ImGui.SameLine(rightStart);

            using (ImRaii.Disabled(index == 0))
                if (DrawClassIconBtn(FontAwesomeIcon.ArrowUp, $"##cls_up_{index}", btnSize)) onUp();
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(index == total - 1))
                if (DrawClassIconBtn(FontAwesomeIcon.ArrowDown, $"##cls_dn_{index}", btnSize)) onDown();
            ImGui.SameLine(0, spacingX);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                if (DrawClassIconBtn(FontAwesomeIcon.Times, $"##cls_rm_{index}", btnSize)) onRemove();
        }
    }

    private static bool DrawClassIconBtn(FontAwesomeIcon icon, string id, float size)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGui.Button(icon.ToIconString() + id, new Vector2(size, size));
    }

    private static void DrawGemstonesTab(Configuration cfg)
    {
        SettingsRow.Draw("Auto-trade when at threshold",
            "When your Bicolor Gemstone inventory reaches the threshold below, the plugin teleports to a trader and buys the item.",
            () => DrawToggle(cfg, () => cfg.TradeOnCap, v => cfg.TradeOnCap = v, "##tr_oncap", Styling.AccentPink));

        if (!cfg.TradeOnCap)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("Auto-trade is off. Enable the toggle above to configure the trade.");
            return;
        }

        SettingsRow.Draw("Trade threshold",
            "Gem count that triggers the trade. Game cap is 1500. Lower values trade more often so fewer FATEs are wasted near cap.",
            () =>
            {
                var v = cfg.TradeThreshold;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##tr_threshold", ref v, 100, 1500, "%d gems"))
                { cfg.TradeThreshold = Math.Clamp(v, 100, 1500); cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Item to buy", "Pulled live from game data. Cost shown in gems per one.", () =>
        {
            var catalog = GemstoneCatalog.All;
            if (catalog.Length == 0)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                    ImGui.TextUnformatted("No gem-shop items found.");
                return;
            }
            var effectiveId = GemstoneCatalog.EnsurePersistedTarget();
            var idx = Array.FindIndex(catalog, i => i.ItemId == effectiveId);
            if (idx < 0) idx = 0;
            var labels = catalog.Select(i => $"{i.ItemName}  ({i.CostPerOne}g)").ToArray();
            ImGui.SetNextItemWidth(420);
            if (ImGui.Combo("##tr_item", ref idx, labels, labels.Length))
            { cfg.TargetTradeItemId = catalog[idx].ItemId; cfg.SaveDebounced(); }
        });

        SettingsRow.Draw("How much to spend",
            "Choose the strategy used when the trade fires.", () =>
            {
                if (ImGui.RadioButton("Spend all gemstones (minus reserve)", cfg.SpendMode == GemstoneSpendMode.SpendAll))
                { cfg.SpendMode = GemstoneSpendMode.SpendAll; cfg.SaveDebounced(); }

                if (ImGui.RadioButton("Spend up to a set amount of gems", cfg.SpendMode == GemstoneSpendMode.SpendGems))
                { cfg.SpendMode = GemstoneSpendMode.SpendGems; cfg.SaveDebounced(); }

                if (cfg.SpendMode == GemstoneSpendMode.SpendGems)
                {
                    ImGui.Indent(20f);
                    var g = cfg.SpendGemsAmount;
                    ImGui.SetNextItemWidth(220);
                    if (ImGui.SliderInt("##tr_spend_gems", ref g, 50, 1500, "%d gems / trade"))
                    { cfg.SpendGemsAmount = Math.Clamp(g, 50, 1500); cfg.SaveDebounced(); }
                    ImGui.Unindent(20f);
                }

                if (ImGui.RadioButton("Buy a fixed number of the item", cfg.SpendMode == GemstoneSpendMode.BuyQuantity))
                { cfg.SpendMode = GemstoneSpendMode.BuyQuantity; cfg.SaveDebounced(); }

                if (cfg.SpendMode == GemstoneSpendMode.BuyQuantity)
                {
                    ImGui.Indent(20f);
                    var n = cfg.BuyQuantityAmount;
                    ImGui.SetNextItemWidth(220);
                    if (ImGui.SliderInt("##tr_buy_qty", ref n, 1, 99, "%d × item"))
                    { cfg.BuyQuantityAmount = Math.Clamp(n, 1, 99); cfg.SaveDebounced(); }
                    ImGui.Unindent(20f);
                }
            });

        SettingsRow.Draw("Keep in reserve",
            "Gems left untouched on every trade. Use this when you want to save toward a pricier item without turning auto-trade off.",
            () =>
            {
                var v = cfg.KeepGemstonesReserve;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##tr_reserve", ref v, 0, 1500, "%d gems"))
                { cfg.KeepGemstonesReserve = Math.Clamp(v, 0, 1500); cfg.SaveDebounced(); }
            });

        DrawSpendPreview(cfg);

        SettingsRow.Draw("After the trade",
            "What to do once the buy succeeds.", () =>
            {
                var resume = cfg.AfterTrade == AfterTradeAction.Resume;
                if (ImGui.RadioButton("Resume FATE grind in the same zone", resume))
                { cfg.AfterTrade = AfterTradeAction.Resume; cfg.SaveDebounced(); }
                if (ImGui.RadioButton("Stop the run", !resume))
                { cfg.AfterTrade = AfterTradeAction.Stop; cfg.SaveDebounced(); }
            });
    }

    private static void DrawSpendPreview(Configuration cfg)
    {
        var item = GemstoneCatalog.FindById(cfg.TargetTradeItemId);
        if (item is null) return;

        var qty = GemstoneCatalog.ComputeBuyQuantity(cfg.TradeThreshold, item.CostPerOne);

        var color = qty <= 0 ? Styling.AccentRose : Styling.TextMuted;
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextWrapped(qty <= 0
                ? $"Threshold/reserve won't afford any {item.ItemName} at {item.CostPerOne}g each."
                : $"At threshold {cfg.TradeThreshold}g (keeping {cfg.KeepGemstonesReserve}g), next trade buys ~{qty} × {item.ItemName} for {qty * item.CostPerOne}g.");
        ImGui.Spacing();
    }

    private static void DrawToggle(Configuration cfg, Func<bool> getter, Action<bool> setter, string id, Vector4 accent)
    {
        var v = getter();
        if (ToggleSwitch.Draw(id, ref v, accent))
        {
            setter(v);
            cfg.SaveDebounced();
        }
    }

    private static void DrawRepairTab(Configuration cfg)
    {
        SettingsRow.Draw("Auto-repair when gear is damaged",
            "Between FATEs, when the lowest equipped item drops to or below the threshold, the plugin runs a repair. At 0% the gear stops working — keep some margin.",
            () => DrawToggle(cfg, () => cfg.AutoRepair, v => cfg.AutoRepair = v, "##rp_on", Styling.AccentRose));

        if (!cfg.AutoRepair)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("Auto-repair is off. Enable the toggle above to configure repair.");
            return;
        }

        SettingsRow.Draw("Repair threshold",
            "Trips when the worst equipped slot reaches this condition percentage. 20% leaves comfortable margin before the 0% breakdown.",
            () =>
            {
                var v = cfg.AutoRepairThresholdPct;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##rp_threshold", ref v, 5, 80, "%d%%"))
                { cfg.AutoRepairThresholdPct = Math.Clamp(v, 5, 80); cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Repair source",
            "How the repair is performed. Self-repair uses Dark Matter from your bag (no travel). NPC repair travels to your Grand Company mender.",
            () =>
            {
                if (ImGui.RadioButton("Self first, then NPC if no Dark Matter", cfg.RepairMode == RepairMode.SelfThenNpc))
                { cfg.RepairMode = RepairMode.SelfThenNpc; cfg.SaveDebounced(); }

                if (ImGui.RadioButton("Self only (Dark Matter)", cfg.RepairMode == RepairMode.SelfOnly))
                { cfg.RepairMode = RepairMode.SelfOnly; cfg.SaveDebounced(); }

                if (ImGui.RadioButton("NPC only (Grand Company mender)", cfg.RepairMode == RepairMode.NpcOnly))
                { cfg.RepairMode = RepairMode.NpcOnly; cfg.SaveDebounced(); }
            });

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextWrapped("NPC repair requires Grand Company affiliation — the plugin teleports to your GC's mender NPC and pays in company seals.");
    }

    private static void DrawHumanizeTab(Configuration cfg)
    {
        SettingsRow.Draw("Take periodic city breaks",
            "Every N FATEs, teleport to a random selected city and wander around for a few minutes before resuming. Helps you avoid player reports by acting a little more human — useful when you leave the PC running for long sessions and don't want others noticing you grinding FATEs non-stop.",
            () => DrawToggle(cfg, () => cfg.HumanizerEnabled, v => cfg.HumanizerEnabled = v, "##hum_on", Styling.AccentMint));

        if (!cfg.HumanizerEnabled)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("Humanizer is off. Enable the toggle above to configure breaks.");
            return;
        }

        SettingsRow.Draw("FATEs between breaks",
            "Take a break after this many completed FATEs. The counter resets after each break.",
            () =>
            {
                var v = cfg.HumanizerFatesBeforeBreak;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##hum_fates", ref v, 1, 100, "%d FATEs"))
                { cfg.HumanizerFatesBeforeBreak = Math.Clamp(v, 1, 100); cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Break length",
            "Random duration between these two values is rolled for each break.",
            () =>
            {
                var lo = cfg.HumanizerBreakMinMinutes;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##hum_min", ref lo, 1, 60, "Min %d min"))
                {
                    cfg.HumanizerBreakMinMinutes = Math.Clamp(lo, 1, 60);
                    if (cfg.HumanizerBreakMaxMinutes < cfg.HumanizerBreakMinMinutes)
                        cfg.HumanizerBreakMaxMinutes = cfg.HumanizerBreakMinMinutes;
                    cfg.SaveDebounced();
                }

                var hi = cfg.HumanizerBreakMaxMinutes;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##hum_max", ref hi, 1, 60, "Max %d min"))
                {
                    cfg.HumanizerBreakMaxMinutes = Math.Clamp(hi, cfg.HumanizerBreakMinMinutes, 60);
                    cfg.SaveDebounced();
                }
            });

        SettingsRow.Draw("Pause between walks",
            "After arriving at each random point, stand still for a random duration in this range before walking somewhere else.",
            () =>
            {
                var lo = cfg.HumanizerPauseMinSec;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##hum_pause_min", ref lo, 0, 60, "Min %d sec"))
                {
                    cfg.HumanizerPauseMinSec = Math.Clamp(lo, 0, 60);
                    if (cfg.HumanizerPauseMaxSec < cfg.HumanizerPauseMinSec)
                        cfg.HumanizerPauseMaxSec = cfg.HumanizerPauseMinSec;
                    cfg.SaveDebounced();
                }

                var hi = cfg.HumanizerPauseMaxSec;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##hum_pause_max", ref hi, 0, 60, "Max %d sec"))
                {
                    cfg.HumanizerPauseMaxSec = Math.Clamp(hi, cfg.HumanizerPauseMinSec, 60);
                    cfg.SaveDebounced();
                }
            });

        SettingsRow.Draw("Walk distance",
            "Each random destination is rolled this many meters away from your current position. Larger ranges cover more of the city; smaller ranges keep you near the aetheryte.",
            () =>
            {
                var lo = cfg.HumanizerWanderMinMeters;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##hum_wander_min", ref lo, 5, 200, "Min %d m"))
                {
                    cfg.HumanizerWanderMinMeters = Math.Clamp(lo, 5, 200);
                    if (cfg.HumanizerWanderMaxMeters < cfg.HumanizerWanderMinMeters)
                        cfg.HumanizerWanderMaxMeters = cfg.HumanizerWanderMinMeters;
                    cfg.SaveDebounced();
                }

                var hi = cfg.HumanizerWanderMaxMeters;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##hum_wander_max", ref hi, 5, 200, "Max %d m"))
                {
                    cfg.HumanizerWanderMaxMeters = Math.Clamp(hi, cfg.HumanizerWanderMinMeters, 200);
                    cfg.SaveDebounced();
                }
            });

        SettingsRow.Draw("Cities",
            "Tick the cities the plugin is allowed to teleport to. One is picked at random each break. Untick cities you haven't unlocked or don't want visited.",
            () => DrawHumanizerCityList(cfg));
    }

    private static void DrawHumanizerCityList(Configuration cfg)
    {
        var grouped = CityCatalog.All.GroupBy(c => c.Expansion).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted(group.Key.ShortName());

            foreach (var city in group)
            {
                var selected = cfg.HumanizerCities.Contains(city.TerritoryId);
                var id = $"##hum_city_{city.TerritoryId}";
                if (ToggleSwitch.Draw(id, ref selected, Styling.AccentMint))
                {
                    if (selected) cfg.HumanizerCities.Add(city.TerritoryId);
                    else          cfg.HumanizerCities.Remove(city.TerritoryId);
                    cfg.SaveDebounced();
                }
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                    ImGui.TextUnformatted(city.Name);
            }
            ImGui.Spacing();
        }

        if (cfg.HumanizerCities.Count == 0)
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                ImGui.TextWrapped("No cities selected — Humanizer will skip the break and keep grinding.");
    }

    private static string gmCommandDraft = string.Empty;

    private static void DrawGmAlertTab(Configuration cfg)
    {
        SettingsRow.Draw("Stop the run",
            "Halt automation immediately when a GM appears in your zone. Strongly recommended — the rest of the alerts are useless if the bot keeps grinding.",
            () => DrawToggle(cfg, () => cfg.GmAlertStopRun, v => cfg.GmAlertStopRun = v, "##gm_stop", Styling.AccentRose));

        SettingsRow.Draw("Toast notification",
            "Pop a Dalamud toast: \"GM <name> is nearby!\"",
            () => DrawToggle(cfg, () => cfg.GmAlertToast, v => cfg.GmAlertToast = v, "##gm_toast", Styling.AccentAmber));

        SettingsRow.Draw("Chat alert",
            "Print a red chat warning into your local log.",
            () => DrawToggle(cfg, () => cfg.GmAlertChat, v => cfg.GmAlertChat = v, "##gm_chat", Styling.AccentAmber));

        SettingsRow.Draw("Sound beeps",
            "Plays a series of system beeps through your speakers. Loud enough to grab your attention if you're tabbed away.",
            () =>
            {
                DrawToggle(cfg, () => cfg.GmAlertSound, v => cfg.GmAlertSound = v, "##gm_sound", Styling.AccentAmber);

                if (!cfg.GmAlertSound) return;

                ImGui.Indent(20f);

                var count = cfg.GmAlertBeepCount;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##gm_beep_count", ref count, 1, 20, "%d beeps"))
                { cfg.GmAlertBeepCount = Math.Clamp(count, 1, 20); cfg.SaveDebounced(); }

                var dur = cfg.GmAlertBeepDurationMs;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##gm_beep_dur", ref dur, 50, 1000, "%d ms each"))
                { cfg.GmAlertBeepDurationMs = Math.Clamp(dur, 50, 1000); cfg.SaveDebounced(); }

                var freq = cfg.GmAlertBeepFrequencyHz;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##gm_beep_freq", ref freq, 100, 5000, "%d Hz"))
                { cfg.GmAlertBeepFrequencyHz = Math.Clamp(freq, 100, 5000); cfg.SaveDebounced(); }

                using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
                    if (ImGui.SmallButton("Preview##gm_beep_preview"))
                        Core.Game.GmAlertWatcher.PlayBeeps(cfg.GmAlertBeepCount, cfg.GmAlertBeepFrequencyHz, cfg.GmAlertBeepDurationMs);

                ImGui.Unindent(20f);
            });

        SettingsRow.Draw("Custom commands",
            "Chat commands to run when a GM is spotted. Useful for things like /logout, /sh stay calm, or a macro.",
            () => DrawGmCommandList(cfg));

        SettingsRow.Draw("Kill the game",
            "Hard-terminate the game process via /xlkill. The last-resort option — no goodbyes, no cutscene, no logout. You'll get a disconnect.",
            () => DrawToggle(cfg, () => cfg.GmAlertKillGame, v => cfg.GmAlertKillGame = v, "##gm_kill", Styling.AccentRose));
    }

    private static void DrawGmCommandList(Configuration cfg)
    {
        ImGui.SetNextItemWidth(360);
        var input = gmCommandDraft;
        if (ImGui.InputTextWithHint("##gm_cmd_in", "/logout", ref input, 200, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            var trimmed = input.Trim();
            if (trimmed.Length > 0)
            {
                var cmd = trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
                if (!cfg.GmAlertCommands.Contains(cmd))
                {
                    cfg.GmAlertCommands.Add(cmd);
                    cfg.SaveDebounced();
                }
            }
            gmCommandDraft = string.Empty;
        }
        else
        {
            gmCommandDraft = input;
        }

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
            if (ImGui.SmallButton("Add##gm_cmd_add"))
            {
                var trimmed = gmCommandDraft.Trim();
                if (trimmed.Length > 0)
                {
                    var cmd = trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
                    if (!cfg.GmAlertCommands.Contains(cmd))
                    {
                        cfg.GmAlertCommands.Add(cmd);
                        cfg.SaveDebounced();
                    }
                    gmCommandDraft = string.Empty;
                }
            }

        if (cfg.GmAlertCommands.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No commands queued.");
            return;
        }

        int? remove = null;
        var btnSize = ImGui.GetFrameHeight();
        for (var i = 0; i < cfg.GmAlertCommands.Count; i++)
        {
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted($"{i + 1}.");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                ImGui.TextUnformatted(cfg.GmAlertCommands[i]);

            var rightStart = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - btnSize - 8f * ImGuiHelpers.GlobalScale;
            ImGui.SameLine(rightStart);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
            using (ImRaii.PushFont(UiBuilder.IconFont))
                if (ImGui.Button(FontAwesomeIcon.Times.ToIconString() + $"##gm_cmd_rm_{i}", new Vector2(btnSize, btnSize)))
                    remove = i;
        }

        if (remove is int r)
        {
            cfg.GmAlertCommands.RemoveAt(r);
            cfg.SaveDebounced();
        }
    }
}
