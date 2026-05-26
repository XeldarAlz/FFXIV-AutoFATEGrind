using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;

namespace AutoFateGrind.Windows.Sections;

// Inline tracker. Renders only when the player is currently in one of the selected
// zones. The separate-window popout variant is gated by Configuration.ShowLivePopout
// (will be added once the engine is in).
internal static class LiveFateTracker
{
    public static void Draw(Configuration cfg)
    {
        var currentTerritory = Svc.ClientState.TerritoryType;
        var zone = ZoneRegistry.Zones.FirstOrDefault(z => z.TerritoryId == currentTerritory);
        if (zone is null || !cfg.SelectedZones.Contains(currentTerritory)) return;

        ImGui.Spacing();
        Styling.SectionLabel($"Live FATEs in {zone.Name}");

        var active = Svc.Fates
            .Where(f => f.State == FateState.Running)
            .OrderBy(f => f.TimeRemaining)
            .ToArray();

        if (active.Length == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted("No active FATEs right now.");
            return;
        }

        foreach (var fate in active)
        {
            FateRow.Draw(fate);
            ImGui.Spacing();
        }
    }
}
