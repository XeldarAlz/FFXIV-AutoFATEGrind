using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class HumanizerSettings
{
    public static void Draw(Configuration cfg)
    {
        DrawBreaksGroup(cfg);
        if (!cfg.HumanizerEnabled)
        {
            return;
        }

        DrawWanderingGroup(cfg);
        DrawCitiesGroup(cfg);
    }

    private static void DrawBreaksGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.HumanizerBreaks));

        SettingsRow.Draw(Loc.T(L.Settings.HumanizerEnable),
            Loc.T(L.Settings.HumanizerEnableHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.HumanizerEnabled, v => cfg.HumanizerEnabled = v, "##hum_on"),
            SettingsRow.ToggleHeight);

        if (!cfg.HumanizerEnabled)
        {
            SettingsRow.Note(Loc.T(L.Settings.HumanizerOff));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.FatesBetween),
            Loc.T(L.Settings.FatesBetweenHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##hum_fates",
                () => cfg.HumanizerFatesBeforeBreak, v => cfg.HumanizerFatesBeforeBreak = Math.Clamp(v, 1, 100),
                1, 100, Loc.T(L.Settings.FatesFormat)));

        SettingsRow.Draw(Loc.T(L.Settings.BreakLength),
            Loc.T(L.Settings.BreakLengthHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##hum_min", "##hum_max",
                () => cfg.HumanizerBreakMinMinutes, v => cfg.HumanizerBreakMinMinutes = v,
                () => cfg.HumanizerBreakMaxMinutes, v => cfg.HumanizerBreakMaxMinutes = v, 60, 1, Loc.T(L.Settings.MinutesFormat)));
    }

    private static void DrawWanderingGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.HumanizerWandering));

        SettingsRow.Draw(Loc.T(L.Settings.PauseBetween),
            Loc.T(L.Settings.PauseBetweenHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##hum_pause_min", "##hum_pause_max",
                () => cfg.HumanizerPauseMinSec, v => cfg.HumanizerPauseMinSec = v,
                () => cfg.HumanizerPauseMaxSec, v => cfg.HumanizerPauseMaxSec = v, 60, 0, Loc.T(L.Settings.SecondsFormat)));

        SettingsRow.Draw(Loc.T(L.Settings.WalkDistance),
            Loc.T(L.Settings.WalkDistanceHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##hum_wander_min", "##hum_wander_max",
                () => cfg.HumanizerWanderMinMeters, v => cfg.HumanizerWanderMinMeters = v,
                () => cfg.HumanizerWanderMaxMeters, v => cfg.HumanizerWanderMaxMeters = v, 200, 5, Loc.T(L.Settings.MetersFormat)));
    }

    private static void DrawCitiesGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.HumanizerCities));

        SettingsRow.DrawBlock(Loc.T(L.Settings.AllowedCities),
            Loc.T(L.Settings.AllowedCitiesHelp),
            () => DrawHumanizerCityList(cfg));
    }

    private static void DrawHumanizerCityList(Configuration cfg)
    {
        var grouped = CityCatalog.All.GroupBy(c => c.Expansion).OrderByDescending(g => g.Key);
        foreach (var group in grouped)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted(ExpansionLabels.Name(group.Key));

            foreach (var city in group)
            {
                var selected = cfg.HumanizerCities.Contains(city.TerritoryId);
                var id = $"##hum_city_{city.TerritoryId}";
                if (ToggleSwitch.Draw(id, ref selected))
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
                ImGui.TextWrapped(Loc.T(L.Settings.NoCities));
    }
}
