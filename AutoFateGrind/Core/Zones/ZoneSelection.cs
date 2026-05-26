namespace AutoFateGrind.Core.Zones;

internal static class ZoneSelection
{
    public static bool IsAutoSelected(Configuration cfg) => cfg.Mode == GrindMode.MaxFates;

    // Shared-FATE achievements only exist for ShB / EW / DT.
    public static IEnumerable<ZoneInfo> SharedFateCandidates() =>
        ZoneRegistry.Zones
            .Where(z => z.Expansion >= ExpansionKind.ShB)
            .Where(z => z.AchievementId != 0);

    // Eligible zones (unlocked, not yet maxed) in the user's preferred order, with any new
    // entries appended in expansion+name order. Does not refresh zone state — callers that
    // need fresh state should Refresh first.
    public static IReadOnlyList<ZoneInfo> EligibleSharedFatesOrdered(Configuration cfg)
    {
        var pool = SharedFateCandidates()
            .Where(z => z.Unlocked && !z.AchievementDone)
            .OrderBy(z => z.Expansion)
            .ThenBy(z => z.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return ApplyUserOrder(pool, cfg.SharedFateOrder);
    }

    // Refresh + return the non-maxed shared-FATE zones the runner should rotate through.
    // Eligible zones come first in the user's order; locked zones tail behind in registry order
    // (the runner can't enter them anyway).
    public static IReadOnlyList<ZoneInfo> AutoQueue(Configuration cfg)
    {
        var candidates = SharedFateCandidates().ToList();
        foreach (var z in candidates) ZoneStateReader.Refresh(z);

        var eligible = EligibleSharedFatesOrdered(cfg);
        var locked = candidates.Where(z => !z.AchievementDone && !z.Unlocked).ToList();
        return [..eligible, ..locked];
    }

    public static IReadOnlyList<ZoneInfo> ResolveStartList(Configuration cfg)
    {
        if (IsAutoSelected(cfg)) return AutoQueue(cfg);

        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        return cfg.SelectedZones.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    private static IReadOnlyList<ZoneInfo> ApplyUserOrder(IReadOnlyList<ZoneInfo> pool, IReadOnlyList<uint> order)
    {
        if (pool.Count == 0) return pool;
        var byId = pool.ToDictionary(z => z.TerritoryId);
        var seen = new HashSet<uint>(pool.Count);
        var result = new List<ZoneInfo>(pool.Count);
        foreach (var id in order)
            if (byId.TryGetValue(id, out var z) && seen.Add(id))
                result.Add(z);
        foreach (var z in pool)
            if (seen.Add(z.TerritoryId))
                result.Add(z);
        return result;
    }
}
