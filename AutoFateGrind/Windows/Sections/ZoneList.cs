using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections;

internal static class ZoneList
{
    public static void Draw(AutoFateController controller, Configuration cfg)
    {
        foreach (var exp in Enum.GetValues<ExpansionKind>().Reverse())
        {
            if (!PassesRegionFilter(cfg.RegionFilter, exp)) continue;

            var zones = ZoneRegistry.ByExpansion(exp).ToArray();
            if (zones.Length == 0) continue;

            DrawSectionHeader(exp, zones, controller, cfg);
            DrawGrid(zones, controller, cfg);
            ImGui.Spacing();
        }
    }

    private static void DrawSectionHeader(ExpansionKind exp, ZoneInfo[] zones, AutoFateController controller, Configuration cfg)
    {
        Styling.SectionLabel(exp.DisplayName());

        var territoryIds = zones.Select(z => z.TerritoryId).ToHashSet();
        var selectedCount = cfg.SelectedZones.Count(territoryIds.Contains);
        var allSelected = selectedCount == zones.Length;

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextUnformatted($" ({selectedCount}/{zones.Length})");

        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - 180 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(controller.Running))
        {
            if (ImGui.SmallButton($"{(allSelected ? "Deselect" : "Select")} all##{exp}"))
            {
                if (allSelected)
                    cfg.SelectedZones.RemoveAll(territoryIds.Contains);
                else
                    foreach (var id in territoryIds)
                        if (!cfg.SelectedZones.Contains(id))
                            cfg.SelectedZones.Add(id);
                cfg.SaveDebounced();
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(selectedCount == 0))
                if (ImGui.SmallButton($"Clear##{exp}"))
                {
                    cfg.SelectedZones.RemoveAll(territoryIds.Contains);
                    cfg.SaveDebounced();
                }
        }
    }

    private static void DrawGrid(ZoneInfo[] zones, AutoFateController controller, Configuration cfg)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var minCardWidth = Layout.ZoneCardMinWidth * ImGuiHelpers.GlobalScale;
        var columns = Math.Max(1, Math.Min(zones.Length, (int)(avail / minCardWidth)));

        using var table = ImRaii.Table($"##grid_{zones[0].Expansion}", columns,
            ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoBordersInBody);
        if (!table) return;

        foreach (var zone in zones)
        {
            ZoneStateReader.Refresh(zone);
            ImGui.TableNextColumn();
            ZoneCard.Draw(zone, controller, cfg);
        }
    }

    private static bool PassesRegionFilter(ExpansionFilter filter, ExpansionKind exp) => filter switch
    {
        ExpansionFilter.All => true,
        ExpansionFilter.ShB => exp == ExpansionKind.ShB,
        ExpansionFilter.EW  => exp == ExpansionKind.EW,
        ExpansionFilter.DT  => exp == ExpansionKind.DT,
        _ => true,
    };
}
