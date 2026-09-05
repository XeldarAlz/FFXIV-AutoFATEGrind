namespace AutoFateGrind.Core.Zones;

public sealed class CityInfo
{
    public required uint TerritoryId { get; init; }
    public required string Name { get; init; }
    public required ExpansionKind Expansion { get; init; }
}

public static class CityCatalog
{
    public const uint SolutionNineTerritoryId = 1186;
    // Shipped as Solution Nine through 1.5.0.0; it is The For'ard Cabins (the Tuliyollal inn), which owns
    // no aetheryte, so every break routed there faulted. Saved city lists still carry it.
    public const uint LegacySolutionNineTerritoryId = 1205;

    // Curated to cities with clean navmesh + open wander areas. Other hubs were tried and dropped
    // (Ul'dah walls, cramped HW/SB/ShB/EW interiors). Don't re-add without verifying navmesh quality.
    public static readonly CityInfo[] All =
    [
        new() { TerritoryId = 129,  Name = "Limsa Lominsa Lower Decks", Expansion = ExpansionKind.ARR },
        new() { TerritoryId = 132,  Name = "New Gridania",              Expansion = ExpansionKind.ARR },
        new() { TerritoryId = 1185, Name = "Tuliyollal",                Expansion = ExpansionKind.DT },
        new() { TerritoryId = SolutionNineTerritoryId, Name = "Solution Nine", Expansion = ExpansionKind.DT },
    ];

    public static CityInfo? Find(uint territoryId)
    {
        foreach (var c in All)
            if (c.TerritoryId == territoryId) return c;
        return null;
    }

    public static bool MigrateSelection(HashSet<uint> selectedCities)
    {
        if (!selectedCities.Remove(LegacySolutionNineTerritoryId)) return false;
        selectedCities.Add(SolutionNineTerritoryId);
        return true;
    }
}
