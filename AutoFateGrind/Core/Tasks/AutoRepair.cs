using AutoFateGrind.Core.Game;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed class AutoRepair : AutoCommon
{
    private const int TeleportWatchdogMs   = 60_000;
    private const int MoveWatchdogMs       = 120_000;
    private const int InteractWaitMs       = 15_000;
    private const int RepairAddonWaitMs    = 10_000;
    private const int YesnoWaitMs          = 5_000;
    private const int RepairCompleteWaitMs = 30_000;
    private const int DismountWatchdogMs   = 30_000;

    protected override async Task Execute()
    {
        var cfg = Plugin.Cfg;
        ErrorIf(!cfg.AutoRepair, "Auto-repair is disabled.");

        var before = RepairOps.LowestEquippedConditionPct();
        Diag($"AutoRepair start: lowest condition {before:F1}%, threshold {cfg.AutoRepairThresholdPct}%, mode {cfg.RepairMode}");
        Svc.Chat.Print($"[AFG] Repairing gear (lowest at {before:F0}%).");

        var allowSelf = cfg.RepairMode is RepairMode.SelfThenNpc or RepairMode.SelfOnly;
        var allowNpc  = cfg.RepairMode is RepairMode.SelfThenNpc or RepairMode.NpcOnly;

        if (allowSelf && RepairOps.HasDarkMatterForAllEquipped())
        {
            if (await TrySelfRepair())
            {
                ReportDone(before);
                return;
            }
            Diag("Self-repair did not bring condition above threshold; falling through.");
        }
        else if (allowSelf)
        {
            Diag("Skipping self-repair: missing required Dark Matter for at least one equipped item.");
        }

        if (!allowNpc)
        {
            Svc.Chat.PrintError("[AFG] Repair: no Dark Matter and NPC fallback disabled; skipping.");
            return;
        }

        await RepairAtGcMender();
        ReportDone(before);
    }

    private void ReportDone(float before)
    {
        var after = RepairOps.LowestEquippedConditionPct();
        Svc.Chat.Print($"[AFG] Repair done. Lowest condition {before:F0}% → {after:F0}%.");
    }

    // ---------- Self-repair branch ----------

    private async Task<bool> TrySelfRepair()
    {
        if (Svc.Condition[ConditionFlag.Mounted])
            await RunCancellable(new MoveOp(o => o.DismountNow()), DismountWatchdogMs, "repair-dismount");

        Status = "Opening Repair";
        Diag("Triggering Repair general action");
        if (!RepairOps.TriggerRepairGeneralAction())
        {
            Diag("UseAction(GeneralAction, Repair) call returned without dispatching.");
            return false;
        }

        if (!await WaitUntilTimed(RepairOps.RepairAddonOpen, RepairAddonWaitMs, "self-wait-repair-addon"))
        {
            Diag("Repair addon never opened after Repair general action.");
            return false;
        }

        return await DriveRepairAddon();
    }

    // ---------- NPC branch ----------

    private async Task RepairAtGcMender()
    {
        var mender = RepairOps.ResolveRepairNpc(out var repairIndex);
        ErrorIf(mender is null,
            "Auto-repair NPC fallback needs a Grand Company affiliation or a custom repair NPC. Join a Grand Company, set a custom repair NPC, or enable Self-only repair.");

        var m = mender!.Value;
        Diag($"NPC repair: heading to {m.Name} (territory {m.TerritoryId}, DataId {m.DataId})");

        if (Svc.ClientState.TerritoryType != m.TerritoryId)
        {
            var reached = false;
            await RunWithStatusPinned($"Teleporting to {m.Name}",
                async () => reached = await TeleportToTerritory(m.TerritoryId, m.Position, "repair-teleport", TeleportWatchdogMs));
            ErrorIf(!reached, $"Could not reach {m.Name}'s zone (still in {Svc.ClientState.TerritoryType}); aborting repair.");
        }

        await RunWithStatusPinned($"Walking to {m.Name}", async () =>
        {
            var move = new MoveOp(o => o.Move(m.TerritoryId, m.Position,
                MovementConfig.Everything.WithTolerance(4f),
                allowTeleportIfFaster: false,
                stopCondition: null,
                allowAethernetWithinTerritory: true));
            await RunCancellable(move, MoveWatchdogMs, "repair-walk");
        });

        if (Svc.Condition[ConditionFlag.Mounted])
            await RunCancellable(new MoveOp(o => o.DismountNow()), DismountWatchdogMs, "repair-dismount-mender");

        var npc = RepairOps.FindObjectByBaseId(m.DataId);
        ErrorIf(npc is null, $"Could not find {m.Name} (BaseId {m.DataId}) near {m.Position}.");

        Status = $"Talking to {m.Name}";
        Diag($"Interacting with {m.Name}");
        var interact = new MoveOp(o => o.Interact(npc!, waitUntil: null, skip: UiSkipOptions.Talk));
        await RunCancellable(interact, InteractWaitMs, "repair-interact");

        // GC menders open the Repair window directly; other repair NPCs first present a talk menu
        // (SelectIconString) whose repair entry we pick before the window appears.
        ErrorIf(!await WaitUntilTimed(
                () => RepairOps.RepairAddonOpen() || RepairOps.SelectIconStringOpen(),
                RepairAddonWaitMs, "npc-wait-menu-or-repair"),
            $"{m.Name} did not respond with a repair menu within {RepairAddonWaitMs / 1000}s.");

        if (!RepairOps.RepairAddonOpen() && RepairOps.SelectIconStringOpen())
        {
            var detected = RepairOps.FindRepairMenuEntry();
            var index = detected >= 0 ? detected : repairIndex;
            Diag($"Talk menu open; selecting repair entry {index} (auto-detected: {detected >= 0}).");
            ErrorIf(!RepairOps.ClickSelectIconString(index),
                $"Could not select repair option {index} in {m.Name}'s menu.");

            ErrorIf(!await WaitUntilTimed(RepairOps.RepairAddonOpen, RepairAddonWaitMs, "npc-wait-repair-after-menu"),
                $"{m.Name} did not open the Repair window after menu selection.");
        }

        await DriveRepairAddon();
    }

    // ---------- Shared addon driver ----------

    // Walks the Repair → SelectYesno → completion sequence common to self-repair and NPC repair.
    private async Task<bool> DriveRepairAddon()
    {
        Status = "Repairing gear";

        // Repair UI is open. If RepairAll is disabled (no DM / no broken items), the click is a no-op
        // and the SelectYesno will never appear — DriveRepairAddon will fail the Yesno wait below and
        // the caller routes to the NPC fallback (or surfaces the error if NPC-only/SelfOnly).
        if (!RepairOps.ClickRepairAll())
        {
            Diag("ClickRepairAll returned false; addon disappeared between checks.");
            RepairOps.HideRepairAgent();
            return false;
        }

        if (!await WaitUntilTimed(RepairOps.SelectYesnoOpen, YesnoWaitMs, "wait-repair-yesno"))
        {
            Diag("SelectYesno never appeared — RepairAll button likely disabled (no materials/affordability).");
            RepairOps.HideRepairAgent();
            return false;
        }

        if (!RepairOps.ClickSelectYesno())
        {
            Diag("ClickSelectYesno returned false; addon disappeared between checks.");
            RepairOps.HideRepairAgent();
            return false;
        }

        // Repair fires Occupied39 during the animation; condition then jumps to 30000 across all slots.
        var deadline = Environment.TickCount64 + RepairCompleteWaitMs;
        var sawAnim = false;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return false;
            if (Svc.Condition[ConditionFlag.Occupied39]) { sawAnim = true; }
            else if (sawAnim) break;
            // Belt-and-braces: if the addon closed and condition is high, we're done.
            if (!RepairOps.RepairAddonOpen() && RepairOps.LowestEquippedConditionPct() > 95f) break;
            await NextFrame(60);
        }

        RepairOps.HideRepairAgent();
        return RepairOps.LowestEquippedConditionPct() > Plugin.Cfg.AutoRepairThresholdPct + 5;
    }
}
