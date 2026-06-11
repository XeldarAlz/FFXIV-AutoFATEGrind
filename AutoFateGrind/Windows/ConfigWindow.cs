using AutoFateGrind.Windows.Components;
using AutoFateGrind.Windows.Sections.Config;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoFateGrind.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private enum Tab { General, Filters, Classes, Gemstones, Repair, Consumables, Humanize, PartyInvites, GmAlert }

    private readonly Plugin plugin;
    private Tab activeTab = Tab.General;

    public ConfigWindow(Plugin plugin) : base("Auto FATE Grind - Settings###AutoFateGrindConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(620, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(100, 100),
            MaximumSize = new Vector2(2000, 1600),
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
        if (SidebarTab.Draw("General",      FontAwesomeIcon.Cog,        Styling.AccentViolet, activeTab == Tab.General)) activeTab = Tab.General;
        if (SidebarTab.Draw("FATE filters", FontAwesomeIcon.Filter,     Styling.AccentViolet, activeTab == Tab.Filters)) activeTab = Tab.Filters;
        if (SidebarTab.Draw("Class queue",  FontAwesomeIcon.UserShield, Styling.AccentViolet, activeTab == Tab.Classes)) activeTab = Tab.Classes;
        if (SidebarTab.Draw("Gemstones",    FontAwesomeIcon.Gem,        Styling.AccentViolet, activeTab == Tab.Gemstones)) activeTab = Tab.Gemstones;
        if (SidebarTab.Draw("Repair",       FontAwesomeIcon.Wrench,     Styling.AccentViolet, activeTab == Tab.Repair))    activeTab = Tab.Repair;
        if (SidebarTab.Draw("Consumables",  FontAwesomeIcon.Utensils,   Styling.AccentViolet, activeTab == Tab.Consumables)) activeTab = Tab.Consumables;
        if (SidebarTab.Draw("Humanizer",    FontAwesomeIcon.Walking,    Styling.AccentViolet, activeTab == Tab.Humanize))  activeTab = Tab.Humanize;
        if (SidebarTab.Draw("Party invites", FontAwesomeIcon.UserSlash, Styling.AccentViolet, activeTab == Tab.PartyInvites)) activeTab = Tab.PartyInvites;
        if (SidebarTab.Draw("GM alert",     FontAwesomeIcon.UserSecret, Styling.AccentViolet, activeTab == Tab.GmAlert))   activeTab = Tab.GmAlert;
    }

    private void DrawContent(Configuration cfg)
    {
        ImGui.Spacing();
        switch (activeTab)
        {
            case Tab.General: DrawHeader("General", "Window and behavior preferences."); GeneralSettings.Draw(cfg); break;
            case Tab.Filters: DrawHeader("FATE filters", "Keeps the plugin off dying or late FATEs."); FilterSettings.Draw(cfg); break;
            case Tab.Classes: DrawHeader("Class queue", "Switch gearsets on start, and advance to the next class when one hits its level cap."); ClassSettings.Draw(cfg); break;
            case Tab.Gemstones: DrawHeader("Gemstones", "Auto-spend Bicolor Gemstones once the wallet hits your threshold."); GemstoneSettings.Draw(cfg); break;
            case Tab.Repair:    DrawHeader("Repair",    "Auto-repair gear when equipped item condition drops below the threshold."); RepairSettings.Draw(cfg); break;
            case Tab.Consumables: DrawHeader("Consumables", "Keep food and medicine buffs up while grinding — Well Fed is a free +3% EXP."); ConsumableSettings.Draw(cfg); break;
            case Tab.Humanize:  DrawHeader("Humanizer", "Take periodic city breaks between FATEs — teleport to a random hub and wander around for a few minutes before resuming."); HumanizerSettings.Draw(cfg); break;
            case Tab.PartyInvites: DrawHeader("Party invites", "Auto-decline incoming party invites during a run, after a human-like delay, with an optional reply."); PartyInviteSettings.Draw(cfg); break;
            case Tab.GmAlert:   DrawHeader("GM alert",  "Detects nearby Game Masters and reacts — stop the bot, ping you, or take more drastic action.");  GmAlertSettings.Draw(cfg); break;
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
}
