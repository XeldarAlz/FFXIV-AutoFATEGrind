using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Trading;

// Collectibles (unique, or learned on use like minions and orchestrion rolls) are only ever worth one copy.
public sealed record GemstoneTradeItem(
    uint ItemId,
    string ItemName,
    uint CostPerOne,
    uint[] ShopRowIds,
    bool IsCollectible,
    uint StackSize);

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

    // spentThisTrade counts earlier purchases on the same trip, so the spend-up-to cap covers the whole list.
    public static int ComputeBuyQuantity(int wallet, uint costPerOne, int need, int spentThisTrade)
    {
        var cost = (int)costPerOne;
        if (cost <= 0 || need <= 0) return 0;

        var cfg = Plugin.Cfg;
        var spendable = Math.Max(0, wallet - cfg.KeepGemstonesReserve);
        if (cfg.SpendMode == GemstoneSpendMode.SpendGems)
            spendable = Math.Min(spendable, Math.Max(0, cfg.SpendGemsAmount - spentThisTrade));

        var quantity = Math.Min(spendable / cost, need);
        return cfg.SpendMode == GemstoneSpendMode.BuyQuantity ? Math.Min(quantity, cfg.BuyQuantityAmount) : quantity;
    }

    private static GemstoneTradeItem[] LoadFromLumina()
    {
        var shops = Svc.Data.GetExcelSheet<SpecialShop>();
        var items = Svc.Data.GetExcelSheet<Item>();
        if (shops is null || items is null) return [];

        var byItem = new Dictionary<uint, (uint cost, string name, List<uint> shopIds, bool collectible, uint stackSize)>(capacity: 128);

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
                        if (items.GetRowOrDefault(rowId) is not { } row) continue;
                        var name = row.Name.ExtractText();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        data = (bicolorCost, name, new List<uint>(), row.IsUnique || Svc.UnlockState.IsItemUnlockable(row), Math.Max(1u, row.StackSize));
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
                ShopRowIds: [.. kv.Value.shopIds],
                IsCollectible: kv.Value.collectible,
                StackSize: kv.Value.stackSize))
            .OrderBy(i => i.CostPerOne)
            .ThenBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase)];
    }
}
