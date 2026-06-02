using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoFateGrind.Core.Game;

// AddonInteractThrottleMs prevents addon spam.
internal static unsafe class ShopInteraction
{
    public static bool ShopOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.Shop, out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool ShopExchangeCurrencyOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.ShopExchangeCurrency, out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool InclusionShopOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.InclusionShop, out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool SelectIconStringOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.SelectIconString, out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool SelectYesnoOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.SelectYesno, out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static int SelectIconStringEntryCount()
    {
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.SelectIconString, out var addon)) return 0;
        if (!GenericHelpers.IsAddonReady(addon)) return 0;
        return new AddonMaster.SelectIconString(addon).Entries.Length;
    }

    public static bool ClickSelectIconString(int index)
    {
        if (!EzThrottler.Throttle(AfgConstants.ThrottleKeys.ShopClickIconString, AfgConstants.AddonInteractThrottleMs)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.SelectIconString, out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        var entries = new AddonMaster.SelectIconString(addon).Entries;
        if (index < 0 || index >= entries.Length) return false;
        entries[index].Select();
        return true;
    }

    public static bool ClickSelectYesno()
    {
        if (!EzThrottler.Throttle(AfgConstants.ThrottleKeys.ShopClickYesno, AfgConstants.AddonInteractThrottleMs)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.SelectYesno, out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        new AddonMaster.SelectYesno(addon).Yes();
        return true;
    }

    public static int FindCurrencyShopSlot(uint targetItemId)
    {
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.ShopExchangeCurrency, out var addon)) return -1;
        if (!GenericHelpers.IsAddonReady(addon)) return -1;

        var master = new AddonMaster.ShopExchangeCurrency(addon);
        var items = master.BasicShopItems;
        for (var i = 0; i < items.Length; i++)
            if (items[i].ItemId == targetItemId) return i;
        return -1;
    }

    public static bool BuyFromCurrencyShop(uint targetItemId, int quantity)
    {
        if (!EzThrottler.Throttle(AfgConstants.ThrottleKeys.ShopBuyCurrency, AfgConstants.AddonInteractThrottleMs)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.ShopExchangeCurrency, out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;

        var master = new AddonMaster.ShopExchangeCurrency(addon);
        foreach (var entry in master.BasicShopItems)
        {
            if (entry.ItemId != targetItemId) continue;
            entry.Select(quantity);
            Svc.Log.Info($"[AFG] ShopExchangeCurrency.Select(item={targetItemId} qty={quantity} index={entry.Index})");
            return true;
        }
        return false;
    }

    public static bool BuyFromShop(int slotIndex, int quantity)
    {
        if (!EzThrottler.Throttle(AfgConstants.ThrottleKeys.ShopBuy, AfgConstants.AddonInteractThrottleMs)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.Shop, out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;

        var values = stackalloc AtkValue[3];
        values[0].Type = AtkValueType.Int; values[0].Int = 0;
        values[1].Type = AtkValueType.Int; values[1].Int = slotIndex;
        values[2].Type = AtkValueType.Int; values[2].Int = quantity;
        addon->FireCallback(3, values);
        Svc.Log.Info($"[AFG] Shop.FireCallback(buy slot={slotIndex} qty={quantity})");
        return true;
    }

    public static bool CloseShop()
    {
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.ShopExchangeCurrency, out var exch)
            && GenericHelpers.IsAddonReady(exch))
        {
            exch->Close(true);
            return true;
        }
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.Shop, out var addon)
            && GenericHelpers.IsAddonReady(addon))
        {
            addon->Close(true);
            return true;
        }
        return false;
    }

    public static string? CurrentAddonName()
    {
        string[] candidates =
        [
            AfgConstants.AddonNames.Shop, AfgConstants.AddonNames.ShopExchangeCurrency, AfgConstants.AddonNames.InclusionShop,
            AfgConstants.AddonNames.SelectIconString, AfgConstants.AddonNames.SelectString, AfgConstants.AddonNames.SelectYesno,
            AfgConstants.AddonNames.InputNumeric, AfgConstants.AddonNames.InputString,
        ];
        foreach (var name in candidates)
        {
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var addon)
                && GenericHelpers.IsAddonReady(addon))
                return name;
        }
        return null;
    }
}
