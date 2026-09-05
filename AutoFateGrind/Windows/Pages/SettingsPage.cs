using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;
using AutoFateGrind.Windows.Sections.Config;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Pages;

internal sealed class SettingsPage
{
    private enum Tab { General, Filters, Classes, Gemstones, Repair, Consumables, Humanize, PartyInvites, GmAlert }

    private readonly record struct Entry(Tab Tab, LocString Label, FontAwesomeIcon Icon, LocString Subtitle);

    private static readonly Entry[] entries =
    [
        new(Tab.General,      L.Settings.CatGeneral,      FontAwesomeIcon.Cog,        L.Settings.CatGeneralSub),
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
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var title = Loc.T(L.Settings.Title);
        using (Fonts.PushTitle())
        {
            TextDraw.At(title, new Vector2(origin.X + 6f * scale, origin.Y), Styling.TextStrong);
            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, TextDraw.Measure(title).Y + 10f * scale));
        }

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (SidebarTab.Draw(Loc.T(entry.Label), entry.Icon, Styling.AccentViolet, activeTab == entry.Tab)) activeTab = entry.Tab;
        }
    }

    private void DrawContent(Configuration cfg)
    {
        var entry = entries[(int)activeTab];
        var scale = ImGuiHelpers.GlobalScale;

        using var group = ImRaii.Group();
        ImGui.Dummy(new Vector2(0f, 2f * scale));
        PageHeader.Draw(Loc.T(entry.Label), Loc.T(entry.Subtitle));

        switch (activeTab)
        {
            case Tab.General: GeneralSettings.Draw(cfg); break;
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
