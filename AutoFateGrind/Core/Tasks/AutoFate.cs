using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;
using clib.Extensions;
using clib.TaskSystem;
using clib.Utils;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;
using System.Threading.Tasks;
using CSFateManager = FFXIVClientStructs.FFXIV.Client.Game.Fate.FateManager;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate(IReadOnlyList<ZoneInfo> zones, AutoFateSession session, int startIndex = 0) : AutoCommon
{
    private readonly IReadOnlyList<ZoneInfo> zones = zones;
    private readonly AutoFateSession session = session;
    private int zoneIndex = zones.Count == 0 ? 0 : Math.Clamp(startIndex, 0, zones.Count - 1);
    private ZoneInfo zone => zones[zoneIndex];

    public int ZoneIndex => zoneIndex;

    private readonly HashSet<uint> sessionStuckFateIds = new();
    private static readonly HashSet<uint> obstacleMapBlacklist = new() { 1831, 1832, 1914, 1915 };

    private const float InteractRangeMeters = 3f;
    private const float TeleportRetryProgressMeters = 3.0f;
    private const float TeleportShortcutMinSavingMeters = 300f;
    // MoveStallTracker handles actual lack of progress. This longer cap only catches a movement
    // operation that remains alive despite its normal stop and cancellation paths.
    private const int   MoveToFateEmergencyTimeoutMs = 300_000;
    private const int   MoveProgressLogMs = 15_000;
    private const int   FollowUpWatchMs = AfgConstants.FollowUpWaitMs;
    private const int   NpcSpawnTimeoutMs = 30_000;
    private const int   EngageStallTimeoutMs = 60_000;
    private const int   EngageOutOfCombatGraceMs = 30_000;
    // Reach and tolerances are measured from the mob's hitbox edge, not its centre.
    private const float EngageMeleeReachMeters  = 4f;
    private const float EngageRangedReachMeters = 25f;
    // Idle = the character has not displaced while nothing in reach is being fought; BossMod never moves
    // toward a mob outside its FATE-circle pathfind map, so AFG walks in with vnav after this long.
    private const int   EngageIdleStallMs = 8_000;
    // Give BossMod a brief chance to close normally, then use vnav when the selected FATE target remains
    // outside this job's attack range. This also catches movement that keeps heading to a stale fixed point.
    private const int   EngageTargetOutOfRangeGraceMs = 1_500;
    // BossMod may deliberately move away from the selected target to resolve a mechanic. Do not let the
    // range watchdog reverse that movement until the mechanic has had time to resolve.
    private const int   EngageTargetRetreatHoldMs = 6_000;
    private const int   EngageTargetCastSettleMs = 1_500;
    private const float EngageTargetMovementSampleMeters = 0.75f;
    private const float EngageTargetRetreatIncreaseMeters = 0.35f;
    private const float EngageTargetRepathMeters = 5f;
    private const int   EngageRepositionWatchdogMs = 40_000;
    private const float EngageMeleeApproachToleranceMeters  = 2.5f;
    private const float EngageRangedApproachToleranceMeters = 15f;
    private const int   MaxEngageRepositions = 3;
    // With no live FATE mob loaded, walk to the ring centre from further out than this to load the rest.
    private const float EngageCentreSeekMinMeters = 20f;
    private const float EngageCentreSeekToleranceMeters = 10f;
    private const int   EngageCombatStallMs = 30_000;
    private const int   MaxCombatStallBounces = 3;
    // Bails (60s stall or bounces exhausted) per FATE before it is abandoned instead of re-entered forever.
    private const int   MaxEngageStallStrikes = 2;
    // Cap on fighting off a mob that aggroed mid-travel, so an unkillable add can't park the run.
    private const int   CombatClearTimeoutMs = 30_000;
    private const int   RaiseWaitMs = 30_000;
    private const int   ReleaseTransitionWaitMs = 60_000;
    private const int   IdleWaitBeforeSwapMs = 30_000;
    private const int   IdleScanIntervalMs   = 1_000;
    private const int   MidPathRetargetIntervalMs = 1_500;
    // Retarget hysteresis: stops Progress ticks (ranked above Distance) from flip-flopping the target.
    private const float RetargetDistanceMarginMeters = 15f;
    private const float RetargetNearArrivalLockMeters = 20f;
    private const int   TeleportWatchdogMs = 60_000;
    private const int   ActivateMoveWatchdogMs = 60_000;
    private const int   ActivateApproachWatchdogMs = 20_000;
    private const float ActivateApproachToleranceMeters = 2f;
    // Below this, summoning and dismissing a mount costs more time than the walk.
    private const float ActivateMountMinMeters = 30f;
    private const int   NpcInteractAttempts = 3;
    private const int   InteractReadyTimeoutMs = 10_000;
    private const int   TargetSettleTimeoutMs = 500;
    private const int   DialogOpenTimeoutMs = 2_500;
    private const int   NpcDialogTimeoutMs = 30_000;
    private const int   DialogClosedGraceMs = 2_000;
    private const int   InteractRetryDelayMs = 1_000;
    private const int   DialogDismissTimeoutMs = 3_000;
    private const int   NavmeshReadyWaitMs = 60_000;
    private const int   HeartbeatMs = 30_000;
    private const int   MaxConsecutiveStateErrors = 20;
    private const int   GemstoneSettleTimeoutMs = 2_500;
    // Obstacle-map generation poll deadline before proceeding without one.
    private const int   ObstacleMapGenTimeoutMs = 5_000;
    // WrongZone give-up ladder: rotate zones, then (single-zone) fault into auto-resume — never re-issue
    // an impossible teleport forever (the 2026-05-30 wedge).
    private const int   WrongZoneSwapAfterFailures  = 2;
    private const int   WrongZoneFaultAfterFailures = 3;
    // Never-stuck backstop: zero forward progress for this long in a non-idle state faults into auto-resume.
    internal const int  NoProgressFaultMs = 300_000;

    private uint? lastStuckFateId;
    private int consecutiveStuckRetries;
    private uint? lastTeleportedFateId;
    private int consecutiveZoneTeleportFailures;

    private uint? returnToFateId;          // FATE we died in; honor even if normal eligibility fails.
    private uint? arrivedFateId;           // Reached while CurrentFate is stale/empty as a Collect reward settles.
    private uint? followUpFateId;
    private long  followUpWatchUntilMs;
    private long  zoneIdleSinceMs;
    private uint? abandonedFateId;
    private uint? engageStallFateId;
    private int   engageStallStrikes;

    private static readonly Random rng = new();
    private bool presetEnsured;
    private string? combatOverridesPreset;
    private int combatOverridesPullSize = -1;
    private string? combatOverridesTargetRange;
    private string? chocoboOverridePreset;
    private bool? combatOverridesChocobo;
    private bool chocoboOverrideUnavailableLogged;
    private long nextChocoboOverrideAttemptMs;

    // ExecuteCommand revive opcodes, per clib.Enums (CommandFlag.Revive + AgentReviveOp).
    private const uint ReviveCommandId   = (uint)clib.Enums.CommandFlag.Revive;        // 200
    private const int  ReviveParamReturn = (int)clib.Enums.AgentReviveOp.Return;        // 8 — return to home point
    private const int  ReviveParamAccept = (int)clib.Enums.AgentReviveOp.AcceptRevive;  // 5 — accept a raise
    private const int  ReturnReissueMs   = 1_500;
    private const int  RevivePollMs      = 250;
    private const int  WeaknessSettleMs  = 1_000;
    private const int  GemstoneSettlePollMs = 100;

    private enum GrindState
    {
        Idle,                 // Transient; player object not available, etc.
        WrongZone,            // Not in target territory.
        SwapZone,             // Rotate to next selected zone when the current one stays empty.
        AllDone,              // Stop condition met; return cleanly.
        YokaiAdvance,         // The yo-kai being farmed is done; hand off so the controller plans the next one.
        Unconscious,          // Player KO'd, run revive.
        WaitingForFollowUp,   // Just finished a chain parent; hold briefly for sequel.
        WaitingForCollectReward, // Nothing left to pick here, but a finished Collect FATE still owes its reward.
        WaitingForYokaiMinion, // The target yo-kai minion is not out; hold and re-summon rather than fight for nothing.
        BetweenFates,         // Have a target FATE; move (or activate prep NPC) and arrive.
        Engaging,             // CurrentFate is set; fight until it ends or we KO.
        WaitingForFates,      // No eligible FATE; idle-scan with optional zone swap.
    }

    private GrindState lastObservedState = GrindState.Idle;
    private long lastStateChangedAtMs;
    private long lastHeartbeatAtMs;

    private long noProgressSinceMs;
    private int  noProgressCompleted = -1;
    private uint noProgressTerritory;
    private Vector3 noProgressPos;

    protected override async Task Execute()
    {
        ErrorIf(zones.Count == 0, "No zones to grind.");
        // Accepts stock BossMod or BossMod Reborn — both expose the same IPC gates.
        ErrorIf(
            !BossModIPC.Instance.IsAvailable || !ExternalPlugins.IsInstalled(ExternalPlugin.BossMod),
            "BossMod (or BossMod Reborn) not installed or not loaded.");

        Svc.Chat.Print($"[AFG] Starting {zone.Name}...");
        Diag($"PC clock offset from the game server: {FateClock.LocalClockOffsetSeconds:+0;-0;0}s");
        lastStateChangedAtMs = Environment.TickCount64;

        try
        {
            // Eat up front so the buff is live before the first FATE (food works anywhere out of combat).
            await EnsureConsumables();
            await EnsureYokaiCompanion();
            await RunStateMachine();
            Svc.Chat.Print($"[AFG] {zone.Name}: zone done.");
        }
        catch (Exception ex)
        {
            DisableTextAdvance();
            // A user Stop / run cancellation also unwinds through here; only flag a genuine fault so the
            // controller's bounded auto-resume can't be triggered by a deliberate stop.
            if (Plugin.Cfg.AutoResumeOnFault
             && !CancelToken.IsCancellationRequested
             && ex is not OperationCanceledException
             && ex is not UnrecoverableRunException)
            {
                session.EndedWithFault = true;
                session.FaultResumeZoneIndex = zoneIndex;
            }
            var msg = ex.Message;
            var lastBracket = msg.LastIndexOf("] ");
            if (lastBracket >= 0) msg = msg[(lastBracket + 2)..];
            Svc.Chat.PrintError($"[AFG] {zone.Name} stopped: {msg}");
            throw;
        }
        finally
        {
            DisableTextAdvance();
        }
    }

    private async Task RunStateMachine()
    {
        var consecutiveErrors = 0;
        while (!CancelToken.IsCancellationRequested)
        {
          // Outside the try so its fault propagates (the catch below only swallows transient errors).
          GuardForwardProgress(lastObservedState);
          try
          {
            session.UpdateGemstones();

            var state = ComputeState();

            if (state is not GrindState.WaitingForFates)
                zoneIdleSinceMs = 0;

            if (state != lastObservedState)
            {
                Diag($"State {lastObservedState} -> {state}");
                if (state != GrindState.WrongZone) consecutiveZoneTeleportFailures = 0;
                if (lastObservedState == GrindState.WaitingForCollectReward) BossModIPC.Instance.ClearActive();
                lastObservedState = state;
                lastStateChangedAtMs = Environment.TickCount64;
            }

            if (Environment.TickCount64 - lastHeartbeatAtMs >= HeartbeatMs)
            {
                lastHeartbeatAtMs = Environment.TickCount64;
                LogHeartbeat(state);
            }

            switch (state)
            {
                case GrindState.AllDone:
                    Status = "Stop condition met";
                    Diag("Stop condition met; exiting");
                    ReportYokaiGoalMet();
                    session.CompletedByStopCondition = true;
                    return;

                case GrindState.YokaiAdvance:
                    await HandOffToNextYokai();
                    return;

                case GrindState.Unconscious:
                    await Revive();
                    break;

                case GrindState.WrongZone:
                    await GoToZone();
                    break;

                case GrindState.SwapZone:
                    if (!AdvanceZone())
                    {
                        Status = $"Waiting for FATEs in {zone.Name} (no other reachable zone)";
                        Diag("No other reachable zone to rotate to; continuing to wait here");
                    }
                    break;

                case GrindState.WaitingForFollowUp:
                    await TickFollowUpWait();
                    break;

                case GrindState.WaitingForCollectReward:
                    await TickCollectRewardWait();
                    break;

                case GrindState.WaitingForYokaiMinion:
                    await TickYokaiMinionWait();
                    break;

                case GrindState.BetweenFates:
                    if (await MoveAndArrive() is ExitReason.Quit) return;
                    break;

                case GrindState.Engaging:
                    if (await EngageCurrentFate() is ExitReason.Quit) return;
                    break;

                case GrindState.WaitingForFates:
                    await TickIdleScan();
                    break;

                case GrindState.Idle:
                default:
                    await NextFrame(30);
                    break;
            }

            // Hard safety net: guarantee the loop yields the framework thread at least once per
            // iteration. Some handlers can return synchronously (e.g. a FATE that ended the instant
            // we entered); without this a synchronous state could spin and freeze the game.
            await NextFrame();
            consecutiveErrors = 0;
          }
          catch (Exception ex) when (!CancelToken.IsCancellationRequested && ex is not UnrecoverableRunException)
          {
            // One transient fault (e.g. a clib NRE on a despawned FATE) must not end the grind; back off
            // and retry. Only a long unbroken run of failures (a genuinely wedged state) surfaces and stops.
            consecutiveErrors++;
            Diag($"State machine caught {ex.GetType().Name} (#{consecutiveErrors}/{MaxConsecutiveStateErrors}): {ex.Message}");
            if (consecutiveErrors >= MaxConsecutiveStateErrors)
            {
                Diag("Too many consecutive state-machine errors; surfacing and stopping.");
                throw;
            }
            await NextFrame(30);
          }
        }
    }

    private void LogHeartbeat(GrindState state)
    {
        var player = Svc.Objects.LocalPlayer;
        var pos = player?.Position;
        var posStr = pos is { } p ? $"({p.X:F0},{p.Y:F0},{p.Z:F0})" : "?";
        var fate = PublicEvent.CurrentFate;
        var fateStr = fate is null ? "none" : $"{fate.Id}@{fate.Progress}%/{fate.State}";
        var inState = (Environment.TickCount64 - lastStateChangedAtMs) / 1000;
        var nav = NavmeshIPC.Instance;
        var navStr = $"run={nav.IsRunning()} busy={nav.IsBusy()}";
        Diag($"HEARTBEAT state={state} ({inState}s) terr={Svc.ClientState.TerritoryType} zone={zone.Name} pos={posStr} fate={fateStr} {navStr} cond={ConditionTag()} " +
             $"done={session.CompletedCount} ret={returnToFateId?.ToString() ?? "-"} followUp={followUpFateId?.ToString() ?? "-"} collectReward={(CollectRewardPending ? pendingRewardSpawn.FateId.ToString() : "-")} stuckBL={sessionStuckFateIds.Count}");

        if (state is not GrindState.Engaging and not GrindState.WaitingForFates and not GrindState.WaitingForCollectReward and not GrindState.WaitingForYokaiMinion && inState >= 180)
            Diag($"STALL WARNING: state {state} held {inState}s — see prior heartbeats for context.");
    }

    // Progress = a completed FATE, a territory change, or real movement. None for NoProgressFaultMs in a
    // state that shouldn't sit still (i.e. not fighting / idle-waiting) surfaces a fault for recovery.
    private void GuardForwardProgress(GrindState state)
    {
        var now = Environment.TickCount64;
        var terr = Svc.ClientState.TerritoryType;
        var pos = Svc.Objects.LocalPlayer?.Position ?? noProgressPos;

        var advanced = noProgressCompleted < 0
                    || session.CompletedCount != noProgressCompleted
                    || terr != noProgressTerritory
                    || Vector3.Distance(pos, noProgressPos) > StuckDetector.StuckMoveThresholdMeters
                    || state is GrindState.Engaging or GrindState.WaitingForFates or GrindState.WaitingForYokaiMinion;

        if (advanced)
        {
            noProgressCompleted = session.CompletedCount;
            noProgressTerritory = terr;
            noProgressPos = pos;
            noProgressSinceMs = now;
            return;
        }

        ErrorIf(now - noProgressSinceMs >= NoProgressFaultMs,
            $"No forward progress for {NoProgressFaultMs / 60000}m in state {state}; surfacing fault for recovery.");
    }

    private GrindState ComputeState()
    {
        RefreshPendingCollectReward();

        if (abandonedFateId is { } abandonedId && !IsStillExcluded(abandonedId))
            abandonedFateId = null;

        if (IsPlayerKO())
        {
            if (PublicEvent.CurrentFate is { Progress: < 100 } dying && !FateBlacklist.Contains(Plugin.Cfg, dying))
                returnToFateId = dying.Id;
            followUpFateId = null;
            return GrindState.Unconscious;
        }

        if (StopConditionMet())
            return GrindState.AllDone;

        if (YokaiTargetChanged())
            return GrindState.YokaiAdvance;

        if (Svc.ClientState.TerritoryType != zone.TerritoryId)
            return GrindState.WrongZone;

        // Only a Running CurrentFate means "fight it". A completed fate lingers non-Running for a
        // frame; routing that to Engaging (which returns instantly) would spin and freeze the game.
        var current = PublicEvent.CurrentFate;
        if (current is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100 }
         && abandonedFateId != current.Id
         && current.State is not (FateState.Ended or FateState.Failed))
        {
            // The row lingers as a hand-in window and pays out when it clears (issue #64). Nothing is left to
            // fight in it, so the reward is tracked and the grind moves on to the next FATE in the zone.
            TrackCollectReward(current);
        }
        var engagement = ResolveEngagementFate(current);
        if (engagement is not null)
        {
            if (engagement.Progress >= 100)
                StartFollowUpWatch(engagement.Id);
            else if (followUpFateId == engagement.Id)
                followUpFateId = null;
            return GrindState.Engaging;
        }

        if (ShouldWaitForFollowUp())
            return GrindState.WaitingForFollowUp;

        if (YokaiMinionMissing())
            return GrindState.WaitingForYokaiMinion;

        if (returnToFateId is { } retId)
        {
            if (PublicEvent.GetFateById(retId) is { Progress: < 100 } && !IsStillExcluded(retId))
                return GrindState.BetweenFates;
            returnToFateId = null;
        }

        var player = Svc.Objects.LocalPlayer;
        if (player is null) return GrindState.Idle;

        if (FateScanner.PickNext(Plugin.Cfg, player.Position, sessionStuckFateIds, returnToFateId) is not null)
            return GrindState.BetweenFates;

        // Leaving the zone forfeits a pending Collect reward; wait for it here instead of starting the swap clock.
        if (CollectRewardPending)
            return GrindState.WaitingForCollectReward;

        if (zoneIdleSinceMs == 0)
            zoneIdleSinceMs = Environment.TickCount64;

        if (Plugin.Cfg.SwapZonesWhenEmpty && zones.Count > 1
         && Environment.TickCount64 - zoneIdleSinceMs >= IdleWaitBeforeSwapMs)
            return GrindState.SwapZone;

        return GrindState.WaitingForFates;
    }

    // A FATE that despawned, was un-banned, or left the session stuck set is free to be engaged again.
    private bool IsStillExcluded(uint fateId)
    {
        if (PublicEvent.GetFateById(fateId) is not { } fate)
        {
            return false;
        }

        return sessionStuckFateIds.Contains(fateId) || FateBlacklist.Contains(Plugin.Cfg, fate);
    }

    private enum ExitReason { Continue, Quit }

    private async Task GoToZone()
    {
        Status = $"Teleporting to {zone.Name}";
        Diag($"Off-zone (in {Svc.ClientState.TerritoryType}), teleporting to {zone.TerritoryId}");
        var result = await AttemptTerritoryTeleport(zone.TerritoryId, zone.CentralLanding, "teleport-to-zone", TeleportWatchdogMs);
        if (result == TerritoryTeleportResult.Reached)
        {
            consecutiveZoneTeleportFailures = 0;
            session.UnreachableZoneIds.Remove(zone.TerritoryId);
            return;
        }
        if (CancelToken.IsCancellationRequested) return;

        consecutiveZoneTeleportFailures++;
        if (result == TerritoryTeleportResult.Blocked)
        {
            FaultIfCharacterStaysBlocked();
            return;
        }

        Warn($"Could not reach {zone.Name} (failure {consecutiveZoneTeleportFailures}); escalating to keep the run moving.");

        if (consecutiveZoneTeleportFailures >= WrongZoneSwapAfterFailures && zones.Count > 1)
        {
            var unreachableZoneName = zone.Name;
            var newlyUnreachable = session.UnreachableZoneIds.Add(zone.TerritoryId);
            var advanced = AdvanceZone();
            if (newlyUnreachable)
            {
                Svc.Chat.PrintError(advanced
                    ? $"[AFG] Could not teleport to {unreachableZoneName} (aetherytes attuned?); skipping it for the rest of this run."
                    : $"[AFG] Could not teleport to {unreachableZoneName} (aetherytes attuned?) and no other selected zone is reachable.");
            }
            if (advanced)
            {
                consecutiveZoneTeleportFailures = 0;
                return;
            }
        }

        ErrorIf(consecutiveZoneTeleportFailures >= WrongZoneFaultAfterFailures,
            $"Unable to reach {zone.Name} after {consecutiveZoneTeleportFailures} teleport attempts.");
    }

    private void FaultIfCharacterStaysBlocked()
    {
        var blocker = ConditionTag();
        Warn($"No teleport cast toward {zone.Name} could start (failure {consecutiveZoneTeleportFailures}, {blocker}); the character is held, so the zone stays reachable.");
        if (consecutiveZoneTeleportFailures < WrongZoneFaultAfterFailures)
        {
            return;
        }

        throw new UnrecoverableRunException(
            $"No teleport could start in {consecutiveZoneTeleportFailures} rounds (character state: {blocker}). Close any open window or dialog, then start again.");
    }

    private bool AdvanceZone()
    {
        for (var step = 1; step < zones.Count; step++)
        {
            var candidateIndex = (zoneIndex + step) % zones.Count;
            if (session.UnreachableZoneIds.Contains(zones[candidateIndex].TerritoryId)) continue;

            zoneIndex = candidateIndex;
            sessionStuckFateIds.Clear();
            lastStuckFateId = null;
            consecutiveStuckRetries = 0;
            lastTeleportedFateId = null;
            return true;
        }
        return false;
    }

    private async Task TickIdleScan()
    {
        await EnsureConsumables();
        TryMountWhileWaiting();
        var swapPending = Plugin.Cfg.SwapZonesWhenEmpty && zones.Count > 1;
        var remainingSec = Math.Max(0L, IdleWaitBeforeSwapMs - (Environment.TickCount64 - zoneIdleSinceMs)) / 1000;
        Status = swapPending
            ? $"Waiting for FATEs in {zone.Name} (swapping in {remainingSec}s)"
            : $"Waiting for FATEs in {zone.Name}";
        await DelayMs(IdleScanIntervalMs);
    }

    private const int ConsumeItemWaitMs = 6_000;

    // Each item is bounded by a wall-clock deadline, so a use that never lands can't park the grind.
    private async Task EnsureConsumables()
    {
        var cfg = Plugin.Cfg;
        if (!cfg.AutoConsume || cfg.AutoConsumeItems.Count == 0) return;
        if (Svc.Condition[ConditionFlag.InCombat]) return;
        if (IsPlayerKO()) return;
        if (!FoodOps.AnyNeeded(cfg)) return;

        // Eating requires being grounded; dismount first if we're on a mount (e.g. Start pressed mounted).
        await SafeDismount("dismount-consume");
        if (Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.InCombat]) return;

        var minSeconds = Math.Max(0, cfg.AutoConsumeMinMinutes) * 60f;
        foreach (var entry in cfg.AutoConsumeItems)
        {
            if (CancelToken.IsCancellationRequested) return;
            if (FoodOps.HasStatus(entry.StatusId, minSeconds)) continue;
            if (!FoodOps.IsAvailable(entry)) continue;

            Status = $"Consuming {entry.Name}";
            Diag($"Auto-consume: {entry.Name} (status {entry.StatusId} missing or under {cfg.AutoConsumeMinMinutes}m)");
            await WaitUntilTimed(() =>
            {
                if (FoodOps.HasStatus(entry.StatusId, minSeconds)) return true;
                if (Svc.Condition[ConditionFlag.InCombat]) return true; // combat started; re-apply later
                FoodOps.UseConsumable(entry);
                return false;
            }, ConsumeItemWaitMs, $"consume-{entry.ItemId}", checkFrames: 100);
        }
    }

    private async Task TickFollowUpWait()
    {
        TryMountWhileWaiting();
        var remaining = Math.Max(0L, followUpWatchUntilMs - Environment.TickCount64);
        Status = $"Watching for follow-up FATE ({remaining / 1000 + 1}s)";
        await NextFrame(100);
    }

}
