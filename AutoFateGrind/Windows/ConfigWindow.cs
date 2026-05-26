using AutoFateGrind.Core.Trading;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoFateGrind.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private enum Tab { General, Filters, Combat, Trader }

    private readonly Plugin plugin;
    private Tab activeTab = Tab.General;

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
        if (SidebarTab.Draw("Combat",       FontAwesomeIcon.Crosshairs, Styling.AccentRose,       activeTab == Tab.Combat))  activeTab = Tab.Combat;
        if (SidebarTab.Draw("Trader",       FontAwesomeIcon.Gem,        Styling.AccentPink,       activeTab == Tab.Trader))  activeTab = Tab.Trader;
    }

    private void DrawContent(Configuration cfg)
    {
        ImGui.Spacing();
        switch (activeTab)
        {
            case Tab.General: DrawHeader("General", "Window and behavior preferences."); DrawGeneralTab(cfg); break;
            case Tab.Filters: DrawHeader("FATE filters", "Keeps the plugin off dying or late FATEs."); DrawFiltersTab(cfg); break;
            case Tab.Combat:  DrawHeader("Combat", "Which auto-rotation preset to drive while engaged."); DrawCombatTab(cfg); break;
            case Tab.Trader:  DrawHeader("Gemstone trader", "Auto-buy a chosen item when the wallet hits your threshold."); DrawTraderTab(cfg); break;
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
    }

    private static void DrawCombatTab(Configuration cfg)
    {
        SettingsRow.Draw("BossMod preset name",
            "Name of the BossMod (or BossMod Reborn) preset to activate on engage. Define your custom preset in BossMod and put its exact name here.",
            () =>
            {
                var v = cfg.CombatPresetName;
                ImGui.SetNextItemWidth(320);
                if (ImGui.InputText("##cmb_preset", ref v, 64))
                { cfg.CombatPresetName = v; cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Install bundled preset",
            "Installs a bundled default preset into BossMod under the name above. Overwrites any existing preset with that name.",
            () =>
            {
                using (ImRaii.Disabled(true))
                    ImGui.Button("Install (TODO v0.3)");
            });
    }

    private static void DrawTraderTab(Configuration cfg)
    {
        SettingsRow.Draw("Auto-trade when at threshold",
            "When your Bicolor Gemstone inventory reaches the threshold below, the plugin teleports to a trader and buys the item.",
            () => DrawToggle(cfg, () => cfg.TradeOnCap, v => cfg.TradeOnCap = v, "##tr_oncap", Styling.AccentPink));

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
            var idx = Array.FindIndex(catalog, i => i.ItemId == cfg.TargetTradeItemId);
            if (idx < 0) idx = 0;
            var labels = catalog.Select(i => $"{i.ItemName}  ({i.CostPerOne}g)").ToArray();
            ImGui.SetNextItemWidth(420);
            if (ImGui.Combo("##tr_item", ref idx, labels, labels.Length))
            { cfg.TargetTradeItemId = catalog[idx].ItemId; cfg.SaveDebounced(); }
        });

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
