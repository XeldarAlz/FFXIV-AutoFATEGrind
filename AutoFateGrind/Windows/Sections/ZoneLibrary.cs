using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class ZoneLibrary
{
    private const float Gap = 8f;
    private const float SummaryRowHeight = 32f;
    private const int CachedLabelCount = 64;
    private const int SearchMaxLength = 64;

    private static readonly ExpansionKind[] expansions =
        [ExpansionKind.DT, ExpansionKind.EW, ExpansionKind.ShB, ExpansionKind.SB, ExpansionKind.HW, ExpansionKind.ARR];

    private static readonly Segmented.Item[] segments = new Segmented.Item[expansions.Length];
    private static readonly string[] countLabels = BuildLabels(string.Empty);
    private static readonly string[] queueLabels = BuildLabels("#");

    private static ZoneInfo[]? snapshot;
    private static ZoneInfo[][] groups = [];
    private static HashSet<uint> knownIds = [];
    private static int currentExpansion;

    private static string searchQuery = string.Empty;
    private static string matchedQuery = string.Empty;
    private static ZoneInfo[] matches = [];

    public static void Draw(Configuration cfg, AutoFateController ctrl, bool scrollIntoView)
    {
        RefreshGroups();
        DrawHeader(scrollIntoView);
        Styling.VSpace(10f);

        if (searchQuery.Length > 0)
        {
            EnsureMatches();
            DrawSearchSummary();
            DrawGrid(cfg, ctrl, matches, showExpansion: true);
            return;
        }

        for (var index = 0; index < expansions.Length; index++)
        {
            segments[index] = new Segmented.Item(null, ExpansionLabels.Name(expansions[index]));
        }

        Segmented.Draw("##afg_expansions", segments, ref currentExpansion);
        Styling.VSpace(8f);

        var zones = groups[currentExpansion];
        DrawSummaryRow(cfg, ctrl, zones);
        DrawGrid(cfg, ctrl, zones, showExpansion: false);
    }

    private static void RefreshGroups()
    {
        var zones = ZoneRegistry.Zones;
        if (ReferenceEquals(zones, snapshot)) return;

        snapshot = zones;
        matchedQuery = string.Empty;
        matches = [];
        knownIds = new HashSet<uint>(zones.Length);
        var buckets = new List<ZoneInfo>[expansions.Length];
        for (var index = 0; index < buckets.Length; index++) buckets[index] = [];
        for (var index = 0; index < zones.Length; index++)
        {
            var zone = zones[index];
            knownIds.Add(zone.TerritoryId);
            buckets[(int)zone.Expansion].Add(zone);
        }

        groups = new ZoneInfo[expansions.Length][];
        for (var index = 0; index < buckets.Length; index++) groups[index] = [.. buckets[(int)expansions[index]]];
    }

    private static void EnsureMatches()
    {
        if (matchedQuery == searchQuery) return;

        matchedQuery = searchQuery;
        var zones = ZoneRegistry.Zones;
        var found = new List<ZoneInfo>();
        for (var index = 0; index < zones.Length; index++)
        {
            if (zones[index].Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) found.Add(zones[index]);
        }

        matches = [.. found];
    }

    private static void DrawHeader(bool scrollIntoView)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.LibraryHeaderHeight * scale;
        var midY = origin.Y + height * 0.5f;

        var label = Loc.T(L.Grind.Zones);
        var labelSize = TextDraw.SectionTitleSize(label);
        TextDraw.SectionTitle(label, new Vector2(origin.X, midY - labelSize.Y * 0.5f), Styling.TextStrong);

        var searchWidth = MathF.Min(Layout.SearchWidth * scale, width * 0.5f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - searchWidth, origin.Y));
        DrawSearch(searchWidth, height);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
        if (scrollIntoView) ImGui.SetScrollHereY(0f);
    }

    private static void DrawSearch(float width, float height)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(width, height);
        var dl = ImGui.GetWindowDrawList();
        var hasQuery = searchQuery.Length > 0;

        Paint.Pill(dl, origin, end, Styling.WithAlpha(Styling.Surface0, 0.9f), Styling.WithAlpha(hasQuery ? Styling.AccentViolet : Styling.BorderDim, 0.6f));

        var iconSize = TextDraw.IconSize(FontAwesomeIcon.Search);
        var iconX = origin.X + 12f * scale;
        TextDraw.Icon(FontAwesomeIcon.Search, new Vector2(iconX, origin.Y + (height - iconSize.Y) * 0.5f), hasQuery ? Styling.AccentVioletSoft : Styling.TextDim);

        var clearWidth = hasQuery ? height : 8f * scale;
        var inputX = iconX + iconSize.X + 6f * scale;
        ImGui.SetCursorScreenPos(new Vector2(inputX, origin.Y + (height - ImGui.GetFrameHeight()) * 0.5f));
        ImGui.SetNextItemWidth(end.X - clearWidth - inputX);
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Zero)
            .Push(ImGuiCol.FrameBgHovered, Vector4.Zero)
            .Push(ImGuiCol.FrameBgActive, Vector4.Zero))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(4f * scale, ImGui.GetStyle().FramePadding.Y)))
        {
            ImGui.InputTextWithHint("##afg_zone_search", Loc.T(L.Grind.SearchZones), ref searchQuery, SearchMaxLength);
        }

        if (hasQuery)
        {
            ImGui.SetCursorScreenPos(new Vector2(end.X - height, origin.Y));
            if (IconButton.Draw(FontAwesomeIcon.Times, "##afg_zone_search_clear", height, tooltip: Loc.T(L.Grind.ClearSearch)))
            {
                searchQuery = string.Empty;
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawSearchSummary()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var rowHeight = SummaryRowHeight * scale;
        using (Fonts.PushCaption())
        {
            var summary = matches.Length == 0
                ? Loc.T(L.Grind.NoMatches, searchQuery)
                : Loc.Plural(L.Grind.Matches, matches.Length, searchQuery);
            var summarySize = TextDraw.Measure(summary);
            TextDraw.At(summary, new Vector2(origin.X + 2f * scale, origin.Y + (rowHeight - summarySize.Y) * 0.5f), Styling.TextDim);
        }

        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, rowHeight));
    }

    private static void DrawSummaryRow(Configuration cfg, AutoFateController ctrl, ZoneInfo[] zones)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var rowHeight = SummaryRowHeight * scale;
        var selected = CountSelected(cfg, zones);

        using (Fonts.PushCaption())
        {
            var summary = Loc.T(L.Grind.SelectedSummary, selected, zones.Length);
            var summarySize = TextDraw.Measure(summary);
            TextDraw.At(summary, new Vector2(origin.X + 2f * scale, origin.Y + (rowHeight - summarySize.Y) * 0.5f), Styling.TextDim);
        }

        var allSelected = AllUnlockedSelected(cfg, zones);
        var label = allSelected ? Loc.T(L.Common.Clear) : Loc.T(L.Common.SelectAll);
        var width = PillButton.Width(label);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + avail - width, origin.Y));
        if (PillButton.Draw("##afg_zone_bulk", label, allSelected ? Styling.AccentRose : Styling.AccentViolet,
                PillButton.Emphasis.Ghost, enabled: !ctrl.Running && zones.Length > 0, height: SummaryRowHeight))
        {
            SetAll(cfg, zones, !allSelected);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(avail, rowHeight));
    }

    private static void DrawGrid(Configuration cfg, AutoFateController ctrl, ZoneInfo[] zones, bool showExpansion)
    {
        if (zones.Length == 0) return;

        var scale = ImGuiHelpers.GlobalScale;
        var gap = Gap * scale;
        var avail = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)MathF.Floor((avail + gap) / (Layout.ZoneCardMinWidth * scale + gap)));
        var cardWidth = (avail - gap * (columns - 1)) / columns;

        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));
        for (var index = 0; index < zones.Length; index++)
        {
            if (index % columns != 0) ImGui.SameLine(0f, gap);
            var zone = zones[index];
            ZoneStateReader.Refresh(zone);
            DrawCard(zone, cfg, ctrl, cardWidth, QueuePosition(cfg, zone.TerritoryId), showExpansion);
        }
    }

    private static void DrawCard(ZoneInfo zone, Configuration cfg, AutoFateController ctrl, float width, int queuePosition, bool showExpansion)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(width, Layout.ZoneCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var selected = queuePosition > 0;
        var locked = !zone.Unlocked;
        var running = ctrl.Running;
        var interactive = !locked && !running;

        ImGui.PushID((nint)zone.TerritoryId);
        var hit = Hit.Area("##zone", size, interactive);
        var hover = Motion.Hover(Motion.Key("##zone"), hit.Hovered);
        var active = Motion.Approach(Motion.Key("##zone", 1), selected ? 1f : 0f, 14f);
        ImGui.PopID();

        if (hit.Clicked) SetSelected(cfg, zone.TerritoryId, !selected);

        var dl = ImGui.GetWindowDrawList();
        Paint.Glass(dl, origin, end, Styling.CardRounding * scale, Styling.AccentViolet, 0.02f + 0.16f * active, hover);

        var midY = origin.Y + size.Y * 0.5f;
        var discRadius = 9f * scale;
        var discCenter = new Vector2(origin.X + 14f * scale + discRadius, midY);
        DrawSelector(dl, discCenter, discRadius, locked, active);

        var rightX = end.X - 12f * scale;
        if (queuePosition > 0) rightX -= DrawQueueBadge(dl, Label(queueLabels, queuePosition), rightX, midY) + 8f * scale;
        if (zone.ActiveFateCount > 0) rightX -= DrawActiveFates(dl, zone.ActiveFateCount, rightX, midY) + 8f * scale;
        if (showExpansion) rightX -= DrawExpansionTag(dl, ExpansionLabels.Name(zone.Expansion), rightX, midY) + 8f * scale;

        var textX = discCenter.X + discRadius + 12f * scale;
        var nameColor = locked ? Styling.TextMuted : Vector4.Lerp(Styling.TextSecondary, Styling.TextStrong, MathF.Max(active, hover));
        var name = TextDraw.Truncate(zone.Name, rightX - textX);
        var nameSize = TextDraw.Measure(name);
        TextDraw.At(name, new Vector2(textX, midY - nameSize.Y * 0.5f), nameColor);

        if (hit.Hovered && zone.ActiveFateCount > 0)
        {
            Tooltip.Show(Loc.Plural(L.Grind.ActiveFates, zone.ActiveFateCount));
        }
        else if (!interactive && Hit.HoveringRect(origin, end))
        {
            Tooltip.Show(running ? Loc.T(L.Grind.ZonesLockedRunning) : LockedTooltip(zone));
        }
    }

    private static void DrawSelector(ImDrawListPtr dl, Vector2 center, float radius, bool locked, float active)
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (locked)
        {
            TextDraw.IconCentered(FontAwesomeIcon.Lock, center, Styling.TextMuted);
            return;
        }

        var ring = Vector4.Lerp(Styling.WithAlpha(Styling.BorderDim, 0.9f), Styling.AccentVioletSoft, active);
        dl.AddCircle(center, radius, Paint.Col(ring), 0, 1.4f * scale);
        if (active <= 0.01f) return;

        dl.AddCircleFilled(center, radius * active, Paint.Col(Styling.AccentViolet));
        if (active > 0.5f)
        {
            Paint.Check(dl, center, radius * 1.1f, Styling.WithAlpha(Styling.TextStrong, (active - 0.5f) * 2f), 1.8f * scale);
        }
    }

    private static float DrawQueueBadge(ImDrawListPtr dl, string label, float rightX, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        using (Fonts.PushCaption())
        {
            var badgeSize = TextDraw.Measure(label) + new Vector2(12f * scale, 4f * scale);
            var badgeMin = new Vector2(rightX - badgeSize.X, midY - badgeSize.Y * 0.5f);
            var badgeMax = badgeMin + badgeSize;
            Paint.Pill(dl, badgeMin, badgeMax, Styling.WithAlpha(Styling.AccentViolet, 0.35f), Styling.WithAlpha(Styling.AccentVioletSoft, 0.5f));
            TextDraw.Middle(label, badgeMin, badgeMax, Styling.TextStrong);
            return badgeSize.X;
        }
    }

    private static float DrawExpansionTag(ImDrawListPtr dl, string label, float rightX, float midY)
    {
        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.Measure(label);
            TextDraw.At(label, new Vector2(rightX - labelSize.X, midY - labelSize.Y * 0.5f), Styling.TextMuted);
            return labelSize.X;
        }
    }

    private static float DrawActiveFates(ImDrawListPtr dl, int count, float rightX, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var label = Label(countLabels, count);
        var labelSize = TextDraw.Measure(label);
        TextDraw.At(label, new Vector2(rightX - labelSize.X, midY - labelSize.Y * 0.5f), Styling.AccentAmber);
        var boltSize = TextDraw.IconSize(FontAwesomeIcon.Bolt);
        var boltX = rightX - labelSize.X - 4f * scale - boltSize.X;
        TextDraw.Icon(FontAwesomeIcon.Bolt, new Vector2(boltX, midY - boltSize.Y * 0.5f), Styling.AccentAmber);
        return rightX - boltX;
    }

    private static string LockedTooltip(ZoneInfo zone)
        => ZoneAetherytes.TryFindGateway(zone.TerritoryId, out var gateway)
            ? Loc.T(L.Grind.LockedGateway, gateway.Name, zone.Name)
            : Loc.T(L.Grind.LockedAetheryte);

    private static int CountSelected(Configuration cfg, ZoneInfo[] zones)
    {
        var count = 0;
        for (var index = 0; index < zones.Length; index++)
        {
            if (cfg.SelectedZones.Contains(zones[index].TerritoryId)) count++;
        }

        return count;
    }

    private static bool AllUnlockedSelected(Configuration cfg, ZoneInfo[] zones)
    {
        var unlocked = 0;
        for (var index = 0; index < zones.Length; index++)
        {
            if (!zones[index].Unlocked) continue;
            unlocked++;
            if (!cfg.SelectedZones.Contains(zones[index].TerritoryId)) return false;
        }

        return unlocked > 0;
    }

    private static int QueuePosition(Configuration cfg, uint territoryId)
    {
        var position = 0;
        var selected = cfg.SelectedZones;
        for (var index = 0; index < selected.Count; index++)
        {
            if (!knownIds.Contains(selected[index])) continue;
            position++;
            if (selected[index] == territoryId) return position;
        }

        return 0;
    }

    private static void SetSelected(Configuration cfg, uint territoryId, bool selected)
    {
        if (selected && !cfg.SelectedZones.Contains(territoryId)) cfg.SelectedZones.Add(territoryId);
        else if (!selected) cfg.SelectedZones.Remove(territoryId);
        cfg.SaveDebounced();
    }

    private static void SetAll(Configuration cfg, ZoneInfo[] zones, bool selected)
    {
        for (var index = 0; index < zones.Length; index++)
        {
            var id = zones[index].TerritoryId;
            if (selected && zones[index].Unlocked && !cfg.SelectedZones.Contains(id)) cfg.SelectedZones.Add(id);
            else if (!selected) cfg.SelectedZones.Remove(id);
        }

        cfg.SaveDebounced();
    }

    private static string Label(string[] cache, int value)
        => value >= 0 && value < cache.Length ? cache[value] : $"{(ReferenceEquals(cache, queueLabels) ? "#" : "")}{value}";

    private static string[] BuildLabels(string prefix)
    {
        var labels = new string[CachedLabelCount];
        for (var index = 0; index < labels.Length; index++) labels[index] = prefix + index;
        return labels;
    }
}
