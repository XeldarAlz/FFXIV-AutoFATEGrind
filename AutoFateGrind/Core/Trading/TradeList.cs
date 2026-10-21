using AutoFateGrind.Core.Zones;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Trading;

public readonly record struct TradeOrder(GemstoneTradeItem Item, int StopAtCount);

public sealed record TradePlan(TraderLocation Trader, TradeOrder[] Orders)
{
    public string DescribeItems()
    {
        var names = new string[Orders.Length];
        for (var orderIndex = 0; orderIndex < Orders.Length; orderIndex++)
        {
            names[orderIndex] = Orders[orderIndex].Item.ItemName;
        }

        return string.Join(", ", names);
    }
}

public enum TradeEntryState : byte
{
    Needed,
    Unknown,
    Learned,
    Held,
    Stocked,
    NoTrader,
}

// Ownership is read from the game instead of being stored, because one config is shared by every character.
public static class TradeList
{
    public static unsafe int HeldCount(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        return inventory is null ? 0 : inventory->GetInventoryItemCount(itemId);
    }

    public static bool IsLearned(uint itemId)
        => Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } row && Svc.UnlockState.IsItemUnlocked(row);

    public static int RemainingNeed(GemstoneTradeItem item, int stopAtCount)
    {
        var held = HeldCount(item.ItemId);
        if (item.IsCollectible)
        {
            return held > 0 || IsLearned(item.ItemId) ? 0 : 1;
        }

        return stopAtCount <= 0 ? int.MaxValue : Math.Max(0, stopAtCount - held);
    }

    // The first entry that is still needed, affordable and reachable picks the trader. Every entry below it that the
    // same trader sells is bought on the same trip, as far as the budget stretches once the shop is open.
    public static TradePlan? Plan(int wallet, uint? preferTerritoryId, ExpansionKind? preferExpansion, IReadOnlySet<uint> skippedItemIds, int spentThisTrade = 0)
    {
        var entries = Plugin.Cfg.TradeList;
        for (var leadIndex = 0; leadIndex < entries.Count; leadIndex++)
        {
            if (!TryNeeded(entries[leadIndex], skippedItemIds, out var lead, out var need))
            {
                continue;
            }

            if (GemstoneCatalog.ComputeBuyQuantity(wallet, lead.Item.CostPerOne, need, spentThisTrade) <= 0)
            {
                continue;
            }

            var trader = GemstoneTrader.PickForItem(lead.Item.ItemId, preferTerritoryId, preferExpansion, out _);
            if (trader is null)
            {
                continue;
            }

            var orders = new List<TradeOrder> { lead };
            for (var entryIndex = leadIndex + 1; entryIndex < entries.Count; entryIndex++)
            {
                if (TryNeeded(entries[entryIndex], skippedItemIds, out var order, out _) && GemstoneTrader.Sells(trader, order.Item))
                {
                    orders.Add(order);
                }
            }

            return new TradePlan(trader, [.. orders]);
        }

        return null;
    }

    public static TradeEntryState StateOf(TradeListEntry entry, out GemstoneTradeItem? item, out int held)
    {
        item = GemstoneCatalog.FindById(entry.ItemId);
        held = 0;
        if (item is null)
        {
            return TradeEntryState.Unknown;
        }

        held = HeldCount(item.ItemId);
        if (item.IsCollectible && IsLearned(item.ItemId))
        {
            return TradeEntryState.Learned;
        }

        if (item.IsCollectible && held > 0)
        {
            return TradeEntryState.Held;
        }

        if (!item.IsCollectible && entry.StopAtCount > 0 && held >= entry.StopAtCount)
        {
            return TradeEntryState.Stocked;
        }

        return GemstoneTrader.PickForItem(item.ItemId, null, null, out _) is null ? TradeEntryState.NoTrader : TradeEntryState.Needed;
    }

    // Only called once Plan found nothing, so a needed and affordable entry can only be missing a reachable trader.
    public static string DescribeBlocked(int wallet, IReadOnlySet<uint> skippedItemIds)
    {
        var entries = Plugin.Cfg.TradeList;
        if (entries.Count == 0)
        {
            return "the shopping list is empty";
        }

        var reasons = new string[entries.Count];
        for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            reasons[entryIndex] = DescribeEntry(entries[entryIndex], wallet, skippedItemIds);
        }

        return string.Join("; ", reasons);
    }

    private static string DescribeEntry(TradeListEntry entry, int wallet, IReadOnlySet<uint> skippedItemIds)
    {
        var item = GemstoneCatalog.FindById(entry.ItemId);
        if (item is null)
        {
            return $"item {entry.ItemId} is not in the gem catalog";
        }

        if (skippedItemIds.Contains(item.ItemId))
        {
            return $"{item.ItemName} could not be bought earlier this run";
        }

        var need = RemainingNeed(item, entry.StopAtCount);
        if (need <= 0)
        {
            return item.IsCollectible
                ? $"{item.ItemName} is already owned or learned"
                : $"{item.ItemName} is stocked ({HeldCount(item.ItemId)}/{entry.StopAtCount})";
        }

        if (GemstoneCatalog.ComputeBuyQuantity(wallet, item.CostPerOne, need, 0) <= 0)
        {
            return $"{item.ItemName} ({item.CostPerOne}g each) is out of budget at {wallet}g with the current spend settings";
        }

        return $"no trader you can reach sells {item.ItemName}";
    }

    private static bool TryNeeded(TradeListEntry entry, IReadOnlySet<uint> skippedItemIds, out TradeOrder order, out int need)
    {
        order = default;
        need = 0;
        if (skippedItemIds.Contains(entry.ItemId))
        {
            return false;
        }

        var item = GemstoneCatalog.FindById(entry.ItemId);
        if (item is null)
        {
            return false;
        }

        order = new TradeOrder(item, entry.StopAtCount);
        need = RemainingNeed(item, entry.StopAtCount);
        return need > 0;
    }
}
