using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoFateGrind.Core.Game;

// Patterned after Senither/AutoWeeklyCap. 500ms throttle prevents addon spam.
internal static unsafe class ShopInteraction
{
    public static bool ShopOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>("Shop", out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool InclusionShopOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>("InclusionShop", out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool SelectIconStringOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectIconString", out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool SelectYesnoOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectYesno", out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool ClickSelectIconString(int index)
    {
        if (!EzThrottler.Throttle("AFG.ClickSelectIconString", 500)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectIconString", out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        new AddonMaster.SelectIconString(addon).Entries[index].Select();
        return true;
    }

    public static bool ClickSelectYesno()
    {
        if (!EzThrottler.Throttle("AFG.ClickSelectYesno", 500)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectYesno", out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        new AddonMaster.SelectYesno(addon).Yes();
        return true;
    }

    public static bool BuyFromShop(int slotIndex, int quantity)
    {
        if (!EzThrottler.Throttle("AFG.BuyFromShop", 500)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("Shop", out var addon)) return false;
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
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("Shop", out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        addon->Close(true);
        return true;
    }

    public static string? CurrentAddonName()
    {
        string[] candidates = ["Shop", "InclusionShop", "SelectIconString", "SelectString", "SelectYesno", "InputNumeric", "InputString"];
        foreach (var name in candidates)
        {
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var addon)
                && GenericHelpers.IsAddonReady(addon))
                return name;
        }
        return null;
    }
}
