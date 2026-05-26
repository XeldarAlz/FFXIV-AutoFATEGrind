using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Trading;

public sealed record GemstoneTradeItem(
    uint ItemId,
    string ItemName,
    uint CostPerOne);

public static class GemstoneCatalog
{
    public const uint BicolorGemstoneItemId = 26807;

    private static GemstoneTradeItem[]? cached;

    public static GemstoneTradeItem[] All => cached ??= LoadFromLumina();

    public static void Invalidate() => cached = null;

    public static GemstoneTradeItem? FindById(uint itemId)
        => Array.Find(All, i => i.ItemId == itemId);

    // Side-effecting: writes back the cheapest catalog entry when the saved id is missing
    // or stale, so a stored 0 (fresh install) can't silently no-op the trade-on-cap gate.
    public static uint EnsurePersistedTarget()
    {
        var cfg = Plugin.Cfg;
        if (cfg.TargetTradeItemId != 0 && FindById(cfg.TargetTradeItemId) is not null)
            return cfg.TargetTradeItemId;
        if (All.Length == 0) return 0;
        cfg.TargetTradeItemId = All[0].ItemId;
        cfg.Save();
        return cfg.TargetTradeItemId;
    }

    public static int ComputeBuyQuantity(int wallet, uint costPerOne)
    {
        var cost = (int)costPerOne;
        if (cost <= 0) return 0;

        var cfg = Plugin.Cfg;
        var spendable = Math.Max(0, wallet - cfg.KeepGemstonesReserve);
        var affordable = spendable / cost;

        return cfg.SpendMode switch
        {
            GemstoneSpendMode.SpendAll    => affordable,
            GemstoneSpendMode.SpendGems   => Math.Min(affordable, cfg.SpendGemsAmount / cost),
            GemstoneSpendMode.BuyQuantity => Math.Min(affordable, cfg.BuyQuantityAmount),
            _ => affordable,
        };
    }

    // Walks the SpecialShop sheet and collects every distinct item that can be bought
    // with Bicolor Gemstones. The trader/slot for purchase is resolved at runtime by
    // ShopInteraction (per-trader sub-menu and per-shop slot index).
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
                        CostPerOne: bicolorCost));
                }
            }
        }

        return [.. result.OrderBy(i => i.CostPerOne).ThenBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase)];
    }
}
