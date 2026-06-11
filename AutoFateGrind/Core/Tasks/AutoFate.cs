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

    private readonly HashSet<uint> sessionStuckFateIds = new();
    private static readonly HashSet<uint> obstacleMapBlacklist = new() { 1831, 1832, 1914, 1915 };

    private const int   HardStuckTimeoutMs = 3_000;
    private const float TeleportRetryProgressMeters = 3.0f;
    private const int   MoveToFateWatchdogMs = 60_000;
    // Slack on top of the in-move deadline so clib's own graceful 60s exit wins over the hard cancel
    // when it is following a path; the hard cancel only catches a wedge in a non-polling phase.
    private const int   MoveOpUnwindSlackMs = 10_000;
    private const int   DismountWatchdogMs = 30_000;
    private const int   MoveProgressLogMs = 15_000;
    private const int   FollowUpWatchMs = AfgConstants.FollowUpWaitMs;
    private const int   NpcSpawnTimeoutMs = 30_000;
    private const int   CollectExpiryTimeoutMs = 90_000;
    private const int   EngageStallTimeoutMs = 60_000;
    private const int   EngageOutOfCombatGraceMs = 30_000;
    // Cap on fighting off a mob that aggroed mid-travel, so an unkillable add can't park the run.
    private const int   CombatClearTimeoutMs = 30_000;
    private const int   RaiseWaitMs = 30_000;
    private const int   ReleaseTransitionWaitMs = 60_000;
    private const int   IdleScansBeforeSwap = 30;
    private const int   MidPathRetargetIntervalMs = 1_500;
    // Retarget hysteresis: stops Progress ticks (ranked above Distance) from flip-flopping the target.
    private const float RetargetDistanceMarginMeters = 15f;
    private const float RetargetNearArrivalLockMeters = 20f;
    private const int   TeleportWatchdogMs = 60_000;
    private const int   AethernetWatchdogMs = 60_000;
    private const int   ActivateMoveWatchdogMs = 60_000;
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
    private const int   NoProgressFaultMs = 300_000;
    // MotivationNpcId sentinel meaning the FATE has no activation NPC.
    private const uint  NoMotivationNpcId = 0xE0000000;

    private uint? lastStuckFateId;
    private int consecutiveStuckRetries;
    private uint? lastTeleportedFateId;
    private int consecutiveZoneTeleportFailures;

    private uint? returnToFateId;          // FATE we died in; honor even if normal eligibility fails.
    private uint? followUpFateId;
    private long  followUpWatchUntilMs;
    private uint? waitForExpiryFateId;
    private long  waitForExpiryStartedAtMs;
    private int   idleScans;

    private static readonly Random rng = new();
    private bool presetEnsured;

    // ExecuteCommand revive opcodes, per clib.Enums (CommandFlag.Revive + AgentReviveOp).
    private const uint ReviveCommandId   = (uint)clib.Enums.CommandFlag.Revive;        // 200
    private const int  ReviveParamReturn = (int)clib.Enums.AgentReviveOp.Return;        // 8 — return to home point
    private const int  ReviveParamAccept = (int)clib.Enums.AgentReviveOp.AcceptRevive;  // 5 — accept a raise
    private const int  ReturnReissueMs   = 1_500;

    private enum GrindState
    {
        Idle,                 // Transient; player object not available, etc.
        WrongZone,            // Not in target territory.
        SwapZone,             // Rotate to next selected zone when the current one stays empty.
        AllDone,              // Stop condition met; return cleanly.
        Unconscious,          // Player KO'd, run revive.
        WaitingForFollowUp,   // Just finished a chain parent; hold briefly for sequel.
        WaitingForExpiry,     // Collect FATE complete; hold zone until row clears for rewards.
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
        lastStateChangedAtMs = Environment.TickCount64;

        try
        {
            // Eat up front so the buff is live before the first FATE (food works anywhere out of combat).
            await EnsureConsumables();
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
             && ex is not OperationCanceledException)
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

            if (state != lastObservedState)
            {
                Diag($"State {lastObservedState} -> {state}");
                if (state != GrindState.WrongZone) consecutiveZoneTeleportFailures = 0;
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
                    session.CompletedByStopCondition = true;
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
                        Status = "No other zone to rotate to";
                        Diag("Only one zone selected; nothing to rotate to");
                        return;
                    }
                    idleScans = 0;
                    break;

                case GrindState.WaitingForFollowUp:
                    await TickFollowUpWait();
                    break;

                case GrindState.WaitingForExpiry:
                    await TickExpiryWait();
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
          catch (Exception ex) when (!CancelToken.IsCancellationRequested)
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
             $"done={session.CompletedCount} ret={returnToFateId?.ToString() ?? "-"} followUp={followUpFateId?.ToString() ?? "-"} stuckBL={sessionStuckFateIds.Count}");

        if (state is not GrindState.Engaging and not GrindState.WaitingForFates && inState >= 180)
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
                    || state is GrindState.Engaging or GrindState.WaitingForFates;

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
        if (waitForExpiryFateId is { } wid)
        {
            if (PublicEvent.GetFateById(wid) is null
             || Environment.TickCount64 - waitForExpiryStartedAtMs > CollectExpiryTimeoutMs)
            {
                if (Environment.TickCount64 - waitForExpiryStartedAtMs > CollectExpiryTimeoutMs)
                    Diag($"Collect expiry watch timed out for {wid}");
                waitForExpiryFateId = null;
            }
        }

        if (IsPlayerKO())
        {
            if (PublicEvent.CurrentFate is { Progress: < 100, Id: var dyingId })
                returnToFateId = dyingId;
            followUpFateId = null;
            return GrindState.Unconscious;
        }

        if (StopConditionMet())
            return GrindState.AllDone;

        if (Svc.ClientState.TerritoryType != zone.TerritoryId)
            return GrindState.WrongZone;

        // Only a Running CurrentFate means "fight it". A completed fate lingers non-Running for a
        // frame; routing that to Engaging (which returns instantly) would spin and freeze the game.
        if (PublicEvent.CurrentFate is { State: FateState.Running } current)
        {
            // Hold a completed Collect until out of combat so a stray mob can't trap us mid-deactivation.
            if (current is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100, Id: var cid })
            {
                if (waitForExpiryFateId != cid)
                {
                    waitForExpiryFateId = cid;
                    waitForExpiryStartedAtMs = Environment.TickCount64;
                }
                if (!Svc.Condition[ConditionFlag.InCombat])
                    return GrindState.WaitingForExpiry;
            }
            if (current.Progress >= 100)
                StartFollowUpWatch(current.Id);
            else if (followUpFateId == current.Id)
                followUpFateId = null;
            return GrindState.Engaging;
        }

        if (waitForExpiryFateId is not null)
            return GrindState.WaitingForExpiry;

        if (ShouldWaitForFollowUp())
            return GrindState.WaitingForFollowUp;

        if (returnToFateId is { } retId)
        {
            if (PublicEvent.GetFateById(retId) is { Progress: < 100 })
                return GrindState.BetweenFates;
            returnToFateId = null;
        }

        var player = Svc.Objects.LocalPlayer;
        if (player is null) return GrindState.Idle;

        if (FateScanner.PickNext(Plugin.Cfg, player.Position, sessionStuckFateIds, returnToFateId) is not null)
            return GrindState.BetweenFates;

        if (Plugin.Cfg.SwapZonesWhenEmpty && zones.Count > 1 && idleScans >= IdleScansBeforeSwap)
            return GrindState.SwapZone;

        return GrindState.WaitingForFates;
    }

    private enum ExitReason { Continue, Quit }

    private async Task GoToZone()
    {
        Status = $"Teleporting to {zone.Name}";
        Diag($"Off-zone (in {Svc.ClientState.TerritoryType}), teleporting to {zone.TerritoryId}");
        if (await TeleportToTerritory(zone.TerritoryId, zone.CentralLanding, "teleport-to-zone", TeleportWatchdogMs))
        {
            consecutiveZoneTeleportFailures = 0;
            return;
        }
        if (CancelToken.IsCancellationRequested) return;

        consecutiveZoneTeleportFailures++;
        Warn($"Could not reach {zone.Name} (failure {consecutiveZoneTeleportFailures}); escalating to keep the run moving.");

        // Unreachable target: grind another zone instead (the rotation may land on the one we're in).
        if (consecutiveZoneTeleportFailures >= WrongZoneSwapAfterFailures && zones.Count > 1)
        {
            Diag($"Rotating away from unreachable {zone.Name}.");
            AdvanceZone();
            consecutiveZoneTeleportFailures = 0;
            return;
        }

        // Single-zone and still stuck: bounded fault -> auto-resume (or clean end), never an endless wedge.
        ErrorIf(consecutiveZoneTeleportFailures >= WrongZoneFaultAfterFailures,
            $"Unable to reach {zone.Name} after {consecutiveZoneTeleportFailures} teleport attempts.");
    }

    private bool AdvanceZone()
    {
        if (zones.Count <= 1) return false;

        zoneIndex = (zoneIndex + 1) % zones.Count;
        sessionStuckFateIds.Clear();
        lastStuckFateId = null;
        consecutiveStuckRetries = 0;
        lastTeleportedFateId = null;
        return true;
    }

    private async Task TickIdleScan()
    {
        await EnsureConsumables();
        idleScans++;
        Status = $"Waiting for FATEs in {zone.Name} ({idleScans}/{IdleScansBeforeSwap})";
        await NextFrame(60);
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
        if (Svc.Condition[ConditionFlag.Mounted])
            await DismountViaOp("dismount-consume");
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
            }, ConsumeItemWaitMs, $"consume-{entry.ItemId}", checkMs: 100);
        }
    }

    private async Task TickFollowUpWait()
    {
        var remaining = Math.Max(0L, followUpWatchUntilMs - Environment.TickCount64);
        Status = $"Watching for follow-up FATE ({remaining / 1000 + 1}s)";
        await NextFrame(100);
    }

    private async Task TickExpiryWait()
    {
        Status = "Waiting for Collect rewards";
        await NextFrame(60);
    }

}
