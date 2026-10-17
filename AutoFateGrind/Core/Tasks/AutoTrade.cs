using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Trading;
using clib.TaskSystem;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed class AutoTrade(TradePlan plan) : AutoCommon
{
    private readonly TradePlan plan = plan;
    private readonly HashSet<uint> boughtItemIds = [];
    private readonly HashSet<uint> passedOverItemIds = [];
    private int spentGems;
    private bool transportBlocked;

    private const int TeleportWatchdogMs = 60_000;
    private const int MoveWatchdogMs = 120_000;
    private const float WalkToleranceMeters = 4f;
    private const int InteractWaitMs = 15_000;
    private const int ShopOpenWaitMs = 15_000;
    private const int ConfirmWaitMs = 10_000;
    private const int SpendWaitMs = 10_000;
    private const int SubmenuWaitMs = 10_000;
    private const int MenuReturnWaitMs = 2_000;
    private const int ShopCloseSettleMs = 350;
    private const int PurchaseSettleMs = 500;
    private const int MenuRetryMs = 500;

    public bool BoughtAny => boughtItemIds.Count > 0;

    // The character's own state (combat, a stuck teleport lock) stalled every cast toward the trader, which says
    // nothing about the shopping list itself.
    public bool TransportBlocked => transportBlocked;

    // Anything neither bought nor passed over for budget failed, including every order a fault cut short, since clib
    // still runs OnCompleted after an exception.
    public bool Failed(uint itemId) => !boughtItemIds.Contains(itemId) && !passedOverItemIds.Contains(itemId);

    protected override async Task Execute()
    {
        var trader = plan.Trader;
        Diag($"AutoTrade start: trader={trader.Name} terr={trader.TerritoryId} items={plan.DescribeItems()}");
        Svc.Chat.Print($"[AFG] Auto-trade at {trader.Name}: {plan.DescribeItems()}");

        await ReachTrader(trader);

        for (var orderIndex = 0; orderIndex < plan.Orders.Length; orderIndex++)
        {
            await BuyOrder(trader, plan.Orders[orderIndex]);
        }

        Status = "Closing shop";
        ShopInteraction.CloseShop();
        await DelayMs(ShopCloseSettleMs);

        if (BoughtAny)
            Svc.Chat.Print($"[AFG] Trade complete. Gemstones now: {GemstoneCount()}");
    }

    private async Task ReachTrader(TraderLocation trader)
    {
        var traderPos = trader.Position;
        var traderTerr = trader.TerritoryId;
        if (Svc.ClientState.TerritoryType != traderTerr)
        {
            var result = TerritoryTeleportResult.Unreachable;
            await RunWithStatusPinned($"Teleporting to {trader.Name}",
                async () => result = await AttemptTerritoryTeleport(traderTerr, traderPos, "trade-teleport", TeleportWatchdogMs));
            transportBlocked = result == TerritoryTeleportResult.Blocked;
            ErrorIf(result != TerritoryTeleportResult.Reached,
                $"Could not reach {trader.Name}'s zone (still in {Svc.ClientState.TerritoryType}); aborting trade.");
        }

        await RunWithStatusPinned($"Walking to {trader.Name}", async () =>
        {
            await RideAethernetShortcut(traderPos, "trade-aethernet");
            await WalkWithRetries(
                () => new MoveOp(o => o.Move(traderTerr, traderPos,
                    MovementConfig.Everything.WithTolerance(WalkToleranceMeters),
                    stopCondition: null)),
                MoveWatchdogMs, "trade-walk", () => WithinReach(traderPos, WalkToleranceMeters));
        });
    }

    private async Task BuyOrder(TraderLocation trader, TradeOrder order)
    {
        var item = order.Item;
        var need = TradeList.RemainingNeed(item, order.StopAtCount);
        var wallet = GemstoneCount();
        var qty = GemstoneCatalog.ComputeBuyQuantity(wallet, item.CostPerOne, need, spentGems);
        if (qty <= 0)
        {
            passedOverItemIds.Add(item.ItemId);
            Diag($"Passing over {item.ItemName}: need {need}, wallet {wallet}g, {spentGems}g spent this trade, {item.CostPerOne}g each.");
            return;
        }

        if (!await OpenShopFor(trader, item))
        {
            Diag($"{item.ItemName} is not in any shop {trader.Name} opened; not buying it.");
            return;
        }

        Status = $"Buying {qty} × {item.ItemName}";
        Diag($"Shop open. Wallet={wallet}, cost={item.CostPerOne}, mode={Plugin.Cfg.SpendMode}, need={need}, qty={qty}");

        if (!await WaitUntilTimed(() => ShopInteraction.BuyFromCurrencyShop(item.ItemId, qty), ConfirmWaitMs, "buy-select"))
        {
            Diag($"Could not select {item.ItemName} in the open shop.");
            return;
        }

        if (!await WaitUntilTimed(
                () => ShopInteraction.SelectYesnoOpen() || GemstoneCount() < wallet,
                ConfirmWaitMs, "wait-confirm"))
            Diag("No confirm dialog and no spend detected within window; attempting to continue.");

        if (ShopInteraction.SelectYesnoOpen())
            await WaitUntilTimed(
                () => !ShopInteraction.SelectYesnoOpen() || ShopInteraction.ClickSelectYesno(),
                ConfirmWaitMs, "confirm-yes");

        if (!await WaitUntilTimed(() => GemstoneCount() < wallet, SpendWaitMs, "wait-spend"))
        {
            Diag($"Wallet unchanged after {SpendWaitMs / 1000}s (was {wallet}, now {GemstoneCount()}); {item.ItemName} was not bought.");
            return;
        }

        spentGems += wallet - GemstoneCount();
        boughtItemIds.Add(item.ItemId);
        Diag($"Bought {qty} × {item.ItemName}; wallet now {GemstoneCount()}g, {spentGems}g spent this trade.");
        await DelayMs(PurchaseSettleMs);
    }

    // Each sub-shop of a trader's menu holds part of the stock, so a later order may need the menu again, or a fresh
    // talk for traders whose shop closes straight back to the world.
    private async Task<bool> OpenShopFor(TraderLocation trader, GemstoneTradeItem item)
    {
        if (ShopInteraction.ShopExchangeCurrencyOpen())
        {
            if (ShopInteraction.FindCurrencyShopSlot(item.ItemId) >= 0)
            {
                return true;
            }

            Status = "Switching shops";
            ShopInteraction.CloseShop();
            await WaitUntilTimed(ShopInteraction.SelectIconStringOpen, MenuReturnWaitMs, "wait-menu-return");
        }

        if (!ShopInteraction.SelectIconStringOpen())
            await TalkTo(trader);

        if (ShopInteraction.SelectIconStringOpen())
            await NavigateSubMenu(item);

        return ShopInteraction.ShopExchangeCurrencyOpen() && ShopInteraction.FindCurrencyShopSlot(item.ItemId) >= 0;
    }

    private async Task TalkTo(TraderLocation trader)
    {
        var npc = RepairOps.FindNearestObjectByBaseId(trader.EnpcBaseId);
        ErrorIf(npc is null, $"Could not find {trader.Name} (ENpcBase {trader.EnpcBaseId}) near {trader.Position}.");

        Status = $"Talking to {trader.Name}";
        Diag($"Interacting with {trader.Name} (BaseId={npc!.BaseId})");
        var interact = new MoveOp(o => o.Interact(npc,
            waitUntil: () => ShopInteraction.ShopExchangeCurrencyOpen() || ShopInteraction.SelectIconStringOpen(),
            skip: UiSkipOptions.Talk | UiSkipOptions.YesNo));
        await RunCancellable(interact, InteractWaitMs, "trade-interact");
        ErrorIf(interact.Fault is not null, $"Interacting with {trader.Name} failed: {interact.Fault?.Message}");

        ErrorIf(!await WaitUntilTimed(
                () => ShopInteraction.ShopExchangeCurrencyOpen() || ShopInteraction.SelectIconStringOpen(),
                ShopOpenWaitMs, "wait-shop-or-menu"),
            $"{trader.Name} did not open a shop/menu within {ShopOpenWaitMs / 1000}s; aborting trade.");
    }

    private async Task NavigateSubMenu(GemstoneTradeItem item)
    {
        var menuCount = ShopInteraction.SelectIconStringEntryCount();
        Diag($"Sub-menu open with {menuCount} entries; scanning for {item.ItemName}.");

        for (var i = 0; i < menuCount; i++)
        {
            if (!ShopInteraction.SelectIconStringOpen()) break;

            Status = $"Trying menu entry {i + 1}/{menuCount}";
            if (!ShopInteraction.ClickSelectIconString(i))
            {
                await DelayMs(MenuRetryMs);
                continue;
            }

            await WaitUntilTimed(
                () => ShopInteraction.ShopExchangeCurrencyOpen() || ShopInteraction.SelectIconStringOpen(),
                SubmenuWaitMs, $"wait-submenu-{i}");

            if (!ShopInteraction.ShopExchangeCurrencyOpen()) continue;

            if (ShopInteraction.FindCurrencyShopSlot(item.ItemId) >= 0)
            {
                Diag($"Found {item.ItemName} in menu entry {i}.");
                return;
            }

            Diag($"Menu entry {i} did not contain {item.ItemName}; closing and trying next.");
            ShopInteraction.CloseShop();
            await WaitUntilTimed(
                ShopInteraction.SelectIconStringOpen,
                SubmenuWaitMs, $"wait-submenu-reopen-{i}");
        }
    }

    private static int GemstoneCount() => GemstoneCatalog.CurrentWalletCount();
}
