using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class QueueStrip
{
    private static int? dragIndex;

    public static void Draw(Configuration cfg, AutoFateController controller)
    {
        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        var ids = cfg.SelectedZones.Where(byId.ContainsKey).ToList();

        if (ids.Count == 0)
        {
            dragIndex = null;
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No zones picked yet — tick zones below to build your grind order.");
            return;
        }

        var running = controller.Running;
        var scale = ImGuiHelpers.GlobalScale;
        var h = ImGui.GetFrameHeight();
        var gap = 6f * scale;

        float boltW, timesW;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            boltW = ImGui.CalcTextSize(FontAwesomeIcon.Bolt.ToIconString()).X;
            timesW = ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X;
        }

        var mouse = ImGui.GetMousePos();
        var dragActive = !running && dragIndex is not null && ImGui.IsMouseDown(ImGuiMouseButton.Left);

        var regionStart = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var x = regionStart.X;
        var y = regionStart.Y;
        var maxY = y + h;

        int? remove = null;
        var rects = new (Vector2 Min, Vector2 Max)[ids.Count];

        for (var i = 0; i < ids.Count; i++)
        {
            var zone = byId[ids[i]];
            ZoneStateReader.Refresh(zone);
            var m = Measure(zone, i + 1, h, boltW, timesW, scale);

            if (x > regionStart.X && x + m.Total > regionStart.X + avail)
            {
                x = regionStart.X;
                y += h + gap;
            }

            var origin = new Vector2(x, y);
            var end = origin + new Vector2(m.Total, h);
            rects[i] = (origin, end);

            var isDropTarget = dragActive && dragIndex != i && Contains(origin, end, mouse);
            DrawChip(origin, end, m, zone, i, running, isDropTarget, ref remove);

            x += m.Total + gap;
            maxY = Math.Max(maxY, y + h);
        }

        ImGui.SetCursorScreenPos(regionStart);
        ImGui.Dummy(new Vector2(avail, maxY - regionStart.Y));

        (int from, int to)? move = null;
        if (!running && dragIndex is int src)
        {
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                var tgt = -1;
                for (var k = 0; k < rects.Length; k++)
                    if (Contains(rects[k].Min, rects[k].Max, mouse)) tgt = k;
                if (tgt >= 0 && tgt != src) move = (src, tgt);
                dragIndex = null;
            }
            else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                dragIndex = null;
            }
            else if (src < ids.Count)
            {
                DrawDragPreview(byId[ids[src]].Name, mouse);
            }
        }

        var changed = false;
        if (remove is int r) changed = RemoveAtFiltered(cfg.SelectedZones, byId, r);
        else if (move is { } mv) changed = MoveFiltered(cfg.SelectedZones, byId, mv.from, mv.to);
        if (changed) cfg.SaveDebounced();
    }

    private static bool Contains(Vector2 min, Vector2 max, Vector2 p)
        => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

    private readonly record struct ChipMetrics(
        float Total, float BodyW, float PadX, float NumW, float NameW, bool HasBonus,
        float BoltW, float Gap, float Height, string Num, string Name, string Count);

    private static ChipMetrics Measure(ZoneInfo zone, int position, float h, float boltW, float timesW, float scale)
    {
        var padX = 9f * scale;
        var gap = 6f * scale;
        var num = $"{position}";
        var name = zone.Name;
        var numW = ImGui.CalcTextSize(num).X;
        var nameW = ImGui.CalcTextSize(name).X;

        var hasBonus = zone.ActiveFateCount > 0;
        var count = hasBonus ? $"{zone.ActiveFateCount}" : "";
        var countW = hasBonus ? ImGui.CalcTextSize(count).X : 0f;

        var bodyW = padX + numW + gap + nameW
            + (hasBonus ? gap + boltW + 3f * scale + countW : 0f)
            + gap;
        var xW = timesW + gap * 2;
        return new ChipMetrics(bodyW + xW, bodyW, padX, numW, nameW, hasBonus, boltW, gap, h, num, name, count);
    }

    private static void DrawChip(
        Vector2 origin, Vector2 end, ChipMetrics m, ZoneInfo zone, int index, bool running,
        bool isDropTarget, ref int? remove)
    {
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton($"##qchip{zone.TerritoryId}", new Vector2(m.BodyW, m.Height));
        var bodyHovered = ImGui.IsItemHovered();
        if (!running && ImGui.IsItemActivated()) dragIndex = index;
        var beingDragged = dragIndex == index;
        if (!running && (bodyHovered || beingDragged)) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        if (!running && bodyHovered && !beingDragged && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            ImGui.SetTooltip("Drag to reorder");

        ImGui.SetCursorScreenPos(new Vector2(origin.X + m.BodyW, origin.Y));
        var xClicked = ImGui.InvisibleButton($"##qx{zone.TerritoryId}", new Vector2(end.X - origin.X - m.BodyW, m.Height));
        var xHovered = ImGui.IsItemHovered();
        if (!running)
        {
            if (xHovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (xClicked) remove = index;
        }

        var accent = Styling.AccentViolet;
        var bg = running ? Styling.CardBgSoft
            : beingDragged ? Vector4.Lerp(Styling.CardBg, accent, 0.40f)
            : isDropTarget || bodyHovered ? Vector4.Lerp(Styling.CardBg, accent, 0.30f)
            : Vector4.Lerp(Styling.CardBg, accent, 0.18f);
        var border = running ? Styling.BorderDim
            : isDropTarget ? Styling.AccentVioletSoft
            : accent * (bodyHovered || beingDragged ? 0.85f : 0.55f);
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(bg), 6f);
        dl.AddRect(origin, end, ImGui.GetColorU32(border), 6f, ImDrawFlags.None, isDropTarget ? 2f : 1f);

        var dim = running ? Styling.TextMuted : Styling.TextDim;
        var strong = running ? Styling.TextDim : Styling.TextStrong;
        var midY = origin.Y + m.Height * 0.5f;
        var scale = ImGuiHelpers.GlobalScale;
        var cx = origin.X + m.PadX;

        PutText(m.Num, cx, midY, dim);
        cx += m.NumW + m.Gap;
        PutText(m.Name, cx, midY, strong);
        cx += m.NameW;

        if (m.HasBonus)
        {
            cx += m.Gap;
            var bolt = running ? Styling.TextMuted : Styling.AccentAmber;
            using (ImRaii.PushFont(UiBuilder.IconFont))
                PutText(FontAwesomeIcon.Bolt.ToIconString(), cx, midY, bolt);
            cx += m.BoltW + 3f * scale;
            PutText(m.Count, cx, midY, bolt);
        }

        var xColor = running ? Styling.TextMuted : xHovered ? Styling.AccentRose : Styling.TextDim;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            PutText(FontAwesomeIcon.Times.ToIconString(), origin.X + m.BodyW + m.Gap, midY, xColor);

        if (!running && xHovered) ImGui.SetTooltip("Remove from grind order");
    }

    private static void DrawDragPreview(string name, Vector2 mouse)
    {
        var dl = ImGui.GetForegroundDrawList();
        var pad = 6f * ImGuiHelpers.GlobalScale;
        var pos = mouse + new Vector2(14f, 8f) * ImGuiHelpers.GlobalScale;
        var size = ImGui.CalcTextSize(name);
        dl.AddRectFilled(pos - new Vector2(pad, pad * 0.5f), pos + size + new Vector2(pad, pad * 0.5f),
            ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, Styling.AccentViolet, 0.35f)), 5f);
        dl.AddText(pos, ImGui.GetColorU32(Styling.TextStrong), name);
    }

    private static void PutText(string text, float x, float midY, Vector4 color)
    {
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorScreenPos(new Vector2(x, midY - size.Y * 0.5f));
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
    }

    // Chip indices are positions in the filtered (known-zone) view; map them back to raw SelectedZones indices.
    private static int FindId(List<uint> selected, Dictionary<uint, ZoneInfo> byId, int filteredIndex)
    {
        var k = -1;
        for (var i = 0; i < selected.Count; i++)
            if (byId.ContainsKey(selected[i]) && ++k == filteredIndex) return i;
        return -1;
    }

    private static bool RemoveAtFiltered(List<uint> selected, Dictionary<uint, ZoneInfo> byId, int filteredIndex)
    {
        var real = FindId(selected, byId, filteredIndex);
        if (real < 0) return false;
        selected.RemoveAt(real);
        return true;
    }

    private static bool MoveFiltered(List<uint> selected, Dictionary<uint, ZoneInfo> byId, int fromFiltered, int toFiltered)
    {
        var from = FindId(selected, byId, fromFiltered);
        var to = FindId(selected, byId, toFiltered);
        return ListReorder.Move(selected, from, to);
    }
}
