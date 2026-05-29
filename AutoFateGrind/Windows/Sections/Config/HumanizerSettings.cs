using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class HumanizerSettings
{
    public static void Draw(Configuration cfg)
    {
        SettingsRow.Draw("Take periodic city breaks",
            "Every N FATEs, teleport to a random selected city and wander around for a few minutes before resuming. Helps you avoid player reports by acting a little more human — useful when you leave the PC running for long sessions and don't want others noticing you grinding FATEs non-stop.",
            () => SettingsControls.DrawToggle(cfg, () => cfg.HumanizerEnabled, v => cfg.HumanizerEnabled = v, "##hum_on"));

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
                ImGui.TextWrapped("No cities selected — Humanizer will skip the break and keep grinding.");
    }
}
