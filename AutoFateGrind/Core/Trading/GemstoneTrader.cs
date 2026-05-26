using AutoFateGrind.Core.Zones;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoFateGrind.Core.Trading;

public sealed record TraderLocation(
    string Name,
    uint EnpcBaseId,
    uint TerritoryId,
    Vector3 Position,
    ExpansionKind Expansion,
    bool IsHub);

public static class GemstoneTrader
{
    public static readonly TraderLocation[] Traders =
    [
        // Shadowbringers
        new("Siulmet",         1027385, 813,  new Vector3( 701.898f,  21.7958f,  -45.9079f), ExpansionKind.ShB, IsHub: false),
        new("Zumutt",          1027497, 814,  new Vector3(-482.859f, 417.190f,  -629.042f), ExpansionKind.ShB, IsHub: false),
        new("Halden",          1027892, 815,  new Vector3(-541.649f,  45.4565f, -217.395f), ExpansionKind.ShB, IsHub: false),
        new("Sul Lad",         1027665, 816,  new Vector3(-253.926f,  40.324f,   466.046f), ExpansionKind.ShB, IsHub: false),
        new("Nacille",         1027709, 817,  new Vector3( 323.079f,  33.7629f, -162.036f), ExpansionKind.ShB, IsHub: false),
        new("Goushs Ooan",     1027766, 818,  new Vector3( 586.613f, 348.980f,  -173.632f), ExpansionKind.ShB, IsHub: false),
        new("Gramsol",         1027998, 819,  new Vector3(  -6.7598f, -7.700f,   118.517f), ExpansionKind.ShB, IsHub: true),
        new("Pedronille",      1027538, 820,  new Vector3( -32.8222f, 84.184f,    49.3019f), ExpansionKind.ShB, IsHub: true),

        // Endwalker
        new("Faezbroes",       1037484, 956,  new Vector3( 423.562f, 166.283f,  -423.983f), ExpansionKind.EW,  IsHub: false),
        new("Mahveydah",       1037635, 957,  new Vector3( 218.646f,   4.7637f,  658.839f), ExpansionKind.EW,  IsHub: false),
        new("Zawawa",          1037724, 958,  new Vector3(-426.858f,  22.3626f,  430.132f), ExpansionKind.EW,  IsHub: false),
        new("Tradingway",      1037793, 959,  new Vector3(  19.6384f,-132.952f, -460.746f), ExpansionKind.EW,  IsHub: false),
        new("Aisara",          1037909, 961,  new Vector3( 146.861f,  10.3859f,  100.747f), ExpansionKind.EW,  IsHub: false),
        new("N-1499",          1038004, 960,  new Vector3( 468.980f, 437.002f,   330.620f), ExpansionKind.EW,  IsHub: false),
        new("Gadfrid",         1037055, 962,  new Vector3(  78.3673f,  5.1423f,  -36.7838f), ExpansionKind.EW,  IsHub: true),
        new("Sajareen",        1037304, 963,  new Vector3(  -4.7563f,  0.9002f,  -52.8448f), ExpansionKind.EW,  IsHub: true),

        // Dawntrail
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

    private static Dictionary<uint, TraderLocation[]>? shopToTraders;

    public static void Invalidate() => shopToTraders = null;

    private static Dictionary<uint, TraderLocation[]> ShopToTraders =>
        shopToTraders ??= BuildShopToTraders();

    // ShB traders whose FateShop row is empty in Lumina — manual override matches clib/OmenTools.
    private static readonly Dictionary<uint, uint> ShbFateShopFallback = new()
    {
        { 1027998, 1769957 }, // Gramsol (Crystarium)
        { 1027538, 1769958 }, // Pedronille (Eulmore)
        { 1027385, 1769959 }, // Siulmet (Lakeland)
        { 1027497, 1769960 }, // Zumutt (Kholusia)
        { 1027892, 1769961 }, // Halden (Amh Araeng)
        { 1027665, 1769962 }, // Sul Lad (Il Mheg)
        { 1027709, 1769963 }, // Nacille (Rak'tika)
        { 1027766, 1769964 }, // Goushs Ooan (Tempest)
    };

    // Bicolor traders expose their stock through FateShop, which is keyed directly by ENpcBase.RowId
    // (not via ENpcData / TopicSelect / PreHandler). Pattern matches clib's ItemCostLookup.
    private static Dictionary<uint, TraderLocation[]> BuildShopToTraders()
    {
        var fateShops = Svc.Data.GetExcelSheet<FateShop>();
        if (fateShops is null) return [];

        var accum = new Dictionary<uint, List<TraderLocation>>();

        void Attach(uint shopId, TraderLocation trader)
        {
            if (shopId == 0) return;
            if (!accum.TryGetValue(shopId, out var list))
            {
                list = new List<TraderLocation>();
                accum[shopId] = list;
            }
            if (!list.Contains(trader)) list.Add(trader);
        }

        var perTraderCount = new Dictionary<TraderLocation, int>();

        foreach (var trader in Traders)
        {
            var count = 0;
            if (fateShops.GetRowOrDefault(trader.EnpcBaseId) is { } fateShop)
            {
                foreach (var shopRef in fateShop.SpecialShop)
                {
                    if (shopRef.RowId == 0) continue;
                    Attach(shopRef.RowId, trader);
                    count++;
                }
            }

            if (count == 0 && ShbFateShopFallback.TryGetValue(trader.EnpcBaseId, out var fallbackShopId))
            {
                Attach(fallbackShopId, trader);
                count = 1;
            }

            perTraderCount[trader] = count;
        }

        var totalShops = accum.Count;
        var totalMapped = perTraderCount.Count(kv => kv.Value > 0);
        Svc.Log.Info($"[AFG] Bicolor catalog: {totalMapped}/{Traders.Length} traders mapped, {totalShops} unique shops.");
        foreach (var kv in perTraderCount.Where(kv => kv.Value == 0))
            Svc.Log.Warning($"[AFG] Bicolor trader {kv.Key.Name} (ENpcBase {kv.Key.EnpcBaseId}) mapped to 0 shops.");

        return accum.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    // Preference: in-zone → same-expansion hub → same-expansion → any hub → any seller.
    public static TraderLocation? PickForItem(uint itemId, uint? preferTerritoryId, ExpansionKind? preferExpansion)
    {
        var item = GemstoneCatalog.FindById(itemId);
        if (item is null) return null;

        var sellers = new List<TraderLocation>();
        foreach (var shopId in item.ShopRowIds)
        {
            if (!ShopToTraders.TryGetValue(shopId, out var owners)) continue;
            foreach (var t in owners)
                if (!sellers.Contains(t)) sellers.Add(t);
        }
        if (sellers.Count == 0) return null;

        if (preferTerritoryId is { } tid)
        {
            var inZone = sellers.FirstOrDefault(t => t.TerritoryId == tid);
            if (inZone is not null) return inZone;
        }
        if (preferExpansion is { } exp)
        {
            var hub = sellers.FirstOrDefault(t => t.Expansion == exp && t.IsHub);
            if (hub is not null) return hub;
            var inExp = sellers.FirstOrDefault(t => t.Expansion == exp);
            if (inExp is not null) return inExp;
        }
        var anyHub = sellers.FirstOrDefault(t => t.IsHub);
        return anyHub ?? sellers[0];
    }
}
