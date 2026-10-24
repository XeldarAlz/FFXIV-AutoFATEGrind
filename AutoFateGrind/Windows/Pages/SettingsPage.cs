using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;
using AutoFateGrind.Windows.Sections.Config;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Glasswork;
using Glasswork.Backend;
using System.Numerics;

namespace AutoFateGrind.Windows.Pages;

internal sealed class SettingsPage
{
    private enum Tab { General, Travel, Filters, Classes, Gemstones, Repair, Consumables, Humanize, PartyInvites, GmAlert }

    private readonly record struct Entry(Tab Tab, LocString Label, FontAwesomeIcon Icon, LocString Subtitle);

    private static readonly Entry[] entries =
    [
        new(Tab.General,      L.Settings.CatGeneral,      FontAwesomeIcon.Cog,        L.Settings.CatGeneralSub),
        new(Tab.Travel,       L.Settings.CatTravel,       FontAwesomeIcon.Route,      L.Settings.CatTravelSub),
        new(Tab.Filters,      L.Settings.CatFilters,      FontAwesomeIcon.Filter,     L.Settings.CatFiltersSub),
        new(Tab.Classes,      L.Settings.CatClasses,      FontAwesomeIcon.UserShield, L.Settings.CatClassesSub),
        new(Tab.Gemstones,    L.Settings.CatGemstones,    FontAwesomeIcon.Gem,        L.Settings.CatGemstonesSub),
        new(Tab.Repair,       L.Settings.CatRepair,       FontAwesomeIcon.Wrench,     L.Settings.CatRepairSub),
        new(Tab.Consumables,  L.Settings.CatConsumables,  FontAwesomeIcon.Utensils,   L.Settings.CatConsumablesSub),
        new(Tab.Humanize,     L.Settings.CatHumanizer,    FontAwesomeIcon.Walking,    L.Settings.CatHumanizerSub),
        new(Tab.PartyInvites, L.Settings.CatPartyInvites, FontAwesomeIcon.UserSlash,  L.Settings.CatPartyInvitesSub),
        new(Tab.GmAlert,      L.Settings.CatGmAlert,      FontAwesomeIcon.UserSecret, L.Settings.CatGmAlertSub),
    ];

    private Tab activeTab = Tab.General;
    private bool resetScroll;

    public void Draw(Plugin plugin)
    {
        var cfg = plugin.Configuration;
        var scale = ImGuiHelpers.GlobalScale;
        var navWidth = Layout.SettingsNavWidth * scale;

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero))
        {
            using (var nav = ImRaii.Child("##afg_settings_nav", new Vector2(navWidth, -1f), false, ImGuiWindowFlags.NoScrollbar))
            {
                if (nav) DrawNav();
            }

            ImGui.SameLine(0f, 18f * scale);

            using (var content = ImRaii.Child("##afg_settings_content", new Vector2(-1f, -1f), false, ImGuiWindowFlags.None))
            {
                if (content) DrawContent(cfg);
            }
        }
    }

    private void DrawNav()
    {
        Gw.Heading(Loc.T(L.Settings.Title));
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (SidebarTab.Draw(Loc.T(entry.Label), entry.Icon, Styling.AccentViolet, activeTab == entry.Tab)) Select(entry.Tab);
        }
    }

    private void Select(Tab tab)
    {
        if (activeTab == tab) return;
        activeTab = tab;
        resetScroll = true;
    }

    private void DrawContent(Configuration cfg)
    {
        if (resetScroll)
        {
            ImGui.SetScrollY(0f);
            resetScroll = false;
        }

        var entry = entries[(int)activeTab];
        Gw.Heading(Loc.T(entry.Label));
        Gw.TextWrapped(Loc.T(entry.Subtitle), TextRole.Subheadline, Frame.Look.SecondaryLabel);
        Gw.Divider();

        switch (activeTab)
        {
            case Tab.General: GeneralSettings.Draw(cfg); break;
            case Tab.Travel: TravelSettings.Draw(cfg); break;
            case Tab.Filters: FilterSettings.Draw(cfg); break;
            case Tab.Classes: ClassSettings.Draw(cfg); break;
            case Tab.Gemstones: GemstoneSettings.Draw(cfg); break;
            case Tab.Repair: RepairSettings.Draw(cfg); break;
            case Tab.Consumables: ConsumableSettings.Draw(cfg); break;
            case Tab.Humanize: HumanizerSettings.Draw(cfg); break;
            case Tab.PartyInvites: PartyInviteSettings.Draw(cfg); break;
            case Tab.GmAlert: GmAlertSettings.Draw(cfg); break;
        }
    }
}
