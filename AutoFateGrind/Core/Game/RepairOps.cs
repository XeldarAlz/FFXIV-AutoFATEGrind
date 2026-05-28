using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoFateGrind.Core.Game;

// Repair detection + addon helpers, structured the same way as ShopInteraction.cs.
internal static unsafe class RepairOps
{
    // The Repair general-action id (the same icon the player slots on a hotbar).
    public const uint RepairGeneralActionId = 6;

    // Lowest item Condition across the 13 equipped slots, in 0..100%. Condition is stored in 1/300ths
    // internally; matches AutoDuty's check.
    public static float LowestEquippedConditionPct()
    {
        var im = InventoryManager.Instance();
        if (im is null) return 100f;
        var container = im->GetInventoryContainer(InventoryType.EquippedItems);
        if (container is null || !container->IsLoaded) return 100f;

        uint lowest = 30000;
        var anySeen = false;
        for (uint i = 0; i < 13; i++)
        {
            var item = container->Items[i];
            if (item.ItemId == 0) continue;
            anySeen = true;
            if (item.Condition < lowest) lowest = item.Condition;
        }
        if (!anySeen) return 100f;
        return lowest / 300f;
    }

    public static bool NeedsRepair(int thresholdPct)
        => LowestEquippedConditionPct() <= thresholdPct;

    // True iff every repair material the equipped set needs is somewhere in the bag at the required
    // grade or better. Used to skip the in-place self-repair branch and go straight to the NPC when we
    // know it would fail.
    public static bool HasDarkMatterForAllEquipped()
    {
        var im = InventoryManager.Instance();
        if (im is null) return false;
        var container = im->GetInventoryContainer(InventoryType.EquippedItems);
        if (container is null || !container->IsLoaded) return false;

        var itemSheet = Svc.Data.GetExcelSheet<Item>();
        if (itemSheet is null) return false;

        for (uint i = 0; i < 13; i++)
        {
            var equipped = container->Items[i];
            if (equipped.ItemId == 0) continue;
            if (!itemSheet.TryGetRow(equipped.ItemId, out var row)) continue;

            var resource = row.ItemRepair.ValueNullable;
            if (resource is null) continue;
            var dmId = resource.Value.Item.RowId;
            if (dmId == 0) continue;
            if (!HasDarkMatterOrBetter(dmId, im)) return false;
        }
        return true;
    }

    private static bool HasDarkMatterOrBetter(uint requiredDmId, InventoryManager* im)
    {
        var sheet = Svc.Data.GetExcelSheet<ItemRepairResource>();
        if (sheet is null) return false;
        foreach (var dm in sheet)
        {
            if (dm.Item.RowId < requiredDmId) continue;
            if (im->GetInventoryItemCount(dm.Item.RowId) > 0) return true;
        }
        return false;
    }

    // ---------- Self-repair action ----------

    public static bool TriggerRepairGeneralAction()
    {
        if (!EzThrottler.Throttle("AFG.Repair.Trigger", 500)) return false;
        var am = ActionManager.Instance();
        if (am is null) return false;
        am->UseAction(ActionType.GeneralAction, RepairGeneralActionId);
        return true;
    }

    // ---------- Repair UI helpers (mirrors ShopInteraction.cs) ----------

    public static bool RepairAddonOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>("Repair", out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool SelectYesnoOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectYesno", out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool SelectIconStringOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectIconString", out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool ClickRepairAll()
    {
        if (!EzThrottler.Throttle("AFG.Repair.RepairAll", 500)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("Repair", out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        new AddonMaster.Repair(addon).RepairAll();
        return true;
    }

    public static bool ClickSelectYesno()
    {
        if (!EzThrottler.Throttle("AFG.Repair.Yesno", 500)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectYesno", out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        new AddonMaster.SelectYesno(addon).Yes();
        return true;
    }

    public static bool ClickSelectIconString(int index)
    {
        if (!EzThrottler.Throttle("AFG.Repair.IconString", 500)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectIconString", out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        var entries = new AddonMaster.SelectIconString(addon).Entries;
        if (index < 0 || index >= entries.Length) return false;
        entries[index].Select();
        return true;
    }

    public static void HideRepairAgent()
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Repair);
        if (agent is null) return;
        agent->Hide();
    }

    // ---------- Grand Company mender lookup ----------

    public record struct GcMender(uint TerritoryId, Vector3 Position, uint DataId, string Name);

    public static GcMender? GetGrandCompanyMender()
    {
        var state = PlayerState.Instance();
        if (state is null) return null;
        return state->GrandCompany switch
        {
            1 => new GcMender(128, new Vector3(17.715698f, 40.200005f, 3.9520264f), 1003251u, "Maelstrom Mender"),
            2 => new GcMender(132, new Vector3(24.826416f, -8f, 93.18677f), 1000394u, "Twin Adder Mender"),
            3 => new GcMender(130, new Vector3(32.85266f, 6.999999f, -81.31531f), 1004416u, "Flame Mender"),
            _ => null,
        };
    }

    public static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindObjectByBaseId(uint baseId)
    {
        foreach (var obj in Svc.Objects)
            if (obj.BaseId == baseId) return obj;
        return null;
    }
}
