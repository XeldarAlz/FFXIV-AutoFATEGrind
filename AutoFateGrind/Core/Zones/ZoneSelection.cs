namespace AutoFateGrind.Core.Zones;

internal static class ZoneSelection
{
    public static IReadOnlyList<ZoneInfo> ResolveStartList(Configuration cfg)
    {
        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        return cfg.SelectedZones.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }
}
