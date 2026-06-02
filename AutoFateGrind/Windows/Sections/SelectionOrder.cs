using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections;

internal static class SelectionOrder
{
    public static void Draw(Configuration cfg, AutoFateController controller)
    {
        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        var ids = cfg.SelectedZones.Where(byId.ContainsKey).ToList();

        if (ids.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No zones selected. Pick zones from the expansion tabs above.");
            return;
        }

        // Deferred so the for-loop doesn't trip over self-mutation.
        int? moveUp = null, moveDown = null, remove = null;

        var btnSize = ImGui.GetFrameHeight();
        var spacingX = 4f * ImGuiHelpers.GlobalScale;
        var rightMargin = Layout.RowRightMargin * ImGuiHelpers.GlobalScale;
        var rowRightWidth = btnSize * 3 + spacingX * 2 + rightMargin;

        for (var i = 0; i < ids.Count; i++)
        {
            var z = byId[ids[i]];
            DrawRow(i, ids.Count, z, controller.Running, btnSize, spacingX, rowRightWidth,
                onUp: () => moveUp = i,
                onDown: () => moveDown = i,
                onRemove: () => remove = i);
        }

        if (ListReorder.Apply(cfg.SelectedZones, ids.Count, moveUp, moveDown, remove))
            cfg.SaveDebounced();
    }

    private static void DrawRow(
        int index, int total, ZoneInfo zone, bool running,
        float btnSize, float spacingX, float rowRightWidth,
        Action onUp, Action onDown, Action onRemove)
    {
        using (ImRaii.Disabled(running))
        {
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted($"{index + 1}.");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                ImGui.TextUnformatted(zone.Name);

            var rightStart = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - rowRightWidth;
            ImGui.SameLine(rightStart);

            using (ImRaii.Disabled(index == 0))
                if (IconButton.Draw(FontAwesomeIcon.ArrowUp, $"##up{index}_{zone.TerritoryId}", btnSize))
                    onUp();
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(index == total - 1))
                if (IconButton.Draw(FontAwesomeIcon.ArrowDown, $"##dn{index}_{zone.TerritoryId}", btnSize))
                    onDown();
            ImGui.SameLine(0, spacingX);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                if (IconButton.Draw(FontAwesomeIcon.Times, $"##rm{index}_{zone.TerritoryId}", btnSize))
                    onRemove();
        }
    }
}
