namespace AutoFateGrind.Core.Zones;

public sealed class CityInfo
{
    public required uint TerritoryId { get; init; }
    public required string Name { get; init; }
    public required ExpansionKind Expansion { get; init; }
}

public static class CityCatalog
{
    // Curated to cities with clean navmesh + open wander areas. Other hubs were tried and dropped
    // (Ul'dah walls, cramped HW/SB/ShB/EW interiors). Don't re-add without verifying navmesh quality.
    public static readonly CityInfo[] All =
    [
        new() { TerritoryId = 129,  Name = "Limsa Lominsa Lower Decks", Expansion = ExpansionKind.ARR },
        new() { TerritoryId = 132,  Name = "New Gridania",              Expansion = ExpansionKind.ARR },
        new() { TerritoryId = 1185, Name = "Tuliyollal",                Expansion = ExpansionKind.DT },
        new() { TerritoryId = 1205, Name = "Solution Nine",             Expansion = ExpansionKind.DT },
    ];

    public static CityInfo? Find(uint territoryId)
    {
        foreach (var c in All)
            if (c.TerritoryId == territoryId) return c;
        return null;
    }
}
