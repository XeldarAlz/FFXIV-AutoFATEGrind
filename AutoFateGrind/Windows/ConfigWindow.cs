using AutoFateGrind.Core.Game;
using AutoFateGrind.Core.Trading;
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
    private enum Tab { General, Filters, Classes, Gemstones }

    private readonly Plugin plugin;
    private Tab activeTab = Tab.General;
    private int classPickerSelection;

    public ConfigWindow(Plugin plugin) : base("Auto Fate Grind — Settings###AutoFateGrindConfig")
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

        using (ImRaii.Disabled(!cfg.ApplyClassOnStart))
        {
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
                cfg.ClassQueue.Add(new ClassQueueEntry
                {
                    GearsetIndex = picked.UserIndex,
                    JobId = picked.JobId,
                    StopAtLevel = ClassSwitcher.GameMaxLevel,
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
            if (ImGui.SliderInt($"##cls_cap_{index}", ref cap, 0, ClassSwitcher.GameMaxLevel, cap == 0 ? "no cap" : "stop @ %d"))
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
}
