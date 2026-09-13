using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Game.Ops;
using clib.Extensions;
using clib.TaskSystem;
using clib.Utils;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private async Task ActivateFate(PublicEvent fate)
    {
        var fateId = fate.Id;
        var fateName = fate.Name;
        Status = $"Activating {fateName}";
        Diag($"FATE {fateId} ({fateName}) in Preparation, walking to MotivationNpc {fate.MotivationNpcId:X}");

        try
        {
            var npc = await WaitForStarterNpc(fateId);
            if (npc is null)
            {
                return;
            }

            await WalkToStarterNpc(fateId, fateName, npc.Position);

            for (var attempt = 1; attempt <= NpcInteractAttempts; attempt++)
            {
                if (CancelToken.IsCancellationRequested || !AwaitingNpcStart(fateId))
                {
                    return;
                }
                if (!await PrepareToTalk(fateId))
                {
                    continue;
                }

                var starter = ResolveStarterNpc(fateId);
                if (starter is null)
                {
                    Diag($"Starter NPC for FATE {fateId} is gone or untargetable (attempt {attempt}/{NpcInteractAttempts})");
                    await DelayMs(InteractRetryDelayMs);
                    continue;
                }

                if (!await OpenNpcDialog(fateId, starter, attempt, () => !AwaitingNpcStart(fateId), "activate"))
                {
                    continue;
                }
                if (await DriveStarterDialog(fateId, fateName))
                {
                    return;
                }
            }

            if (!AwaitingNpcStart(fateId))
            {
                return;
            }
            Diag($"FATE {fateId} ({fateName}) did not start after {NpcInteractAttempts} NPC interactions; blacklisting for this session");
            sessionStuckFateIds.Add(fateId);
        }
        catch (Exception ex)
        {
            Diag($"ActivateFate caught: {ex.Message}");
        }
    }

    private static bool AwaitingNpcStart(uint fateId)
        => PublicEvent.GetFateById(fateId) is { } live && FateScanner.AwaitsNpcStart(live);

    private static IGameObject? ResolveStarterNpc(uint fateId)
    {
        if (PublicEvent.GetFateById(fateId) is not { } live)
        {
            return null;
        }
        var npc = live.MotivationNpc;
        return npc is { IsTargetable: true } ? npc : null;
    }

    private async Task<IGameObject?> WaitForStarterNpc(uint fateId)
    {
        var deadline = Environment.TickCount64 + NpcSpawnTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested || !AwaitingNpcStart(fateId))
            {
                return null;
            }
            var npc = ResolveStarterNpc(fateId);
            if (npc is not null)
            {
                return npc;
            }
            await NextFrame(30);
        }

        Diag($"NPC for FATE {fateId} never spawned within {NpcSpawnTimeoutMs / 1000}s; blacklisting for session");
        sessionStuckFateIds.Add(fateId);
        return null;
    }

    private async Task WalkToStarterNpc(uint fateId, string fateName, Vector3 npcPos)
    {
        var label = $"Activating {fateName}";
        await WalkWithRetries(
            () => new MoveOp(o => o.Move(zone.TerritoryId, npcPos,
                MovementConfig.InteractRange,
                allowTeleportIfFaster: false,
                stopCondition: () => { Status = label; return !AwaitingNpcStart(fateId); },
                allowAethernetWithinTerritory: false)),
            ActivateMoveWatchdogMs, $"activate-move-{fateId}",
            () => !AwaitingNpcStart(fateId) || WithinReach(npcPos, InteractRangeMeters));
    }

    private async Task<bool> PrepareToTalk(uint fateId)
    {
        if (Svc.Condition[ConditionFlag.InCombat])
        {
            await ClearBlockingCombat();
        }
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await DismountViaOp($"dismount-activate-{fateId}");
        }
        if (await WaitUntilTimed(NpcInteraction.PlayerReady, InteractReadyTimeoutMs, $"activate-ready-{fateId}", checkFrames: 2))
        {
            return true;
        }

        Diag($"Cannot talk to the NPC for FATE {fateId} yet ({NpcInteraction.DescribeBlockers()}); retrying");
        return false;
    }

    // Shared by FATE activation and Collect hand-ins; abandon() reports the reason to talk has gone away.
    private async Task<bool> OpenNpcDialog(uint fateId, IGameObject npc, int attempt, Func<bool> abandon, string scope)
    {
        if (!npc.IsInInteractRange())
        {
            var npcPos = npc.Position;
            var approachScope = $"{scope}-approach-{fateId}";
            var approach = new MoveOp(o => o.MoveInZone(npcPos, MovementConfig.InteractRange.WithTolerance(ActivateApproachToleranceMeters),
                () => npc.IsInInteractRange() || abandon()));
            await RunCancellable(approach, ActivateApproachWatchdogMs, approachScope, StuckDetector.MoveStallAbort(approachScope));
            if (!npc.IsInInteractRange())
            {
                Diag($"Could not get within interact range of the NPC for FATE {fateId} (attempt {attempt}/{NpcInteractAttempts})");
                return false;
            }
        }

        NpcInteraction.Target(npc);
        await WaitUntilTimed(() => NpcInteraction.IsTargeted(npc), TargetSettleTimeoutMs, $"{scope}-target-{fateId}", checkFrames: 1);

        var result = NpcInteraction.Interact(npc);
        if (result == 0)
        {
            Diag($"The game rejected the interaction with NPC {npc.GameObjectId:X} for FATE {fateId} (attempt {attempt}/{NpcInteractAttempts}, blockers: {NpcInteraction.DescribeBlockers()}); retrying");
            await DelayMs(InteractRetryDelayMs);
            return false;
        }

        var opened = await WaitUntilTimed(() => NpcInteraction.DialogOpen() || abandon(),
            DialogOpenTimeoutMs, $"{scope}-dialog-{fateId}", checkFrames: 2);
        if (opened)
        {
            Trace($"NPC dialog for FATE {fateId} opened (interaction result {result}, attempt {attempt})");
            return true;
        }

        Diag($"Interaction with NPC {npc.GameObjectId:X} for FATE {fateId} opened no dialog (result {result}, attempt {attempt}/{NpcInteractAttempts}, blockers: {NpcInteraction.DescribeBlockers()}); retrying");
        await DelayMs(InteractRetryDelayMs);
        return false;
    }

    private async Task<bool> DriveStarterDialog(uint fateId, string fateName)
    {
        Status = $"Talking to the NPC for {fateName}";
        var deadline = Environment.TickCount64 + NpcDialogTimeoutMs;
        var lastDialogSeenMs = Environment.TickCount64;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return true;
            }
            if (PublicEvent.GetFateById(fateId) is not { } live)
            {
                return true;
            }
            if (!FateScanner.AwaitsNpcStart(live))
            {
                if (live.State == FateState.Running)
                {
                    Diag($"FATE {fateId} ({fateName}) started via its NPC");
                }
                await DismissLingeringDialog();
                return true;
            }

            var dialogPresent = NpcInteraction.DriveDialog() || NpcInteraction.DialogOpen();
            if (dialogPresent)
            {
                lastDialogSeenMs = Environment.TickCount64;
            }
            else if (Environment.TickCount64 - lastDialogSeenMs > DialogClosedGraceMs)
            {
                Diag($"NPC dialog for FATE {fateId} closed without starting it; retrying");
                return false;
            }
            await NextFrame(2);
        }

        Diag($"NPC dialog for FATE {fateId} did not start it within {NpcDialogTimeoutMs / 1000}s; retrying");
        return false;
    }

    private async Task DismissLingeringDialog()
    {
        var deadline = Environment.TickCount64 + DialogDismissTimeoutMs;
        while (Environment.TickCount64 < deadline && NpcInteraction.DialogOpen())
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }
            if (!NpcInteraction.DriveDialog())
            {
                NpcInteraction.CancelRequestDialog();
            }
            await NextFrame(2);
        }
    }
}
