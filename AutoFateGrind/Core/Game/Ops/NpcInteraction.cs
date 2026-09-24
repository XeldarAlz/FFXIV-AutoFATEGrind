using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Text;
using PlayerHelpers = ECommons.GameHelpers.Player;

namespace AutoFateGrind.Core.Game.Ops;

internal static unsafe class NpcInteraction
{
    private readonly record struct BlockingFlag(ConditionFlag Flag, string Label);

    private static readonly BlockingFlag[] BlockingFlags =
    {
        new(ConditionFlag.Mounted, "mounted"),
        new(ConditionFlag.MountOrOrnamentTransition, "mount transition"),
        new(ConditionFlag.Jumping, "jumping"),
        new(ConditionFlag.Jumping61, "airborne"),
        new(ConditionFlag.InCombat, "in combat"),
        new(ConditionFlag.Casting, "casting"),
    };

    public static bool PlayerReady()
    {
        if (Svc.Objects.LocalPlayer is null)
        {
            return false;
        }

        for (var flagIndex = 0; flagIndex < BlockingFlags.Length; flagIndex++)
        {
            if (Svc.Condition[BlockingFlags[flagIndex].Flag])
            {
                return false;
            }
        }

        return !PlayerHelpers.IsAnimationLocked && !GenericHelpers.IsOccupied() && PlayerHelpers.Interactable;
    }

    public static string DescribeBlockers()
    {
        var builder = new StringBuilder();
        for (var flagIndex = 0; flagIndex < BlockingFlags.Length; flagIndex++)
        {
            if (Svc.Condition[BlockingFlags[flagIndex].Flag])
            {
                AppendBlocker(builder, BlockingFlags[flagIndex].Label);
            }
        }

        if (PlayerHelpers.IsAnimationLocked)
        {
            AppendBlocker(builder, "animation lock");
        }
        if (GenericHelpers.IsOccupied())
        {
            AppendBlocker(builder, "occupied");
        }
        if (!PlayerHelpers.Interactable)
        {
            AppendBlocker(builder, "player not interactable");
        }

        return builder.Length == 0 ? "no blocker detected" : builder.ToString();
    }

    private static void AppendBlocker(StringBuilder builder, string label)
    {
        if (builder.Length > 0)
        {
            builder.Append(", ");
        }
        builder.Append(label);
    }

    public static bool IsTargeted(IGameObject npc)
        => Svc.Targets.Target?.GameObjectId == npc.GameObjectId;

    public static void Target(IGameObject npc)
        => Svc.Targets.Target = npc;

    public static ulong Interact(IGameObject npc)
        => TargetSystem.Instance()->InteractWithObject(npc.Struct(), false);

    public static bool DialogOpen()
        => Svc.Condition[ConditionFlag.OccupiedInQuestEvent]
        || Svc.Condition[ConditionFlag.OccupiedInEvent]
        || DialogAddonOpen();

    public static bool DialogAddonOpen()
        => AddonReady(AfgConstants.AddonNames.Talk, out _)
        || AddonReady(AfgConstants.AddonNames.SelectYesno, out _)
        || AddonReady(AfgConstants.AddonNames.SelectString, out _)
        || RequestDialogOpen();

    public static bool RequestDialogOpen()
        => AddonReady(AfgConstants.AddonNames.Request, out _);

    public static bool AddonOpen(string name)
        => AddonReady(name, out _);

    // Slot 0 of the request window and the icon-menu entry that moves the held stack into it; the same
    // callback pair TextAdvance fires, so the two never disagree on how the window gets filled.
    private const int RequestFirstSlot = 0;
    private const int RequestOpenSlotMenuEvent = 2;
    private const int RequestMenuHandOverEntry = 1021003;

    // Hands over a filled request; with fillOurselves it also fills the slot when nothing else has.
    public static bool DriveRequestDialog(bool fillOurselves)
    {
        if (!AddonReady(AfgConstants.AddonNames.Request, out var request))
        {
            return false;
        }

        var master = new AddonMaster.Request(request);
        if (master.IsHandOverEnabled)
        {
            if (EzThrottler.Throttle(AfgConstants.ThrottleKeys.RequestHandOver, AfgConstants.AddonInteractThrottleMs))
            {
                master.HandOver();
            }
            return true;
        }
        if (!fillOurselves)
        {
            return true;
        }

        if (AddonReady(AfgConstants.AddonNames.ContextIconMenu, out var menu) && menu->IsVisible)
        {
            if (EzThrottler.Throttle(AfgConstants.ThrottleKeys.RequestFillPick, AfgConstants.AddonInteractThrottleMs))
            {
                Callback.Fire(menu, false, 0, 0, RequestMenuHandOverEntry, 0, 0);
            }
            return true;
        }

        if (EzThrottler.Throttle(AfgConstants.ThrottleKeys.RequestFillOpen, AfgConstants.AddonInteractThrottleMs))
        {
            Callback.Fire(request, false, RequestOpenSlotMenuEvent, RequestFirstSlot, 0, 0);
        }
        return true;
    }

    public static bool CancelRequestDialog()
    {
        if (!AddonReady(AfgConstants.AddonNames.Request, out var request))
        {
            return false;
        }
        if (EzThrottler.Throttle(AfgConstants.ThrottleKeys.RequestHandOver, AfgConstants.AddonInteractThrottleMs))
        {
            new AddonMaster.Request(request).Cancel();
        }
        return true;
    }

    public static bool DriveDialog()
    {
        if (AddonReady(AfgConstants.AddonNames.SelectString, out var selectString))
        {
            if (EzThrottler.Throttle(AfgConstants.ThrottleKeys.NpcSelectString, AfgConstants.AddonInteractThrottleMs))
            {
                SelectFirstEntry(selectString);
            }
            return true;
        }

        if (AddonReady(AfgConstants.AddonNames.SelectYesno, out var selectYesno))
        {
            if (EzThrottler.Throttle(AfgConstants.ThrottleKeys.NpcSelectYesno, AfgConstants.AddonInteractThrottleMs))
            {
                new AddonMaster.SelectYesno(selectYesno).Yes();
            }
            return true;
        }

        if (AddonReady(AfgConstants.AddonNames.Talk, out var talk))
        {
            if (EzThrottler.Throttle(AfgConstants.ThrottleKeys.NpcTalk, AfgConstants.AddonInteractThrottleMs))
            {
                new AddonMaster.Talk(talk).Click();
            }
            return true;
        }

        return false;
    }

    private static void SelectFirstEntry(AtkUnitBase* selectString)
    {
        var master = new AddonMaster.SelectString(selectString);
        if (master.EntryCount == 0)
        {
            return;
        }
        var first = master.Entries[0];
        RunLog.Debug($"SelectString: choosing '{first.Text}'");
        first.Select();
    }

    private static bool AddonReady(string name, out AtkUnitBase* addon)
        => GenericHelpers.TryGetAddonByName(name, out addon) && GenericHelpers.IsAddonReady(addon);
}
