using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Game;

// Consume convention: UseAction(ActionType.Item, id, extraParam 65535), with HQ addressed as id + 1,000,000.
internal static unsafe class FoodOps
{
    // Item UI categories holding consumables (Meal / Seafood / Medicine) and the two statuses they grant.
    private static readonly HashSet<uint> ConsumableUiCategories = [44, 45, 46];
    public const uint WellFedStatusId = 48;
    public const uint MedicatedStatusId = 49;

    private static List<ConsumableEntry>? catalog;

    public static IReadOnlyList<ConsumableEntry> Catalog => catalog ??= BuildCatalog();

    private static List<ConsumableEntry> BuildCatalog()
    {
        var sheet = Svc.Data.GetExcelSheet<Item>();
        if (sheet is null) return [];

        var result = new List<ConsumableEntry>();
        foreach (var item in sheet)
        {
            var name = item.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (item.ItemUICategory.ValueNullable?.RowId is not { } uiCat || !ConsumableUiCategories.Contains(uiCat)) continue;
            if (item.ItemAction.ValueNullable is not { } action) continue;

            // Data[0] is the granted status (48 Well Fed for food, 49 Medicated for medicine).
            var status = (uint)action.Data[0];
            if (status != WellFedStatusId && status != MedicatedStatusId) continue;

            result.Add(new ConsumableEntry
            {
                ItemId = item.RowId,
                Name = name,
                StatusId = status,
                CanBeHq = item.CanBeHq,
            });
        }
        return [.. result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public static int ItemCount(uint itemId)
    {
        var im = InventoryManager.Instance();
        return im is null ? 0 : im->GetInventoryItemCount(itemId);
    }

    public static bool IsAvailable(ConsumableEntry e)
        => (e.CanBeHq && ItemCount(e.ItemId + 1_000_000) >= 1) || ItemCount(e.ItemId) >= 1;

    public static bool HasStatus(uint statusId, float minSeconds)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return false;
        foreach (var s in player.StatusList)
            if (s.StatusId == statusId && (minSeconds <= 0 || s.RemainingTime > minSeconds))
                return true;
        return false;
    }

    // Cheap enough to gate the eat step every loop so the buff-healthy case is a no-op.
    public static bool AnyNeeded(Configuration cfg)
    {
        var minSeconds = Math.Max(0, cfg.AutoConsumeMinMinutes) * 60f;
        foreach (var e in cfg.AutoConsumeItems)
            if (!HasStatus(e.StatusId, minSeconds) && IsAvailable(e))
                return true;
        return false;
    }

    // Throttled so a repeated-call loop can't spam UseAction faster than the game accepts it.
    public static bool UseConsumable(ConsumableEntry e)
    {
        if (!EzThrottler.Throttle("AFG.Food.Use", 500)) return false;
        var am = ActionManager.Instance();
        if (am is null) return false;

        var hqId = e.ItemId + 1_000_000;
        var useId = e.CanBeHq && ItemCount(hqId) >= 1 ? hqId
                  : ItemCount(e.ItemId) >= 1 ? e.ItemId
                  : 0u;
        if (useId == 0) return false;

        am->UseAction(ActionType.Item, useId, extraParam: 65535);
        return true;
    }
}
