namespace AutoFateGrind.Core.Zones;

internal static class ZoneSelection
{
    public static IReadOnlyList<ZoneInfo> ResolveStartList(Configuration cfg)
        => Resolve(cfg.SelectedZones);

    // Maps territory ids to their ZoneInfo in listed order, dropping any id not in the FATE-zone registry.
    public static IReadOnlyList<ZoneInfo> Resolve(IEnumerable<uint> territoryIds)
    {
        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        return territoryIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }
}
