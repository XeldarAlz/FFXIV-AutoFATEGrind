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
    // A finished Collect FATE keeps its row (the hand-in window) open for about a minute and pays out when it
    // clears. Leaving the ring is fine; leaving the zone forfeits the reward (issue #64). The FATE timer sizes
    // how long the zone is held for it, bounded both ways.
    private const int CollectRewardWatchMinMs = 90_000;
    private const int CollectRewardWatchMaxMs = 240_000;
    private const int CollectRewardWatchSlackMs = 15_000;

    private readonly record struct FateSpawnKey(uint FateId, int StartEpoch);

    private FateSpawnKey lastCompletedSpawn;
    private FateSpawnKey handInSpawn;
    private FateSpawnKey pendingRewardSpawn;
    private string pendingRewardName = "";
    private long pendingRewardSinceMs;
    private long pendingRewardDeadlineMs;
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

    private bool CollectRewardPending => pendingRewardSpawn != default;

    private async Task WrapUpCollectFate(uint fateId, string fateName, string preset)
    {
        await HandInLeftovers(fateId, fateName, preset);
        if (PublicEvent.GetFateById(fateId) is { } live && live.State is not (FateState.Ended or FateState.Failed))
        {
            TrackCollectReward(live);
        }
    }

    private void TrackCollectReward(PublicEvent live)
    {
        var spawn = new FateSpawnKey(live.Id, live.StartTimeEpoch);
        if (pendingRewardSpawn == spawn)
        {
            return;
        }
        var fateName = live.Name;
        var now = Environment.TickCount64;
        var timerMs = (long)(Math.Max(0f, live.TimeRemaining) * 1000f);
        var watchMs = Math.Clamp(timerMs + CollectRewardWatchSlackMs, CollectRewardWatchMinMs, CollectRewardWatchMaxMs);
        pendingRewardSpawn = spawn;
        pendingRewardName = fateName;
        pendingRewardSinceMs = now;
        pendingRewardDeadlineMs = now + watchMs;
        Diag($"Collect FATE {live.Id} ({fateName}) is at 100% with {FateItems.HandedInCount(live)} item(s) handed in; moving on inside {zone.Name} and collecting the reward when its row clears (FATE timer {timerMs / 1000}s, zone held for up to {watchMs / 1000}s)");
    }

    private void RefreshPendingCollectReward()
    {
        if (!CollectRewardPending)
        {
            return;
        }
        var fateId = pendingRewardSpawn.FateId;
        var waitedSec = (Environment.TickCount64 - pendingRewardSinceMs) / 1000;
        var live = PublicEvent.GetFateById(fateId);
        var outcome = live is null ? "row cleared"
            : live.StartTimeEpoch != pendingRewardSpawn.StartEpoch ? "respawned"
            : live.State is FateState.Ended or FateState.Failed ? live.State.ToString()
            : null;
        if (outcome is not null)
        {
            session.UpdateGemstones();
            session.UpdateExp();
            Diag($"Collect FATE {fateId} ({pendingRewardName}) {outcome} {waitedSec}s after leaving it; reward settled (wallet {session.GemstoneCurrent}g)");
            ClearPendingCollectReward();
            return;
        }
        if (Svc.ClientState.TerritoryType != zone.TerritoryId)
        {
            Diag($"Left {zone.Name} with Collect FATE {fateId} ({pendingRewardName}) still open; its reward is forfeit");
            ClearPendingCollectReward();
            return;
        }
        if (Environment.TickCount64 >= pendingRewardDeadlineMs)
        {
            Diag($"Collect FATE {fateId} ({pendingRewardName}) row still up {waitedSec}s after leaving it; no longer holding {zone.Name} for it");
            ClearPendingCollectReward();
        }
    }

    private void ClearPendingCollectReward()
    {
        pendingRewardSpawn = default;
        pendingRewardName = "";
    }

    private async Task TickCollectRewardWait()
    {
        var remainingSec = Math.Max(0L, pendingRewardDeadlineMs - Environment.TickCount64) / 1000 + 1;
        Status = $"Waiting for {pendingRewardName} rewards ({remainingSec}s)";
        // Stray mobs may still be on us; keep the rotation up while they are (cleared on state exit).
        if (Svc.Condition[ConditionFlag.InCombat])
        {
            var preset = Plugin.Cfg.CombatPresetName;
            EnsureCombatPreset(preset);
            AssertPresetActive(preset);
        }
        await NextFrame(60);
    }

    // Hand-offs teleport out of the zone; wherever the character stands in it, the reward still lands when the row clears.
    private async Task HoldForCollectReward()
    {
        if (!CollectRewardPending)
        {
            return;
        }
        Diag($"Hand-off queued while Collect FATE {pendingRewardSpawn.FateId} ({pendingRewardName}) still owes its reward; holding in {zone.Name} until it lands");
        try
        {
            while (!CancelToken.IsCancellationRequested && !IsPlayerKO())
            {
                RefreshPendingCollectReward();
                if (!CollectRewardPending)
                {
                    return;
                }
                await TickCollectRewardWait();
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
        }
    }
}
