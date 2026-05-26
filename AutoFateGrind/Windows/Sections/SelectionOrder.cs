using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

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

        // Defer mutations so the for-loop doesn't trip over self-mutation.
        int? moveUp = null, moveDown = null, remove = null;

        // Square icon buttons. We pass explicit spacing to SameLine and add a hard
        // right margin so the row can't clip regardless of GlobalScale or tab insets.
        var btnSize = ImGui.GetFrameHeight();
        var spacingX = 4f * ImGuiHelpers.GlobalScale;
        var rightMargin = 8f * ImGuiHelpers.GlobalScale;
        var rowRightWidth = btnSize * 3 + spacingX * 2 + rightMargin;

        for (var i = 0; i < ids.Count; i++)
        {
            var z = byId[ids[i]];
            DrawRow(i, ids.Count, z, controller.Running, btnSize, spacingX, rowRightWidth,
                onUp: () => moveUp = i,
                onDown: () => moveDown = i,
                onRemove: () => remove = i);
        }

        if (moveUp is int mu && mu > 0)
        {
            (cfg.SelectedZones[mu - 1], cfg.SelectedZones[mu]) = (cfg.SelectedZones[mu], cfg.SelectedZones[mu - 1]);
            cfg.SaveDebounced();
        }
        else if (moveDown is int md && md < ids.Count - 1)
        {
            (cfg.SelectedZones[md + 1], cfg.SelectedZones[md]) = (cfg.SelectedZones[md], cfg.SelectedZones[md + 1]);
            cfg.SaveDebounced();
        }
        else if (remove is int r)
        {
            cfg.SelectedZones.RemoveAt(r);
            cfg.SaveDebounced();
        }
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
                if (DrawIconBtn(FontAwesomeIcon.ArrowUp, $"##up{index}_{zone.TerritoryId}", btnSize))
                    onUp();
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(index == total - 1))
                if (DrawIconBtn(FontAwesomeIcon.ArrowDown, $"##dn{index}_{zone.TerritoryId}", btnSize))
                    onDown();
            ImGui.SameLine(0, spacingX);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                if (DrawIconBtn(FontAwesomeIcon.Times, $"##rm{index}_{zone.TerritoryId}", btnSize))
                    onRemove();
        }
    }

    private static bool DrawIconBtn(FontAwesomeIcon icon, string id, float size)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGui.Button(icon.ToIconString() + id, new Vector2(size, size));
    }
}
