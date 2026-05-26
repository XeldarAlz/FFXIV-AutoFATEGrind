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

    protected override async Task Execute()
    {
        var item = GemstoneCatalog.FindById(targetItemId);
        ErrorIf(item is null, "No target item set. Open /afg config → Trader and pick one.");

        var trader = GemstoneTrader.PickForItem(targetItemId, originTerritoryId, originExpansion);
        ErrorIf(trader is null, $"No registered Bicolor trader sells {item!.ItemName}.");

        Svc.Chat.Print($"[AFG] Auto-trade: {item!.ItemName} ({item.CostPerOne} gems each) at {trader!.Name}");

        if (Svc.ClientState.TerritoryType != trader.TerritoryId)
        {
            Status = $"Teleporting to {trader.Name}";
            await TeleportTo(trader.TerritoryId, trader.Position, allowSameZoneTeleport: false);
            await WaitUntilTerritory(trader.TerritoryId);
        }

        var walkLabel = $"Walking to {trader.Name}";
        Status = walkLabel;
        await MoveTo(trader.TerritoryId, trader.Position,
            MovementConfig.Everything.WithTolerance(4f),
            allowTeleportIfFaster: false,
            stopCondition: () => { Status = walkLabel; return false; },
            onStopReached: null,
            allowAethernetWithinTerritory: true);

        var npc = FindTraderObject(trader.EnpcBaseId);
        ErrorIf(npc is null, $"Could not find {trader.Name} (ENpcBase {trader.EnpcBaseId}) near {trader.Position}.");

        Status = $"Talking to {trader.Name}";
        Diag($"Interacting with {trader.Name} (BaseId={npc!.BaseId})");
        await InteractWith(npc, waitUntil: null, selectStringIndex: null, skip: UiSkipOptions.YesNo);

        await WaitUntil(
            condition: () => ShopInteraction.ShopExchangeCurrencyOpen() || ShopInteraction.SelectIconStringOpen(),
            scopeName: "wait-shop-or-menu",
            checkFrequency: 30,
            logContinuously: false);

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

        await WaitUntil(
            condition: () => ShopInteraction.SelectYesnoOpen() || GemstoneCount() < wallet,
            scopeName: "wait-confirm",
            checkFrequency: 30,
            logContinuously: false);

        if (ShopInteraction.SelectYesnoOpen())
            ShopInteraction.ClickSelectYesno();

        await WaitUntil(
            condition: () => GemstoneCount() < wallet,
            scopeName: "wait-spend",
            checkFrequency: 30,
            logContinuously: false);

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

            await WaitUntil(
                condition: () => ShopInteraction.ShopExchangeCurrencyOpen() || ShopInteraction.SelectIconStringOpen(),
                scopeName: $"wait-submenu-{i}",
                checkFrequency: 30,
                logContinuously: false);

            if (!ShopInteraction.ShopExchangeCurrencyOpen()) continue;

            if (ShopInteraction.FindCurrencyShopSlot(item.ItemId) >= 0)
            {
                Diag($"Found {item.ItemName} in menu entry {i}.");
                return;
            }

            Diag($"Menu entry {i} did not contain {item.ItemName}; closing and trying next.");
            ShopInteraction.CloseShop();
            await WaitUntil(
                condition: ShopInteraction.SelectIconStringOpen,
                scopeName: $"wait-submenu-reopen-{i}",
                checkFrequency: 30,
                logContinuously: false);
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
