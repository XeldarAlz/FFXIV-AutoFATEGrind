using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoFateGrind.Core.Game.Fates;

// Collect FATE turn-in items live in the key-items container, which GetInventoryItemCount never scans.
internal static unsafe class FateItems
{
    private static readonly Dictionary<uint, uint> turnInItemByFateId = new();

    public static uint TurnInItemId(uint fateId)
    {
        if (turnInItemByFateId.TryGetValue(fateId, out var cached))
        {
            return cached;
        }

        var row = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>()?.GetRowOrDefault(fateId);
        var itemId = row?.EventItem.RowId ?? 0u;
        turnInItemByFateId[fateId] = itemId;
        return itemId;
    }

    public static int HeldCount(uint eventItemId)
    {
        if (eventItemId == 0)
        {
            return 0;
        }
        var manager = InventoryManager.Instance();
        if (manager is null)
        {
            return 0;
        }
        var container = manager->GetInventoryContainer(InventoryType.KeyItems);
        if (container is null || !container->IsLoaded)
        {
            return 0;
        }

        var held = 0;
        for (var slot = 0; slot < container->Size; slot++)
        {
            var item = container->GetInventorySlot(slot);
            if (item is null || item->GetItemId() != eventItemId)
            {
                continue;
            }
            held += (int)item->GetQuantity();
        }
        return held;
    }
}
