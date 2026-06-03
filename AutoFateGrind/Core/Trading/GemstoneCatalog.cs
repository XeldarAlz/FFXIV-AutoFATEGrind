using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Trading;

public sealed record GemstoneTradeItem(
    uint ItemId,
    string ItemName,
    uint CostPerOne,
    uint[] ShopRowIds);

public static class GemstoneCatalog
{
    public const uint BicolorGemstoneItemId = 26807;

    private static GemstoneTradeItem[]? cached;

    public static GemstoneTradeItem[] All => cached ??= LoadFromLumina();

    public static GemstoneTradeItem? FindById(uint itemId)
        => Array.Find(All, i => i.ItemId == itemId);

    public static unsafe int CurrentWalletCount()
    {
        var im = InventoryManager.Instance();
        return im is null ? 0 : im->GetInventoryItemCount(BicolorGemstoneItemId);
    }

    // Reports readability so delta-trackers don't mistake an unavailable inventory for a drop to zero.
    public static unsafe bool TryCurrentWalletCount(out int count)
    {
        var im = InventoryManager.Instance();
        if (im is null) { count = 0; return false; }
        count = im->GetInventoryItemCount(BicolorGemstoneItemId);
        return true;
    }

    // Picks the cheapest routable item as a default so fresh installs don't no-op trade-on-cap.
    public static uint EnsurePersistedTarget()
    {
        var cfg = Plugin.Cfg;
        if (cfg.TargetTradeItemId != 0 && FindById(cfg.TargetTradeItemId) is not null)
            return cfg.TargetTradeItemId;
        var fallback = Array.Find(All, i => GemstoneTrader.PickForItem(i.ItemId, null, null) is not null);
        if (fallback is null) return 0;
        cfg.TargetTradeItemId = fallback.ItemId;
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

    private static GemstoneTradeItem[] LoadFromLumina()
    {
        var shops = Svc.Data.GetExcelSheet<SpecialShop>();
        var items = Svc.Data.GetExcelSheet<Item>();
        if (shops is null || items is null) return [];

        var byItem = new Dictionary<uint, (uint cost, string name, List<uint> shopIds)>(capacity: 128);

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
                    var rowId = r.Item.RowId;
                    if (rowId == 0) continue;

                    if (!byItem.TryGetValue(rowId, out var data))
                    {
                        var name = items.GetRowOrDefault(rowId)?.Name.ExtractText() ?? "";
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        data = (bicolorCost, name, new List<uint>());
                        byItem[rowId] = data;
                    }
                    if (!data.shopIds.Contains(shop.RowId))
                        data.shopIds.Add(shop.RowId);
                }
            }
        }

        return [.. byItem
            .Select(kv => new GemstoneTradeItem(
                ItemId: kv.Key,
                ItemName: kv.Value.name,
                CostPerOne: kv.Value.cost,
                ShopRowIds: [.. kv.Value.shopIds]))
            .OrderBy(i => i.CostPerOne)
            .ThenBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase)];
    }
}
