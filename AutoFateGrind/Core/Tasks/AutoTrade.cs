using AutoFateGrind.Core.Game;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;
using clib.TaskSystem;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed class AutoTrade(uint targetItemId, uint originTerritoryId, ExpansionKind originExpansion) : AutoCommon
{
    private readonly uint targetItemId = targetItemId;
    private readonly uint originTerritoryId = originTerritoryId;
    private readonly ExpansionKind originExpansion = originExpansion;

    private const int TeleportWatchdogMs = 60_000;
    private const int TerritoryWaitMs = 45_000;
    private const int MoveWatchdogMs = 120_000;
    private const int InteractWaitMs = 15_000;
    private const int ShopOpenWaitMs = 15_000;
    private const int ConfirmWaitMs = 10_000;
    private const int SpendWaitMs = 10_000;
    private const int SubmenuWaitMs = 10_000;

    protected override async Task Execute()
    {
        var item = GemstoneCatalog.FindById(targetItemId);
        ErrorIf(item is null, "No target item set. Open /afg config → Trader and pick one.");

        var trader = GemstoneTrader.PickForItem(targetItemId, originTerritoryId, originExpansion);
        ErrorIf(trader is null, $"No registered Bicolor trader sells {item!.ItemName}.");

        Diag($"AutoTrade start: item={item!.ItemName}({item.ItemId}) trader={trader!.Name} terr={trader.TerritoryId} from={originTerritoryId}");
        Svc.Chat.Print($"[AFG] Auto-trade: {item.ItemName} ({item.CostPerOne} gems each) at {trader.Name}");

        if (Svc.ClientState.TerritoryType != trader.TerritoryId)
        {
            await RunWithStatusPinned($"Teleporting to {trader.Name}", async () =>
            {
                if (!await AwaitWatchdog(TeleportTo(trader.TerritoryId, trader.Position, allowSameZoneTeleport: false), TeleportWatchdogMs, "trade-teleport"))
                    return;
                await AwaitWatchdog(WaitUntilTerritory(trader.TerritoryId), TerritoryWaitMs, "trade-wait-territory");
            });
            ErrorIf(Svc.ClientState.TerritoryType != trader.TerritoryId,
                $"Could not reach {trader.Name}'s zone (still in {Svc.ClientState.TerritoryType}); aborting trade.");
        }

        await RunWithStatusPinned($"Walking to {trader.Name}", async () =>
        {
            var moveTask = MoveTo(trader.TerritoryId, trader.Position,
                MovementConfig.Everything.WithTolerance(4f),
                allowTeleportIfFaster: false,
                stopCondition: null,
                onStopReached: null,
                allowAethernetWithinTerritory: true);
            await AwaitWatchdog(moveTask, MoveWatchdogMs, "trade-walk");
        });

        var npc = FindTraderObject(trader.EnpcBaseId);
        ErrorIf(npc is null, $"Could not find {trader.Name} (ENpcBase {trader.EnpcBaseId}) near {trader.Position}.");

        Status = $"Talking to {trader.Name}";
        Diag($"Interacting with {trader.Name} (BaseId={npc!.BaseId})");
        await AwaitWatchdog(
            InteractWith(npc, waitUntil: null, selectStringIndex: null, skip: UiSkipOptions.YesNo),
            InteractWaitMs, "trade-interact", stopNavOnTimeout: false);

        ErrorIf(!await WaitUntilTimed(
                () => ShopInteraction.ShopExchangeCurrencyOpen() || ShopInteraction.SelectIconStringOpen(),
                ShopOpenWaitMs, "wait-shop-or-menu"),
            $"{trader.Name} did not open a shop/menu within {ShopOpenWaitMs / 1000}s; aborting trade.");

        if (ShopInteraction.SelectIconStringOpen())
            await NavigateSubMenu(item);

        ErrorIf(!ShopInteraction.ShopExchangeCurrencyOpen(),
            "Could not open the gemstone exchange shop. Target item may not be sold by this trader.");

        var wallet = GemstoneCount();
        var qty = GemstoneCatalog.ComputeBuyQuantity(wallet, item.CostPerOne);
        ErrorIf(qty <= 0,
            $"Reserve ({Plugin.Cfg.KeepGemstonesReserve}g) and spend mode leave no budget for {item.ItemName} ({item.CostPerOne}g each); wallet={wallet}.");

        Status = $"Buying {qty} × {item.ItemName}";
        Diag($"Shop open. Wallet={wallet}, cost={item.CostPerOne}, mode={Plugin.Cfg.SpendMode}, qty={qty}");

        ErrorIf(!ShopInteraction.BuyFromCurrencyShop(item.ItemId, qty),
            $"Target item {item.ItemName} not visible in the open shop.");

        if (!await WaitUntilTimed(
                () => ShopInteraction.SelectYesnoOpen() || GemstoneCount() < wallet,
                ConfirmWaitMs, "wait-confirm"))
            Diag("No confirm dialog and no spend detected within window; attempting to continue.");

        if (ShopInteraction.SelectYesnoOpen())
            ShopInteraction.ClickSelectYesno();

        if (!await WaitUntilTimed(() => GemstoneCount() < wallet, SpendWaitMs, "wait-spend"))
            Diag($"Wallet unchanged after {SpendWaitMs / 1000}s (was {wallet}, now {GemstoneCount()}); closing shop anyway.");

        Status = "Closing shop";
        ShopInteraction.CloseShop();
        await NextFrame(20);

        Svc.Chat.Print($"[AFG] Trade complete. Gemstones now: {GemstoneCount()}");
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
                await NextFrame(30);
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

    private static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindTraderObject(uint enpcBaseId)
    {
        Dalamud.Game.ClientState.Objects.Types.IGameObject? best = null;
        var bestDist = float.MaxValue;
        var player = Svc.Objects.LocalPlayer;
        var playerPos = player?.Position ?? Vector3.Zero;

        foreach (var obj in Svc.Objects)
        {
            if (obj.BaseId != enpcBaseId) continue;
            var d = player is null ? 0 : Vector3.Distance(obj.Position, playerPos);
            if (d < bestDist) { best = obj; bestDist = d; }
        }
        return best;
    }

    private static int GemstoneCount() => GemstoneCatalog.CurrentWalletCount();
}
