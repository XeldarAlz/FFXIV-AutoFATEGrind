using AutoFateGrind.Core.Zones;
using System.Numerics;

namespace AutoFateGrind.Core.Trading;

public sealed record TraderLocation(
    string Name,
    uint TerritoryId,
    Vector3 Position,
    ExpansionKind Expansion);

public static class GemstoneTrader
{
    // Territory IDs and positions are hub approximations and may need tuning per trader.
    public static readonly TraderLocation[] Traders =
    [
        new("Mowen's Merchants",       819,  new Vector3(-7.6f,  20.0f, -47.3f), ExpansionKind.ShB),
        new("Concierge of Aesthetics", 963,  new Vector3(-87.0f, -1.0f,  10.5f), ExpansionKind.EW),
        new("Tuliyollal Gem Trader",   1185, new Vector3(43.0f,   3.0f, -50.0f), ExpansionKind.DT),
    ];

    public static TraderLocation PickFor(ExpansionKind exp) =>
        Traders.FirstOrDefault(t => t.Expansion == exp) ?? Traders[^1];
}
