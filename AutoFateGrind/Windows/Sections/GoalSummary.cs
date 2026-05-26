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

        ImGui.Spacing();

        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        var runnable = cfg.SelectedZones.Where(byId.ContainsKey).Count();
        var canStart = runnable > 0 && !controller.Running;
        var label = canStart ? $"START   ({runnable} zone{(runnable == 1 ? "" : "s")})" : "START";

        if (PrimaryButton.Draw(label, Styling.AccentViolet, canStart))
            controller.RunAll(cfg.SelectedZones.Where(byId.ContainsKey).Select(id => byId[id]));

        if (!canStart && runnable == 0)
            Tooltip.For("Pick at least one zone below.");
    }

    private static unsafe string SummaryFor(Configuration cfg) => cfg.Mode switch
    {
        GrindMode.MaxGemstones => $"Stops at {cfg.TradeThreshold} Bicolor Gemstones (you have {GemstoneCount()}).",
        GrindMode.MaxFates     => "Stops when every selected zone's 'Free Market Friend' achievement (60 Shared FATEs) is complete.",
        GrindMode.RunCount     => "Stops after",
        GrindMode.Endless      => "Runs forever until you press Stop. Rotates between selected zones.",
        _ => "",
    };

    private static unsafe int GemstoneCount()
    {
        const uint bicolorItemId = 26807;
        var im = InventoryManager.Instance();
        return im is null ? 0 : im->GetInventoryItemCount(bicolorItemId);
    }
}
