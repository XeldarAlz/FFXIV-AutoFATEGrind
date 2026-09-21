using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace AutoFateGrind.Core.Game.Yokai;

internal static unsafe class YokaiOps
{
    private const ushort WristsEquipSlot = 10;
    private const uint ActionReady = 0;

    private static readonly InventoryType[] watchContainers =
    [
        InventoryType.ArmoryWrist,
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    public static uint SummonedMinionId()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null)
        {
            return 0;
        }

        var character = (Character*)player.Address;
        var minion = character->CompanionData.CompanionObject;
        return minion is not null ? minion->BaseId : character->CompanionData.CompanionId;
    }

    public static bool IsWatchEquipped()
        => FindItem(InventoryType.EquippedItems, YokaiCatalog.WatchItemId, out _);

    public static bool OwnsWatch()
    {
        var inventory = InventoryManager.Instance();
        return inventory is not null && inventory->GetInventoryItemCount(YokaiCatalog.WatchItemId) > 0;
    }

    public static bool TrySummon(uint minionId)
    {
        var actions = ActionManager.Instance();
        if (actions is null)
        {
            return false;
        }

        if (actions->GetActionStatus(ActionType.Companion, minionId) != ActionReady)
        {
            return false;
        }

        return actions->UseAction(ActionType.Companion, minionId);
    }

    public static bool TryEquipWatch()
    {
        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return false;
        }

        for (var index = 0; index < watchContainers.Length; index++)
        {
            if (!FindItem(watchContainers[index], YokaiCatalog.WatchItemId, out var slot))
            {
                continue;
            }

            inventory->MoveItemSlot(watchContainers[index], slot, InventoryType.EquippedItems, WristsEquipSlot, true);
            return true;
        }

        return false;
    }

    private static bool FindItem(InventoryType containerType, uint itemId, out ushort slot)
    {
        slot = 0;
        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return false;
        }

        var container = inventory->GetInventoryContainer(containerType);
        if (container is null || !container->IsLoaded)
        {
            return false;
        }

        for (var index = 0; index < container->Size; index++)
        {
            var item = container->GetInventorySlot(index);
            if (item is null || item->ItemId != itemId)
            {
                continue;
            }

            slot = (ushort)index;
            return true;
        }

        return false;
    }
}
