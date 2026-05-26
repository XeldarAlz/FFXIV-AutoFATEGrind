using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Trading;

public sealed record GemstoneTradeItem(
    uint ItemId,
    string ItemName,
    uint CostPerOne,
    uint TraderEnpcBaseId,
    uint TraderTerritoryId,
    string TraderName);

public static class GemstoneCatalog
{
    public const uint BicolorGemstoneItemId = 26807;

    private static GemstoneTradeItem[]? cached;

    public static GemstoneTradeItem[] All => cached ??= LoadFromLumina();

    public static void Invalidate() => cached = null;

    public static GemstoneTradeItem? FindById(uint itemId)
        => All.FirstOrDefault(i => i.ItemId == itemId);

    private static GemstoneTradeItem[] LoadFromLumina()
    {
        var shops = Svc.Data.GetExcelSheet<SpecialShop>();
        var items = Svc.Data.GetExcelSheet<Item>();
        if (shops is null || items is null) return [];

        var seen = new HashSet<uint>();
        var result = new List<GemstoneTradeItem>(capacity: 128);

        foreach (var shop in shops)
        {
            foreach (var entry in shop.Item)
            {
                var costs = entry.ItemCosts;
                var receives = entry.ReceiveItems;
                if (costs.Count == 0 || receives.Count == 0) continue;

                uint bicolorCost = 0;
                foreach (var c in costs)
                {
                    if (c.ItemCost.RowId == BicolorGemstoneItemId)
                    {
                        bicolorCost = c.CurrencyCost;
                        break;
                    }
                }
                if (bicolorCost == 0) continue;

                foreach (var r in receives)
                {
                    var item = r.Item;
                    if (item.RowId == 0) continue;
                    if (!seen.Add(item.RowId)) continue;

                    var name = items.GetRowOrDefault(item.RowId)?.Name.ExtractText() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    result.Add(new GemstoneTradeItem(
                        ItemId: item.RowId,
                        ItemName: name,
                        CostPerOne: bicolorCost,
                        TraderEnpcBaseId: 0,
                        TraderTerritoryId: 0,
                        TraderName: ""));
                }
            }
        }

        return [.. result.OrderBy(i => i.CostPerOne).ThenBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase)];
    }
}
