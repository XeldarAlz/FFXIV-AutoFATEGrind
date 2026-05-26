using AutoFateGrind.Core.Zones;
using System.Numerics;

namespace AutoFateGrind.Core.Trading;

public sealed record TraderLocation(
    string Name,
    uint EnpcBaseId,
    uint TerritoryId,
    Vector3 Position,
    ExpansionKind Expansion,
    bool IsHub);

// All 24 Bicolor Gemstone Traders, sourced from Lumina (ENpcResident.Title == "Gemstone Trader",
// position resolved via Level sheet). One trader per Shared FATE zone plus two hubs per
// expansion. `IsHub` distinguishes the city hub from the in-zone trader.
public static class GemstoneTrader
{
    public static readonly TraderLocation[] Traders =
    [
        // Shadowbringers — 6 zone traders + 2 hubs
        new("Siulmet",         1027385, 813,  new Vector3( 701.898f,  21.7958f,  -45.9079f), ExpansionKind.ShB, IsHub: false),
        new("Zumutt",          1027497, 814,  new Vector3(-482.859f, 417.190f,  -629.042f), ExpansionKind.ShB, IsHub: false),
        new("Halden",          1027892, 815,  new Vector3(-541.649f,  45.4565f, -217.395f), ExpansionKind.ShB, IsHub: false),
        new("Sul Lad",         1027665, 816,  new Vector3(-253.926f,  40.324f,   466.046f), ExpansionKind.ShB, IsHub: false),
        new("Nacille",         1027709, 817,  new Vector3( 323.079f,  33.7629f, -162.036f), ExpansionKind.ShB, IsHub: false),
        new("Goushs Ooan",     1027766, 818,  new Vector3( 586.613f, 348.980f,  -173.632f), ExpansionKind.ShB, IsHub: false),
        new("Gramsol",         1027998, 819,  new Vector3(  -6.7598f, -7.700f,   118.517f), ExpansionKind.ShB, IsHub: true),
        new("Pedronille",      1027538, 820,  new Vector3( -32.8222f, 84.184f,    49.3019f), ExpansionKind.ShB, IsHub: true),

        // Endwalker — 6 zone traders + 2 hubs
        new("Faezbroes",       1037484, 956,  new Vector3( 423.562f, 166.283f,  -423.983f), ExpansionKind.EW,  IsHub: false),
        new("Mahveydah",       1037635, 957,  new Vector3( 218.646f,   4.7637f,  658.839f), ExpansionKind.EW,  IsHub: false),
        new("Zawawa",          1037724, 958,  new Vector3(-426.858f,  22.3626f,  430.132f), ExpansionKind.EW,  IsHub: false),
        new("Tradingway",      1037793, 959,  new Vector3(  19.6384f,-132.952f, -460.746f), ExpansionKind.EW,  IsHub: false),
        new("Aisara",          1037909, 961,  new Vector3( 146.861f,  10.3859f,  100.747f), ExpansionKind.EW,  IsHub: false),
        new("N-1499",          1038004, 960,  new Vector3( 468.980f, 437.002f,   330.620f), ExpansionKind.EW,  IsHub: false),
        new("Gadfrid",         1037055, 962,  new Vector3(  78.3673f,  5.1423f,  -36.7838f), ExpansionKind.EW,  IsHub: true),
        new("Sajareen",        1037304, 963,  new Vector3(  -4.7563f,  0.9002f,  -52.8448f), ExpansionKind.EW,  IsHub: true),

        // Dawntrail — 6 zone traders + 2 hubs
        new("Tepli",           1048628, 1187, new Vector3( 301.503f, -172.282f, -484.550f), ExpansionKind.DT,  IsHub: false),
        new("Kunuhali",        1048778, 1188, new Vector3(-200.336f,   6.3003f, -522.772f), ExpansionKind.DT,  IsHub: false),
        new("Rral Wuruq",      1048933, 1189, new Vector3(-381.561f,  23.5356f, -436.932f), ExpansionKind.DT,  IsHub: false),
        new("Mitepe",          1049283, 1190, new Vector3( 357.748f,  -1.4908f,  469.419f), ExpansionKind.DT,  IsHub: false),
        new("Toashana",        1049438, 1191, new Vector3(-258.373f,  30.000f,  -594.195f), ExpansionKind.DT,  IsHub: false),
        new("Clerk PX-0029",   1049528, 1192, new Vector3(  30.3197f, 53.200f,   802.792f), ExpansionKind.DT,  IsHub: false),
        new("Kajeel Ja",       1048383, 1185, new Vector3( -23.9029f,-10.000f,   105.344f), ExpansionKind.DT,  IsHub: true),
        new("Beryl",           1049082, 1186, new Vector3(-198.779f,   0.9002f,   -4.3314f), ExpansionKind.DT,  IsHub: true),
    ];

    public static TraderLocation? PickForTerritory(uint territoryId) =>
        Array.Find(Traders, t => t.TerritoryId == territoryId);

    public static TraderLocation? PickHub(ExpansionKind expansion) =>
        Array.Find(Traders, t => t.Expansion == expansion && t.IsHub);

    // Prefer the trader in the player's current territory; fall back to the expansion's hub
    // (e.g. when finishing a zone whose trader isn't usable, or for non-Shared-FATE zones).
    public static TraderLocation? PickFor(uint currentTerritoryId, ExpansionKind expansion) =>
        PickForTerritory(currentTerritoryId) ?? PickHub(expansion);
}
