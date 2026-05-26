using AutoFateGrind.Core.Game;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;
using clib.TaskSystem;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed class AutoTrade(uint targetItemId, ExpansionKind originExpansion) : AutoCommon
{
    private readonly uint targetItemId = targetItemId;
    private readonly ExpansionKind originExpansion = originExpansion;

    protected override async Task Execute()
    {
        var item = GemstoneCatalog.FindById(targetItemId);
        var trader = GemstoneTrader.PickFor(originExpansion);
        ErrorIf(item is null, "No target item set. Open /afg config -> Trader and pick one.");

        Svc.Chat.Print($"[AFG] Auto-trade: {item!.ItemName} ({item.CostPerOne} gems each) at {trader.Name}");

        if (Svc.ClientState.TerritoryType != trader.TerritoryId)
        {
            Status = $"Teleporting to {trader.Name}";
            await TeleportTo(trader.TerritoryId, trader.Position, allowSameZoneTeleport: false);
            await WaitUntilTerritory(trader.TerritoryId);
        }

        Status = $"Walking to {trader.Name}";
        await MoveTo(trader.TerritoryId, trader.Position,
            MovementConfig.Everything.WithTolerance(4f),
            allowTeleportIfFaster: false,
            stopCondition: null,
            onStopReached: null,
            allowAethernetWithinTerritory: true);

        var npc = FindNearestNpc(trader.Position, 8f);
        ErrorIf(npc is null, $"Could not find a vendor NPC near {trader.Name}. Move next to them and rerun.");

        Status = $"Talking to {npc!.Name}";
        await InteractWith(npc, waitUntil: null, selectStringIndex: null, skip: UiSkipOptions.YesNo);

        await WaitUntil(
            condition: () => ShopInteraction.ShopOpen() || ShopInteraction.SelectIconStringOpen(),
            scopeName: "wait-shop-or-menu",
            checkFrequency: 30,
            logContinuously: false);

        if (ShopInteraction.SelectIconStringOpen())
        {
            Status = "Selecting gemstone exchange";
            ShopInteraction.ClickSelectIconString(0);
            await WaitUntil(ShopInteraction.ShopOpen, "wait-shop-after-menu", 30, false);
        }

        var wallet = GemstoneCount();
        var maxQty = (int)Math.Max(1, wallet / (int)item.CostPerOne);
        Status = $"Buying {maxQty} of {item.ItemName}";
        Diag($"Shop open. Wallet={wallet}, cost={item.CostPerOne}, qty={maxQty}");

        ShopInteraction.BuyFromShop(slotIndex: ResolveSlotIndex(item), quantity: maxQty);

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

    // v0.2: hardcoded to first shop slot. v0.3 will resolve via FateShop -> SpecialShop ordering.
    private static int ResolveSlotIndex(GemstoneTradeItem item)
    {
        _ = item;
        return 0;
    }

    private static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindNearestNpc(Vector3 anchor, float maxDistance)
    {
        Dalamud.Game.ClientState.Objects.Types.IGameObject? best = null;
        var bestDist = float.MaxValue;
        foreach (var obj in Svc.Objects)
        {
            if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc) continue;
            var d = Vector3.Distance(obj.Position, anchor);
            if (d > maxDistance) continue;
            if (d < bestDist) { best = obj; bestDist = d; }
        }
        return best;
    }

    private static unsafe int GemstoneCount()
    {
        var im = InventoryManager.Instance();
        return im is null ? 0 : im->GetInventoryItemCount(GemstoneCatalog.BicolorGemstoneItemId);
    }
}
