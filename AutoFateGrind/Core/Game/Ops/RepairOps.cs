using AutoFateGrind.Core.Game.Player;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoFateGrind.Core.Game.Ops;

internal static unsafe class RepairOps
{
    public const uint RepairGeneralActionId = 6;

    // Condition is stored internally in 1/300ths (full = 30000) across the equipped-gear slots.
    private const float  ConditionPerPercent = 300f;
    private const uint   MaxConditionRaw = 30000;
    private const uint   EquippedSlotCount = 13;
    private const string RepairMenuKeyword = "repair";

    // Convert the lowest equipped-slot condition to 0..100%.
    public static float LowestEquippedConditionPct()
    {
        var im = InventoryManager.Instance();
        if (im is null) return 100f;
        var container = im->GetInventoryContainer(InventoryType.EquippedItems);
        if (container is null || !container->IsLoaded) return 100f;

        uint lowest = MaxConditionRaw;
        var anySeen = false;
        for (uint i = 0; i < EquippedSlotCount; i++)
        {
            var item = container->Items[i];
            if (item.ItemId == 0) continue;
            anySeen = true;
            if (item.Condition < lowest) lowest = item.Condition;
        }
        if (!anySeen) return 100f;
        return lowest / ConditionPerPercent;
    }

    public static bool NeedsRepair(int thresholdPct)
        => LowestEquippedConditionPct() <= thresholdPct;

    public static bool HasDarkMatterForAllEquipped()
    {
        var im = InventoryManager.Instance();
        if (im is null) return false;
        var container = im->GetInventoryContainer(InventoryType.EquippedItems);
        if (container is null || !container->IsLoaded) return false;

        var itemSheet = Svc.Data.GetExcelSheet<Item>();
        if (itemSheet is null) return false;

        for (uint i = 0; i < EquippedSlotCount; i++)
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

    public static bool TriggerRepairGeneralAction()
    {
        if (!EzThrottler.Throttle(AfgConstants.ThrottleKeys.RepairTrigger, AfgConstants.AddonInteractThrottleMs)) return false;
        var am = ActionManager.Instance();
        if (am is null) return false;
        am->UseAction(ActionType.GeneralAction, RepairGeneralActionId);
        return true;
    }

    public static bool RepairAddonOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.Repair, out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool SelectYesnoOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.SelectYesno, out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool SelectIconStringOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.SelectIconString, out var addon)
        && GenericHelpers.IsAddonReady(addon);

    public static bool ClickRepairAll()
    {
        if (!EzThrottler.Throttle(AfgConstants.ThrottleKeys.RepairAll, AfgConstants.AddonInteractThrottleMs)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.Repair, out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        new AddonMaster.Repair(addon).RepairAll();
        return true;
    }

    public static bool ClickSelectYesno()
    {
        if (!EzThrottler.Throttle(AfgConstants.ThrottleKeys.RepairYesno, AfgConstants.AddonInteractThrottleMs)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.SelectYesno, out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        new AddonMaster.SelectYesno(addon).Yes();
        return true;
    }

    public static bool ClickSelectIconString(int index)
    {
        if (!EzThrottler.Throttle(AfgConstants.ThrottleKeys.RepairIconString, AfgConstants.AddonInteractThrottleMs)) return false;
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.SelectIconString, out var addon)) return false;
        if (!GenericHelpers.IsAddonReady(addon)) return false;
        var entries = new AddonMaster.SelectIconString(addon).Entries;
        if (index < 0 || index >= entries.Length) return false;
        entries[index].Select();
        return true;
    }

    // Index of the talk-menu entry whose label looks like a repair option, or -1 if none/closed.
    // Lets custom repair NPCs work without a hand-tuned index on English clients.
    public static int FindRepairMenuEntry()
    {
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AfgConstants.AddonNames.SelectIconString, out var addon)) return -1;
        if (!GenericHelpers.IsAddonReady(addon)) return -1;
        var entries = new AddonMaster.SelectIconString(addon).Entries;
        for (var i = 0; i < entries.Length; i++)
        {
            var text = entries[i].Text;
            if (!string.IsNullOrEmpty(text)
                && text.Contains(RepairMenuKeyword, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    public static void HideRepairAgent()
    {
        var agentModule = AgentModule.Instance();
        if (agentModule is null) return;
        var agent = agentModule->GetAgentByInternalId(AgentId.Repair);
        if (agent is null) return;
        agent->Hide();
    }

    public record struct GcMender(uint TerritoryId, Vector3 Position, uint DataId, string Name);

    public static GcMender? GetGrandCompanyMender()
    {
        var state = PlayerState.Instance();
        if (state is null) return null;
        return state->GrandCompany switch
        {
            GrandCompanyId.Maelstrom      => new GcMender(128, new Vector3(17.715698f, 40.200005f, 3.9520264f), 1003251u, "Maelstrom Mender"),
            GrandCompanyId.TwinAdder      => new GcMender(132, new Vector3(24.826416f, -8f, 93.18677f), 1000394u, "Twin Adder Mender"),
            GrandCompanyId.ImmortalFlames => new GcMender(130, new Vector3(32.85266f, 6.999999f, -81.31531f), 1004416u, "Flame Mender"),
            _                             => null,
        };
    }

    public static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindObjectByBaseId(uint baseId)
    {
        foreach (var obj in Svc.Objects)
            if (obj.BaseId == baseId) return obj;
        return null;
    }

    // Nearest matching object to the player (falls back to the first match when the player position is
    // unavailable). Used where several objects can share a BaseId.
    public static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindNearestObjectByBaseId(uint baseId)
    {
        Dalamud.Game.ClientState.Objects.Types.IGameObject? best = null;
        var bestDist = float.MaxValue;
        var player = Svc.Objects.LocalPlayer;
        var playerPos = player?.Position ?? Vector3.Zero;

        foreach (var obj in Svc.Objects)
        {
            if (obj.BaseId != baseId) continue;
            var d = player is null ? 0 : Vector3.Distance(obj.Position, playerPos);
            if (d < bestDist) { best = obj; bestDist = d; }
        }
        return best;
    }

    // The user's chosen NPC if set, else the GC mender.
    public static GcMender? ResolveRepairNpc(out int repairIndex)
    {
        var custom = Plugin.Cfg.PreferredRepairNpc;
        if (custom is not null)
        {
            repairIndex = custom.RepairIndex;
            return new GcMender(custom.TerritoryId,
                new Vector3(custom.X, custom.Y, custom.Z), custom.DataId, custom.Name);
        }
        repairIndex = 0;
        return GetGrandCompanyMender();
    }

    public static RepairNpc? CaptureCurrentTargetAsRepairNpc()
    {
        var targetSystem = TargetSystem.Instance();
        if (targetSystem is null) return null;
        var target = targetSystem->Target;
        if (target is null) return null;

        var baseId = target->BaseId;
        var name = Svc.Data.GetExcelSheet<ENpcResident>()?.GetRowOrDefault(baseId)?.Singular.ToString();
        if (string.IsNullOrEmpty(name)) name = target->NameString;

        var pos = target->Position;
        return new RepairNpc
        {
            TerritoryId = Svc.ClientState.TerritoryType,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            DataId = baseId,
            Name = name,
        };
    }
}
