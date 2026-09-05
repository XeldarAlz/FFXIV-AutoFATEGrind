using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class QueueStrip
{
    private const float ChipHeight = 32f;
    private const float Gap = 8f;

    private static int? dragIndex;

    public static void Draw(Configuration cfg, AutoFateController controller)
    {
        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        var ids = cfg.SelectedZones.Where(byId.ContainsKey).ToList();

        if (ids.Count == 0)
        {
            dragIndex = null;
            using (Fonts.PushCaption())
            {
                var origin = ImGui.GetCursorScreenPos();
                var hint = Loc.T(L.Grind.OrderHint);
                TextDraw.At(hint, new Vector2(origin.X + 2f * ImGuiHelpers.GlobalScale, origin.Y), Styling.TextMuted);
                ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, TextDraw.Measure(hint).Y));
            }

            return;
        }

        var running = controller.Running;
        var scale = ImGuiHelpers.GlobalScale;
        var height = ChipHeight * scale;
        var gap = Gap * scale;

        var boltWidth = TextDraw.IconSize(FontAwesomeIcon.Bolt).X;
        var timesWidth = TextDraw.IconSize(FontAwesomeIcon.Times).X;

        var mouse = ImGui.GetMousePos();
        var dragActive = !running && dragIndex is not null && ImGui.IsMouseDown(ImGuiMouseButton.Left);

        var regionStart = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var x = regionStart.X;
        var y = regionStart.Y;
        var maxY = y + height;

        int? remove = null;
        var rects = new (Vector2 Min, Vector2 Max)[ids.Count];

        for (var index = 0; index < ids.Count; index++)
        {
            var zone = byId[ids[index]];
            ZoneStateReader.Refresh(zone);
            var metrics = Measure(zone, index + 1, height, boltWidth, timesWidth, scale);

            if (x > regionStart.X && x + metrics.Total > regionStart.X + avail)
            {
                x = regionStart.X;
                y += height + gap;
            }

            var origin = new Vector2(x, y);
            var end = origin + new Vector2(metrics.Total, height);
            rects[index] = (origin, end);

            var isDropTarget = dragActive && dragIndex != index && Contains(origin, end, mouse);
            DrawChip(origin, end, metrics, zone, index, running, isDropTarget, ref remove);

            x += metrics.Total + gap;
            maxY = Math.Max(maxY, y + height);
        }

        ImGui.SetCursorScreenPos(regionStart);
        ImGui.Dummy(new Vector2(avail, maxY - regionStart.Y));

        (int From, int To)? move = null;
        if (!running && dragIndex is int source)
        {
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                var target = -1;
                for (var index = 0; index < rects.Length; index++)
                {
                    if (Contains(rects[index].Min, rects[index].Max, mouse)) target = index;
                }

                if (target >= 0 && target != source) move = (source, target);
                dragIndex = null;
            }
            else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                dragIndex = null;
            }
            else if (source < ids.Count)
            {
                DrawDragPreview(byId[ids[source]].Name, mouse);
            }
        }

        var changed = false;
        if (remove is int removeIndex) changed = RemoveAtFiltered(cfg.SelectedZones, byId, removeIndex);
        else if (move is { } movement) changed = MoveFiltered(cfg.SelectedZones, byId, movement.From, movement.To);
        if (changed) cfg.SaveDebounced();
    }

    private static bool Contains(Vector2 min, Vector2 max, Vector2 p)
        => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

    private readonly record struct ChipMetrics(
        float Total, float BodyWidth, float PadX, float NumberWidth, float NameWidth, bool HasActive,
        float BoltWidth, float Gap, float Height, string Number, string Name, string Count);

    private static ChipMetrics Measure(ZoneInfo zone, int position, float height, float boltWidth, float timesWidth, float scale)
    {
        var padX = 11f * scale;
        var gap = 6f * scale;
        var number = position.ToString();
        var name = zone.Name;
        var numberWidth = TextDraw.Measure(number).X;
        var nameWidth = TextDraw.Measure(name).X;

        var hasActive = zone.ActiveFateCount > 0;
        var count = hasActive ? zone.ActiveFateCount.ToString() : string.Empty;
        var countWidth = hasActive ? TextDraw.Measure(count).X : 0f;

        var bodyWidth = padX + numberWidth + gap + nameWidth
            + (hasActive ? gap + boltWidth + 3f * scale + countWidth : 0f)
            + gap;
        var closeWidth = timesWidth + gap * 2f;
        return new ChipMetrics(bodyWidth + closeWidth, bodyWidth, padX, numberWidth, nameWidth, hasActive, boltWidth, gap, height, number, name, count);
    }

    private static void DrawChip(
        Vector2 origin, Vector2 end, ChipMetrics metrics, ZoneInfo zone, int index, bool running,
        bool isDropTarget, ref int? remove)
    {
        var dl = ImGui.GetWindowDrawList();
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton($"##qchip{zone.TerritoryId}", new Vector2(metrics.BodyWidth, metrics.Height));
        var bodyHovered = ImGui.IsItemHovered();
        if (!running && ImGui.IsItemActivated()) dragIndex = index;
        var beingDragged = dragIndex == index;
        if (!running && (bodyHovered || beingDragged)) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        if (!running && bodyHovered && !beingDragged && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            ImGui.SetTooltip(Loc.T(L.Grind.DragToReorder));

        ImGui.SetCursorScreenPos(new Vector2(origin.X + metrics.BodyWidth, origin.Y));
        var closeClicked = ImGui.InvisibleButton($"##qx{zone.TerritoryId}", new Vector2(end.X - origin.X - metrics.BodyWidth, metrics.Height));
        var closeHovered = ImGui.IsItemHovered();
        if (!running)
        {
            if (closeHovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (closeClicked) remove = index;
        }

        var hover = Motion.Hover(Motion.Key("##qchip", zone.TerritoryId), !running && (bodyHovered || beingDragged));
        var accent = Styling.AccentViolet;
        var rounding = metrics.Height * 0.5f;
        var tint = running ? 0.06f : beingDragged ? 0.55f : isDropTarget ? 0.5f : 0.28f + 0.14f * hover;
        var top = Styling.Tint(Styling.Surface2, accent, tint);
        var bottom = Styling.Tint(Styling.Surface1, accent, tint * 0.8f);
        Paint.Gradient(dl, origin, end, top, bottom, rounding);
        Paint.TopLight(dl, origin, end, rounding, 0.09f);
        var border = running ? Styling.WithAlpha(Styling.BorderDim, 0.6f)
            : isDropTarget ? Styling.AccentVioletSoft
            : Styling.WithAlpha(accent, 0.5f + 0.35f * hover);
        Paint.Stroke(dl, origin, end, border, rounding, isDropTarget ? 1.8f : 1f);

        var dim = running ? Styling.TextMuted : Styling.TextDim;
        var strong = running ? Styling.TextDim : Styling.TextStrong;
        var midY = origin.Y + metrics.Height * 0.5f;
        var cursorX = origin.X + metrics.PadX;

        PutText(metrics.Number, cursorX, midY, dim);
        cursorX += metrics.NumberWidth + metrics.Gap;
        PutText(metrics.Name, cursorX, midY, strong);
        cursorX += metrics.NameWidth;

        if (metrics.HasActive)
        {
            cursorX += metrics.Gap;
            var bolt = running ? Styling.TextMuted : Styling.AccentAmber;
            var boltSize = TextDraw.IconSize(FontAwesomeIcon.Bolt);
            TextDraw.Icon(FontAwesomeIcon.Bolt, new Vector2(cursorX, midY - boltSize.Y * 0.5f), bolt);
            cursorX += metrics.BoltWidth + 3f * scale;
            PutText(metrics.Count, cursorX, midY, bolt);
        }

        var closeColor = running ? Styling.TextMuted : closeHovered ? Styling.AccentRose : Styling.TextDim;
        var closeSize = TextDraw.IconSize(FontAwesomeIcon.Times);
        TextDraw.Icon(FontAwesomeIcon.Times, new Vector2(origin.X + metrics.BodyWidth + metrics.Gap, midY - closeSize.Y * 0.5f), closeColor);

        if (!running && closeHovered) ImGui.SetTooltip(Loc.T(L.Grind.RemoveFromOrder));
    }

    private static void DrawDragPreview(string name, Vector2 mouse)
    {
        var dl = ImGui.GetForegroundDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var pad = new Vector2(10f, 5f) * scale;
        var pos = mouse + new Vector2(14f, 8f) * scale;
        var size = ImGui.CalcTextSize(name);
        var min = pos - pad;
        var max = pos + size + pad;
        Paint.Shadow(dl, min, max, (max.Y - min.Y) * 0.5f, 6f * scale, 0.5f);
        Paint.Pill(dl, min, max, Styling.Tint(Styling.Surface2, Styling.AccentViolet, 0.45f), Styling.WithAlpha(Styling.AccentVioletSoft, 0.7f));
        dl.AddText(pos, Paint.Col(Styling.TextStrong), name);
    }

    private static void PutText(string text, float x, float midY, Vector4 color)
    {
        var size = TextDraw.Measure(text);
        TextDraw.At(text, new Vector2(x, midY - size.Y * 0.5f), color);
    }

    private static int FindId(List<uint> selected, Dictionary<uint, ZoneInfo> byId, int filteredIndex)
    {
        var seen = -1;
        for (var index = 0; index < selected.Count; index++)
        {
            if (byId.ContainsKey(selected[index]) && ++seen == filteredIndex) return index;
        }

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
