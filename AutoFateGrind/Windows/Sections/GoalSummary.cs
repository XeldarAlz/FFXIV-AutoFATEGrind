using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoFateGrind.Windows.Sections;

internal static class GoalSummary
{
    public static void Draw(Configuration cfg, AutoFateController controller)
    {
        ImGui.Spacing();

        var summary = SummaryFor(cfg);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
            ImGui.TextUnformatted(summary);

        if (cfg.Mode == GrindMode.RunCount)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            var count = cfg.TargetFateCount;
            if (ImGui.InputInt("##cnt", ref count, 5, 25))
            {
                cfg.TargetFateCount = Math.Clamp(count, 1, 9999);
                cfg.SaveDebounced();
            }
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted("FATEs");
        }
        else if (cfg.Mode == GrindMode.MaxGemstones)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            var target = cfg.TargetGemstoneCount;
            if (ImGui.InputInt("##gemcnt", ref target, 50, 250))
            {
                cfg.TargetGemstoneCount = Math.Clamp(target, 1, 1500);
                cfg.SaveDebounced();
            }
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted($"gems  ·  have {GemstoneCount()}");
        }

        ImGui.Spacing();

        var startList = ZoneSelection.ResolveStartList(cfg);
        var runnable = startList.Count;
        var canStart = runnable > 0 && !controller.Running;
        var label = canStart ? $"START   ({runnable} zone{(runnable == 1 ? "" : "s")})" : "START";

        if (PrimaryButton.Draw(label, Styling.AccentViolet, canStart))
            controller.RunAll(startList);

        if (!canStart && runnable == 0)
            Tooltip.For(ZoneSelection.IsAutoSelected(cfg)
                ? "All Shared FATE achievements are already complete."
                : "Pick at least one zone below.");
    }

    private static unsafe string SummaryFor(Configuration cfg) => cfg.Mode switch
    {
        GrindMode.MaxGemstones => "Stops at",
        GrindMode.MaxFates     => "Auto-rotates ShB / EW / DT zones until every Shared FATE achievement is complete.",
        GrindMode.RunCount     => "Stops after",
        GrindMode.Endless      => "Rotates selected zones until you press Stop.",
        _ => "",
    };

    private static unsafe int GemstoneCount()
    {
        const uint bicolorItemId = 26807;
        var im = InventoryManager.Instance();
        return im is null ? 0 : im->GetInventoryItemCount(bicolorItemId);
    }
}
