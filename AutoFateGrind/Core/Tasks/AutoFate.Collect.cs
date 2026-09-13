using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Ipc;
using clib.Extensions;
using clib.TaskSystem;
using clib.Utils;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private const string FateUtilsModule = "BossMod.Autorotation.MiscAI.FateUtils";
    private const string FateUtilsCollectTrack = "Collect";
    private const string FateUtilsDisabledOption = "Disabled";
    private const string AutoTargetModule = "BossMod.Autorotation.MiscAI.AutoTarget";
    private const string AutoTargetGeneralTrack = "General";
    private const string AutoTargetPassiveOption = "Passive";

    private const int HandInWalkWatchdogMs = 40_000;
    private const int HandInCombatClearMs = 30_000;
    private const int HandInDialogTimeoutMs = 20_000;
    private const int HandInRetryBackoffMs = 8_000;
    private const int HandInNpcMissingBackoffMs = 3_000;
    private const int HandInRequestFillGraceMs = 2_000;
    private const int MaxHandInFailuresPerFate = 3;
    // A finished Collect FATE keeps its row (the hand-in window) open for about a minute before the reward
    // lands; leaving the ring earlier forfeits it (issue #64). The FATE timer sizes the hold, bounded both ways.
    private const int CollectRewardHoldMinMs = 90_000;
    private const int CollectRewardHoldMaxMs = 240_000;
    private const int CollectRewardHoldSlackMs = 15_000;

    private readonly record struct FateSpawnKey(uint FateId, int StartEpoch);

    private FateSpawnKey lastCompletedSpawn;
    private FateSpawnKey handInSpawn;
    private bool afgHandInOwner;
    private int  handInFailures;
    private long handInNextAttemptMs;
    private bool handInNpcMissingLogged;

    private static int HandInBatch => Math.Max(1, Plugin.Cfg.CollectHandInBatch);

    private void BeginCollectFate(FateSpawnKey spawn, string fateName)
    {
        EnableTextAdvanceForCollect();
        if (handInSpawn == spawn)
        {
            return;
        }
        handInSpawn = spawn;
        handInFailures = 0;
        handInNextAttemptMs = 0;
        handInNpcMissingLogged = false;
        afgHandInOwner = Plugin.Cfg.CollectHandInEnabled;

        Diag(afgHandInOwner
            ? $"Collect FATE {spawn.FateId} ({fateName}): AFG hands in every {HandInBatch} of item {FateItems.TurnInItemId(spawn.FateId)}; BossMod's own 10-item hand-in stays as the backstop"
            : $"Collect FATE {spawn.FateId} ({fateName}): hand-ins left to BossMod's FATE helper (AFG hand-in is off in settings)");
    }

    private static bool FateAlive(uint fateId)
        => PublicEvent.GetFateById(fateId) is { } live && live.State is not (FateState.Ended or FateState.Failed);

    private static IGameObject? ResolveObjectiveNpc(uint fateId)
    {
        if (PublicEvent.GetFateById(fateId) is not { } live)
        {
            return null;
        }
        var npc = live.ObjectiveNpc;
        return npc is { IsTargetable: true } ? npc : null;
    }

    // Returns true when a trip was attempted so the caller can restart its stall clocks.
    private async Task<bool> MaybeHandInCollectItems(uint fateId, string fateName, string preset)
    {
        if (!afgHandInOwner || Environment.TickCount64 < handInNextAttemptMs)
        {
            return false;
        }
        var itemId = FateItems.TurnInItemId(fateId);
        var held = FateItems.HeldCount(itemId);
        if (itemId == 0 || held < HandInBatch)
        {
            return false;
        }
        return await HandInHeldItems(fateId, fateName, preset, itemId, held);
    }

    private async Task HandInLeftovers(uint fateId, string fateName, string preset)
    {
        if (!afgHandInOwner)
        {
            return;
        }
        var itemId = FateItems.TurnInItemId(fateId);
        var held = FateItems.HeldCount(itemId);
        if (itemId == 0 || held <= 0)
        {
            return;
        }
        Diag($"FATE {fateId} ({fateName}) is at 100% with {held} item(s) still held; turning them in during the hand-in window");
        await HandInHeldItems(fateId, fateName, preset, itemId, held);
    }

    private async Task<bool> HandInHeldItems(uint fateId, string fateName, string preset, uint itemId, int held)
    {
        if (ResolveObjectiveNpc(fateId) is null)
        {
            if (!handInNpcMissingLogged)
            {
                handInNpcMissingLogged = true;
                Diag($"FATE {fateId} ({fateName}): holding {held} item(s) but its hand-in NPC is not loaded or targetable yet; retrying");
            }
            handInNextAttemptMs = Environment.TickCount64 + HandInNpcMissingBackoffMs;
            return false;
        }
        handInNpcMissingLogged = false;

        if (await TryHandIn(fateId, fateName, preset, itemId, held))
        {
            handInFailures = 0;
            return true;
        }
        if (CancelToken.IsCancellationRequested || !FateAlive(fateId))
        {
            return true;
        }

        handInFailures++;
        handInNextAttemptMs = Environment.TickCount64 + HandInRetryBackoffMs;
        if (handInFailures < MaxHandInFailuresPerFate)
        {
            Diag($"Hand-in for FATE {fateId} did not go through (attempt {handInFailures}/{MaxHandInFailuresPerFate}); retrying in {HandInRetryBackoffMs / 1000}s");
            return true;
        }
        afgHandInOwner = false;
        Diag($"Hand-in for FATE {fateId} failed {handInFailures} times; leaving the rest of this FATE's turn-ins to BossMod's 10-item hand-in");
        return true;
    }

    private async Task<bool> TryHandIn(uint fateId, string fateName, string preset, uint itemId, int held)
    {
        Status = $"Handing in {held} item(s) for {fateName}";
        Diag($"FATE {fateId} ({fateName}): walking {held} item(s) to the hand-in NPC");

        // Mirror BossMod's own hand-in trip: no new pulls, no node pickups, and no movement of its own.
        var parkedMovement = ParkBossModMovement(preset);
        BossModIPC.Instance.AddTransientStrategy(preset, AutoTargetModule, AutoTargetGeneralTrack, AutoTargetPassiveOption);
        var parkedPickup = BossModIPC.Instance.AddTransientStrategy(preset, FateUtilsModule, FateUtilsCollectTrack, FateUtilsDisabledOption);
        try
        {
            for (var attempt = 1; attempt <= NpcInteractAttempts; attempt++)
            {
                if (CancelToken.IsCancellationRequested || !FateAlive(fateId))
                {
                    return false;
                }
                if (ResolveObjectiveNpc(fateId) is not { } npc)
                {
                    return false;
                }
                if (!await ApproachHandInNpc(fateId, npc, attempt))
                {
                    continue;
                }
                if (!await ReadyToHandIn(fateId, preset))
                {
                    continue;
                }
                if (!await OpenNpcDialog(fateId, npc, attempt, () => !FateAlive(fateId), "handin"))
                {
                    continue;
                }
                if (await DriveHandInDialog(fateId, fateName, itemId, held))
                {
                    return true;
                }
            }
            return false;
        }
        finally
        {
            if (parkedPickup)
            {
                BossModIPC.Instance.ClearTransientStrategy(preset, FateUtilsModule, FateUtilsCollectTrack);
            }
            BossModIPC.Instance.ClearTransientStrategy(preset, AutoTargetModule, AutoTargetGeneralTrack);
            if (parkedMovement)
            {
                ResumeBossModMovement(preset);
            }
        }
    }

    private async Task<bool> ApproachHandInNpc(uint fateId, IGameObject npc, int attempt)
    {
        if (npc.IsInInteractRange())
        {
            return true;
        }
        var npcPos = npc.Position;
        var scope = $"handin-walk-{fateId}#{attempt}";
        var walk = new MoveOp(o => o.MoveInZone(npcPos, MovementConfig.InteractRange, () => npc.IsInInteractRange() || !FateAlive(fateId)));
        await RunCancellable(walk, HandInWalkWatchdogMs, scope, StuckDetector.MoveStallAbort(scope));
        if (walk.Fault is { } fault)
        {
            Diag($"{scope} faulted: {fault.Message}");
        }
        if (npc.IsInInteractRange())
        {
            return true;
        }
        Diag($"Could not reach the hand-in NPC for FATE {fateId} (attempt {attempt}/{NpcInteractAttempts})");
        return false;
    }

    // NPC events refuse to open in combat, and with targeting parked nothing fights back; hand the rotation
    // its targets again and stay put until the chasers are dead.
    private async Task<bool> ReadyToHandIn(uint fateId, string preset)
    {
        if (Svc.Condition[ConditionFlag.InCombat])
        {
            await FightFreeAtHandInNpc(fateId, preset);
        }
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await DismountViaOp($"dismount-handin-{fateId}");
        }
        if (await WaitUntilTimed(NpcInteraction.PlayerReady, InteractReadyTimeoutMs, $"handin-ready-{fateId}", checkFrames: 2))
        {
            return true;
        }
        Diag($"Cannot talk to the hand-in NPC for FATE {fateId} yet ({NpcInteraction.DescribeBlockers()}); retrying");
        return false;
    }

    private async Task FightFreeAtHandInNpc(uint fateId, string preset)
    {
        Status = "Clearing aggro before handing in";
        BossModIPC.Instance.ClearTransientStrategy(preset, AutoTargetModule, AutoTargetGeneralTrack);
        var deadline = Environment.TickCount64 + HandInCombatClearMs;
        try
        {
            while (Environment.TickCount64 < deadline)
            {
                if (CancelToken.IsCancellationRequested || IsPlayerKO() || !FateAlive(fateId))
                {
                    return;
                }
                if (!Svc.Condition[ConditionFlag.InCombat])
                {
                    return;
                }
                AssertPresetActive(preset);
                await NextFrame(30);
            }
            Diag($"Still in combat at the hand-in NPC after {HandInCombatClearMs / 1000}s; retrying the hand-in later");
        }
        finally
        {
            BossModIPC.Instance.AddTransientStrategy(preset, AutoTargetModule, AutoTargetGeneralTrack, AutoTargetPassiveOption);
        }
    }

    private async Task<bool> DriveHandInDialog(uint fateId, string fateName, uint itemId, int heldBefore)
    {
        Status = $"Handing in items for {fateName}";
        var deadline = Environment.TickCount64 + HandInDialogTimeoutMs;
        var lastDialogSeenMs = Environment.TickCount64;
        var requestSeenAtMs = 0L;
        var handedIn = false;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return handedIn;
            }
            var now = Environment.TickCount64;
            var held = FateItems.HeldCount(itemId);
            if (!handedIn && held < heldBefore)
            {
                handedIn = true;
                Diag($"Handed in {heldBefore - held} item(s) for FATE {fateId} ({fateName})");
            }

            if (!NpcInteraction.RequestDialogOpen())
            {
                requestSeenAtMs = 0;
            }
            else if (requestSeenAtMs == 0)
            {
                requestSeenAtMs = now;
            }
            // TextAdvance fills the request window when armed; past the grace period (or without it) we fill it.
            var fillOurselves = !textAdvanceArmed || (requestSeenAtMs != 0 && now - requestSeenAtMs >= HandInRequestFillGraceMs);

            var dialogPresent = NpcInteraction.DriveRequestDialog(fillOurselves) || NpcInteraction.DriveDialog() || NpcInteraction.DialogOpen();
            if (dialogPresent)
            {
                lastDialogSeenMs = now;
            }
            else if (handedIn)
            {
                return true;
            }
            else if (now - lastDialogSeenMs > DialogClosedGraceMs)
            {
                Diag($"Hand-in dialog for FATE {fateId} closed without taking the items; retrying");
                return false;
            }
            await NextFrame(2);
        }

        Diag($"Hand-in dialog for FATE {fateId} did not finish within {HandInDialogTimeoutMs / 1000}s (handed in: {handedIn})");
        await DismissLingeringDialog();
        return handedIn;
    }

    private async Task HoldForCollectRewards(uint fateId, string fateName, string preset)
    {
        if (PublicEvent.GetFateById(fateId) is not { } finished)
        {
            return;
        }
        var startedAtMs = Environment.TickCount64;
        var timerMs = (long)(Math.Max(0f, finished.TimeRemaining) * 1000f);
        var holdMs = Math.Clamp(timerMs + CollectRewardHoldSlackMs, CollectRewardHoldMinMs, CollectRewardHoldMaxMs);
        var deadline = startedAtMs + holdMs;
        Diag($"Collect FATE {fateId} ({fateName}) reached 100%; holding in the ring for its reward (FATE timer {timerMs / 1000}s, hold cap {holdMs / 1000}s)");

        var leftoversTried = false;
        while (!CancelToken.IsCancellationRequested)
        {
            var now = Environment.TickCount64;
            var live = PublicEvent.GetFateById(fateId);
            if (live is null)
            {
                Diag($"Collect FATE {fateId} row cleared {(now - startedAtMs) / 1000}s into the hold; reward delivered, moving on");
                return;
            }
            if (live.State is FateState.Ended or FateState.Failed)
            {
                Diag($"Collect FATE {fateId} is {live.State} {(now - startedAtMs) / 1000}s into the hold; moving on");
                return;
            }
            if (IsPlayerKO() || Svc.ClientState.TerritoryType != zone.TerritoryId)
            {
                return;
            }
            if (now >= deadline)
            {
                Diag($"Collect reward hold for FATE {fateId} hit its {holdMs / 1000}s cap with the row still up; moving on");
                return;
            }

            Status = $"Waiting for {fateName} rewards ({(deadline - now) / 1000 + 1}s)";
            if (Svc.Condition[ConditionFlag.Mounted])
            {
                BossModIPC.Instance.ClearActive();
                await DismountViaOp($"dismount-hold-{fateId}");
            }
            AssertPresetActive(preset);

            if (!leftoversTried)
            {
                leftoversTried = true;
                await HandInLeftovers(fateId, fateName, preset);
            }
            await NextFrame(30);
        }
    }
}
