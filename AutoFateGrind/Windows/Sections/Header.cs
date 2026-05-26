using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections;

internal static class Header
{
    private static readonly string[] ModeLabels =
    [
        "Endless",
        "Max Shared FATEs",
        "Max Gemstones",
        "Run N FATEs",
    ];

    private static readonly string[] RegionLabels =
    [
        "All regions",
        "Shadowbringers",
        "Endwalker",
        "Dawntrail",
    ];

    public static void Draw(AutoFateController controller, Configuration cfg)
    {
        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        var selected = cfg.SelectedZones
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToArray();

        var runnable = selected
            .Where(z => z.Unlocked && !z.AchievementDone)
            .ToArray();

        var canRun = runnable.Length > 0 && !controller.Running;
        if (ActionButton.Draw($"Run selected ({runnable.Length})", enabled: canRun, width: 200))
            controller.RunAll(runnable);
        Tooltip.For(selected.Length == 0
            ? "Click a zone card below to add it to the batch."
            : runnable.Length < selected.Length
                ? $"{selected.Length} selected, {runnable.Length} runnable. Locked / done zones are skipped."
                : $"Runs {runnable.Length} zone(s) back-to-back. Loops within each zone until empty, then rotates.");

        ImGui.SameLine();
        using (ImRaii.Disabled(!controller.Running))
            if (ImGui.Button("Stop"))
                controller.Stop();

        if (selected.Length > 0)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(controller.Running))
                if (ImGui.Button("Clear selection"))
                {
                    cfg.SelectedZones.Clear();
                    cfg.SaveDebounced();
                }
        }

        ImGui.Spacing();
        DrawModeAndFilters(cfg);
        ImGui.Separator();
    }

    private static void DrawModeAndFilters(Configuration cfg)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Mode:");
        ImGui.SameLine();
        var modeIdx = (int)cfg.Mode;
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo("##mode", ref modeIdx, ModeLabels, ModeLabels.Length))
        {
            cfg.Mode = (GrindMode)modeIdx;
            cfg.SaveDebounced();
        }
        Tooltip.For(ModeTooltip(cfg.Mode));

        if (cfg.Mode == GrindMode.RunCount)
        {
            ImGui.SameLine();
            var count = cfg.TargetFateCount;
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("##count", ref count, 5, 25))
            {
                cfg.TargetFateCount = Math.Clamp(count, 1, 9999);
                cfg.SaveDebounced();
            }
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("FATEs");
        }

        ImGui.SameLine();
        ImGui.TextUnformatted("    Region:");
        ImGui.SameLine();
        var regionIdx = (int)cfg.RegionFilter;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##region", ref regionIdx, RegionLabels, RegionLabels.Length))
        {
            cfg.RegionFilter = (ExpansionFilter)regionIdx;
            cfg.SaveDebounced();
        }
        Tooltip.For("Filters which expansion's zones are visible below. Hidden zones are also skipped by Run selected.");
    }

    private static string ModeTooltip(GrindMode mode) => mode switch
    {
        GrindMode.Endless      => "Run forever until you press Stop. Rotates between selected zones.",
        GrindMode.MaxFates     => "Stops once every selected zone's 'Date with Destiny' achievement is complete.",
        GrindMode.MaxGemstones => $"Stops when you hit the 1500 Bicolor Gemstone cap.",
        GrindMode.RunCount     => "Stops after the specified number of FATEs has been completed across all selected zones.",
        _ => "",
    };
}
