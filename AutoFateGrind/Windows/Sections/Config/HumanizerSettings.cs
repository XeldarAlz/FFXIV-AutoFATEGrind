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
        using var group = SettingsGroup.Begin("Breaks");

        SettingsRow.Draw("Take periodic city breaks",
            "Every N FATEs, teleport to a random selected city and wander around for a few minutes before resuming. Helps you avoid player reports by acting a little more human, useful when you leave the PC running for long sessions and don't want others noticing you grinding FATEs non-stop.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.HumanizerEnabled, v => cfg.HumanizerEnabled = v, "##hum_on"),
            SettingsRow.ToggleHeight);

        if (!cfg.HumanizerEnabled)
        {
            SettingsRow.Note("Humanizer is off. Enable it to configure breaks.");
            return;
        }

        SettingsRow.Draw("FATEs between breaks",
            "Take a break after this many completed FATEs. The counter resets after each break.",
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##hum_fates",
                () => cfg.HumanizerFatesBeforeBreak, v => cfg.HumanizerFatesBeforeBreak = Math.Clamp(v, 1, 100),
                1, 100, "%d FATEs"));

        SettingsRow.Draw("Break length",
            "A random duration between these two values is rolled for each break.",
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##hum_min", "##hum_max",
                () => cfg.HumanizerBreakMinMinutes, v => cfg.HumanizerBreakMinMinutes = v,
                () => cfg.HumanizerBreakMaxMinutes, v => cfg.HumanizerBreakMaxMinutes = v, 60, 1, "%d min"));
    }

    private static void DrawWanderingGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Wandering");

        SettingsRow.Draw("Pause between walks",
            "After arriving at each random point, stand still for a random duration in this range before walking somewhere else.",
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##hum_pause_min", "##hum_pause_max",
                () => cfg.HumanizerPauseMinSec, v => cfg.HumanizerPauseMinSec = v,
                () => cfg.HumanizerPauseMaxSec, v => cfg.HumanizerPauseMaxSec = v, 60, 0, "%d s"));

        SettingsRow.Draw("Walk distance",
            "Each random destination is rolled this many meters away from your current position. Larger ranges cover more of the city; smaller ranges keep you near the aetheryte.",
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##hum_wander_min", "##hum_wander_max",
                () => cfg.HumanizerWanderMinMeters, v => cfg.HumanizerWanderMinMeters = v,
                () => cfg.HumanizerWanderMaxMeters, v => cfg.HumanizerWanderMaxMeters = v, 200, 5, "%d m"));
    }

    private static void DrawCitiesGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Cities");

        SettingsRow.DrawBlock("Allowed cities",
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
                ImGui.TextWrapped("No cities selected - Humanizer will skip the break and keep grinding.");
    }
}
