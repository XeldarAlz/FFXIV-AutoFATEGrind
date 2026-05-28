using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game;
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

// Each tick computes the desired state, then dispatches a bounded handler. Every wait has a
// wall-clock timeout so no single condition can park the run — an early return self-recovers.
public sealed class AutoFate(IReadOnlyList<ZoneInfo> zones, AutoFateSession session, int startIndex = 0) : AutoCommon
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
    private const int   FollowUpWatchMs = 15_000;
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

    private uint? lastStuckFateId;
    private int consecutiveStuckRetries;
    private uint? lastTeleportedFateId;

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

    protected override async Task Execute()
    {
        ErrorIf(zones.Count == 0, "No zones to grind.");
        // Reborn exposes the same BossMod.* gates, so also require stock BossMod loaded by name.
        ErrorIf(
            !BossModIPC.Instance.IsAvailable || !ExternalPlugins.IsInstalled(ExternalPlugin.BossMod),
            "BossMod not installed or not loaded.");

        Svc.Chat.Print($"[AFG] Starting {zone.Name}...");
        lastStateChangedAtMs = Environment.TickCount64;

        try
        {
            await RunStateMachine();
            Svc.Chat.Print($"[AFG] {zone.Name}: zone done.");
        }
        catch (Exception ex)
        {
            DisableTextAdvance();
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
        while (!CancelToken.IsCancellationRequested)
        {
            var state = ComputeState();

            if (state != lastObservedState)
            {
                Diag($"State {lastObservedState} -> {state}");
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
        Diag($"HEARTBEAT state={state} ({inState}s) terr={Svc.ClientState.TerritoryType} zone={zone.Name} pos={posStr} fate={fateStr} {navStr} " +
             $"done={session.CompletedCount} ret={returnToFateId?.ToString() ?? "-"} followUp={followUpFateId?.ToString() ?? "-"} stuckBL={sessionStuckFateIds.Count}");

        if (state is not GrindState.Engaging and not GrindState.WaitingForFates && inState >= 180)
            Diag($"STALL WARNING: state {state} held {inState}s — see prior heartbeats for context.");
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
        // Fast idle-stall detection + bounded retry inside; arrives or returns and the outer loop re-enters.
        await TeleportToTerritory(zone.TerritoryId, zone.CentralLanding, "teleport-to-zone", TeleportWatchdogMs);
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
        idleScans++;
        Status = $"Waiting for FATEs in {zone.Name} ({idleScans}/{IdleScansBeforeSwap})";
        await NextFrame(60);
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

    private async Task<ExitReason> MoveAndArrive()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) { await NextFrame(); return ExitReason.Continue; }

        var fate = FateScanner.PickNext(Plugin.Cfg, player.Position, sessionStuckFateIds, returnToFateId);
        if (fate is null) return ExitReason.Continue;

        idleScans = 0;
        Status = $"Moving to {fate.Name}";
        Diag($"Picked FATE {fate.Id} ({fate.Name}) at {fate.Position}");

        var moveResult = await MoveToFate(fate);
        if (CancelToken.IsCancellationRequested) return ExitReason.Quit;

        if (moveResult is MoveStopReason.HigherPriority or MoveStopReason.NpcSpawned)
            return ExitReason.Continue;

        // Teleport can't fire in combat, and the FATE is still reachable — fight free, don't blacklist.
        if (moveResult == MoveStopReason.StuckInCombat)
        {
            await ClearBlockingCombat();
            return ExitReason.Continue;
        }

        if (lastTeleportedFateId == fate.Id && moveResult != MoveStopReason.None)
        {
            Diag($"Still stuck after teleport recovery for FATE {fate.Id} ({fate.Name}); blacklisting for this session");
            sessionStuckFateIds.Add(fate.Id);
            lastTeleportedFateId = null;
            lastStuckFateId = null;
            consecutiveStuckRetries = 0;
            return ExitReason.Continue;
        }

        if (moveResult == MoveStopReason.StuckRetry)
        {
            if (lastStuckFateId == fate.Id) consecutiveStuckRetries++;
            else { lastStuckFateId = fate.Id; consecutiveStuckRetries = 1; }

            if (consecutiveStuckRetries >= 2)
            {
                Diag($"Repeated stuck on FATE {fate.Id} ({fate.Name}); escalating to teleport");
                moveResult = MoveStopReason.StuckTeleport;
            }
            else
            {
                Diag($"Stuck en route to FATE {fate.Id} ({fate.Name}); retrying from current position");
                return ExitReason.Continue;
            }
        }

        if (moveResult == MoveStopReason.StuckTeleport)
        {
            if (Svc.Condition[ConditionFlag.InCombat])
            {
                Diag($"Stuck-teleport for FATE {fate.Id} but in combat; clearing aggro before teleporting (teleport is blocked in combat)");
                await ClearBlockingCombat();
                return ExitReason.Continue;
            }
            if (await TryTeleportToFate(fate))
            {
                lastTeleportedFateId = fate.Id;
                lastStuckFateId = null;
                consecutiveStuckRetries = 0;
                return ExitReason.Continue;
            }
            sessionStuckFateIds.Add(fate.Id);
            lastTeleportedFateId = null;
            lastStuckFateId = null;
            consecutiveStuckRetries = 0;
            Diag($"Teleport recovery failed for FATE {fate.Id}; blacklisting for this session");
            return ExitReason.Continue;
        }

        if (lastStuckFateId == fate.Id) { lastStuckFateId = null; consecutiveStuckRetries = 0; }
        if (lastTeleportedFateId == fate.Id) lastTeleportedFateId = null;

        // Boss/event FATEs must be activated via their NPC before they go Running. 0xE0000000 = no NPC.
        if (fate.State == FateState.Preparing && fate.MotivationNpcId != 0xE0000000)
            await ActivateFate(fate);

        if (returnToFateId == fate.Id && fate.State == FateState.Running)
            returnToFateId = null;

        return ExitReason.Continue;
    }

    private async Task<ExitReason> EngageCurrentFate()
    {
        var fate = PublicEvent.CurrentFate;
        if (fate is null) return ExitReason.Continue;
        var fateId = fate.Id;

        var preset = Plugin.Cfg.CombatPresetName;
        EnsureCombatPreset(preset);
        SyncToFate(fateId);
        AssertPresetActive(preset);

        await EnsureObstacleMapForEngage(fate);

        Status = $"Engaging {fate.Name}";

        var lastProgress = fate.Progress;
        var lastProgressAtMs = Environment.TickCount64;
        var lastInCombatAtMs = Environment.TickCount64;
        var collectTextAdvanceArmed = false;
        // Only an entry that fought the fate while Running may book the completion — guards against
        // a re-entry during the lingering 100% frame double-counting.
        var sawRunning = false;

        try
        {
            while (!CancelToken.IsCancellationRequested)
            {
                var refreshed = PublicEvent.GetFateById(fateId);
                if (refreshed is null || refreshed.State != FateState.Running) break;
                if (IsPlayerKO()) break;
                fate = refreshed;
                sawRunning = true;

                if (Svc.Condition[ConditionFlag.InCombat])
                    lastInCombatAtMs = Environment.TickCount64;

                if (fate.Progress != lastProgress)
                {
                    lastProgress = fate.Progress;
                    lastProgressAtMs = Environment.TickCount64;
                }
                else if (Environment.TickCount64 - lastProgressAtMs > EngageStallTimeoutMs
                      && Environment.TickCount64 - lastInCombatAtMs > EngageOutOfCombatGraceMs)
                {
                    Diag($"EngageFate stalled: no progress in {EngageStallTimeoutMs/1000}s and out of combat {EngageOutOfCombatGraceMs/1000}s on FATE {fateId}; bailing");
                    break;
                }

                if (Svc.Condition[ConditionFlag.Mounted])
                {
                    BossModIPC.Instance.ClearActive();
                    await DismountViaOp($"dismount-engage-{fateId}");
                    AssertPresetActive(preset);
                }
                else
                {
                    AssertPresetActive(preset);
                }

                SyncToFate(fateId);

                if (fate.Rule == PublicEvent.FateRule.Collect && !collectTextAdvanceArmed)
                {
                    EnableTextAdvanceForCollect();
                    collectTextAdvanceArmed = true;
                }

                await NextFrame(30);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
            if (collectTextAdvanceArmed) DisableTextAdvance();
        }

        var finalProgress = PublicEvent.GetFateById(fateId)?.Progress ?? lastProgress;
        var ended = sawRunning && (PublicEvent.GetFateById(fateId) is null || finalProgress >= 100);
        if (ended)
        {
            session.CompletedCount++;
            zone.CompletedThisRun++;
            session.GemstoneCurrent = GemstoneCatalog.CurrentWalletCount();
            Diag($"FATE {fateId} done (session total: {session.CompletedCount}, wallet {session.GemstoneCurrent}g)");
            StartFollowUpWatch(fateId);

            if (AdvanceClassQueueIfCapHit()) return ExitReason.Quit;

            if (Plugin.Cfg.AutoRepair
                && RepairOps.NeedsRepair(Plugin.Cfg.AutoRepairThresholdPct))
            {
                Diag($"Repair threshold tripped (lowest equipped at {RepairOps.LowestEquippedConditionPct():F0}% ≤ {Plugin.Cfg.AutoRepairThresholdPct}%); queueing repair hand-off.");
                session.PendingRepair = true;
                session.PendingRepairFromZone = zone;
                return ExitReason.Quit;
            }

            if (Plugin.Cfg.TradeOnCap && session.GemstoneCurrent >= Plugin.Cfg.TradeThreshold)
            {
                if (TryQueueTrade()) return ExitReason.Quit;
            }
        }

        return ExitReason.Continue;
    }

    private bool TryQueueTrade()
    {
        var targetId = GemstoneCatalog.EnsurePersistedTarget();
        if (targetId == 0)
        {
            Diag("Trade-on-cap skipped: EnsurePersistedTarget returned 0 (no gem catalog item maps to a registered Bicolor trader).");
            return false;
        }

        var target = GemstoneCatalog.FindById(targetId);
        if (target is null)
        {
            Diag($"Trade-on-cap skipped: saved target id {targetId} is not in the gem catalog (was the item removed or renamed?).");
            return false;
        }

        var qty = GemstoneCatalog.ComputeBuyQuantity(session.GemstoneCurrent, target.CostPerOne);
        if (qty <= 0)
        {
            Diag($"Trade-on-cap skipped: spend mode {Plugin.Cfg.SpendMode} with {Plugin.Cfg.KeepGemstonesReserve}g reserve buys 0× {target.ItemName} ({target.CostPerOne}g each, wallet {session.GemstoneCurrent}g).");
            return false;
        }

        var trader = GemstoneTrader.PickForItem(targetId, zone.TerritoryId, zone.Expansion);
        if (trader is null)
        {
            Diag($"Trade-on-cap skipped: no registered Bicolor trader sells {target.ItemName}. Pick a different item in /afg config → Trader.");
            return false;
        }

        Diag($"Gemstone threshold {Plugin.Cfg.TradeThreshold}g reached: queueing auto-trade for {qty}× {target.ItemName} at {trader.Name} (territory {trader.TerritoryId}).");
        session.PendingTradeFromZone = zone;
        return true;
    }

    private async Task Revive()
    {
        DisableTextAdvance();
        try { BossModIPC.Instance.ClearActive(); } catch { /* best-effort */ }

        var startZoneId = Svc.ClientState.TerritoryType;
        var startPos = Svc.Objects.LocalPlayer?.Position;

        var soloWait = Svc.Party.Length == 0;
        Status = soloWait ? "KO — releasing" : "KO — waiting for raise";
        Diag(soloWait ? "Solo KO: returning home." : "Party KO: waiting up to 30s for a raise.");

        var returningHome = soloWait;
        if (soloWait)
        {
            TriggerReturnHome();
        }
        else
        {
            var raiseDeadline = Environment.TickCount64 + RaiseWaitMs;
            var accepted = false;
            while (Environment.TickCount64 < raiseDeadline)
            {
                if (CancelToken.IsCancellationRequested) return;
                if (!IsPlayerKO())
                {
                    Diag("Raised by another player.");
                    accepted = true;
                    break;
                }
                if (TryAcceptRaisePrompt())
                {
                    Diag("Accepted raise prompt programmatically.");
                    accepted = true;
                    break;
                }
                await NextFrame(30);
            }

            if (!accepted)
            {
                Diag("No raise within window; falling back to return-home.");
                TriggerReturnHome();
                returningHome = true;
            }
        }

        await WaitForReviveOrTransition(reissueReturn: returningHome);

        // Revive can fail (window not ready, command dropped). Never chase a FATE while still
        // on the ground — bail and let the outer loop re-enter Unconscious and retry the release.
        if (IsPlayerKO())
        {
            Diag("Still KO after revive window; outer loop will retry.");
            return;
        }

        if (returnToFateId is { } retId
            && PublicEvent.GetFateById(retId) is { Progress: < 100 } retFate
            && Svc.ClientState.TerritoryType == zone.TerritoryId)
        {
            Status = "Returning to FATE";
            Diag($"Re-engaging FATE {retId} after revive.");
            var retPos = retFate.Position;
            var tp = new MoveOp(o => o.Teleport(zone.TerritoryId, retPos, allowSameZoneTeleport: true));
            if (await RunCancellable(tp, TeleportWatchdogMs, $"revive-return-tp-{retId}", IdleStallAbort(IdleStallTimeoutMs)))
            {
                var aeth = new MoveOp(o => o.Aethernet(zone.TerritoryId, retPos));
                await RunCancellable(aeth, AethernetWatchdogMs, $"revive-return-aethernet-{retId}", IdleStallAbort(IdleStallTimeoutMs));
            }
        }
        else if (Svc.ClientState.TerritoryType != startZoneId && startPos is not null)
        {
            Diag($"Revived in {Svc.ClientState.TerritoryType}, expected {startZoneId}; outer loop will retarget.");
        }
    }

    private void TriggerReturnHome()
    {
        if (!TryExecuteReviveCommand(ReviveParamReturn))
            Diag("Return-home command dispatch failed.");
    }

    private static bool TryExecuteReviveCommand(int param)
    {
        try
        {
            GameMain.ExecuteCommand((int)ReviveCommandId, param, 0, 0, 0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[AFG] GameMain.ExecuteCommand revive failed");
            return false;
        }
    }

    private static bool TryAcceptRaisePrompt()
    {
        // No reliable raise-prompt addon check that's API-stable across patches; accept-attempt is a no-op
        // unless the prompt is showing, so we can spam it without side effects.
        return TryExecuteReviveCommand(ReviveParamAccept);
    }

    private async Task WaitForReviveOrTransition(bool reissueReturn)
    {
        var deadline = Environment.TickCount64 + ReleaseTransitionWaitMs;
        var nextReissueAt = Environment.TickCount64 + ReturnReissueMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            var stillKO = IsPlayerKO();
            var transitioning = Svc.Condition[ConditionFlag.BetweenAreas]
                             || Svc.Condition[ConditionFlag.BetweenAreas51];
            if (!stillKO && !transitioning)
            {
                await NextFrame(60); // settle so weakness statuses register
                return;
            }
            // The revive window isn't accepting input the instant Unconscious flips, so a single
            // Return fire can land on nothing. Keep re-issuing until the home teleport starts.
            if (reissueReturn && stillKO && !transitioning && Environment.TickCount64 >= nextReissueAt)
            {
                TriggerReturnHome();
                nextReissueAt = Environment.TickCount64 + ReturnReissueMs;
            }
            await NextFrame(30);
        }
        Diag("Revive transition timed out; outer loop will retry.");
    }

    private static bool IsPlayerKO() => Svc.Condition[ConditionFlag.Unconscious];

    private void StartFollowUpWatch(uint parentFateId)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>();
        var parent = sheet?.GetRowOrDefault(parentFateId);
        if (parent?.HasFollowUp != true) return;
        if (followUpFateId != parentFateId)
            Diag($"Watching for follow-up to FATE {parentFateId} for {FollowUpWatchMs/1000}s");
        followUpFateId = parentFateId;
        followUpWatchUntilMs = Environment.TickCount64 + FollowUpWatchMs;
    }

    private bool ShouldWaitForFollowUp()
    {
        if (followUpFateId is not { } fateId) return false;
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>();
        var row = sheet?.GetRowOrDefault(fateId);
        if (row is null) { followUpFateId = null; return false; }

        var locationId = row.Value.Location;
        if (locationId != 0 && PublicEvent.Fates is { } fates &&
            fates.Any(f => f.Id > fateId && sheet?.GetRowOrDefault(f.Id)?.Location == locationId))
        {
            Diag($"Follow-up FATE detected for parent {fateId}; resuming");
            followUpFateId = null;
            return false;
        }

        if (Environment.TickCount64 >= followUpWatchUntilMs)
        {
            followUpFateId = null;
            return false;
        }
        return true;
    }

    private async Task ActivateFate(PublicEvent fate)
    {
        Status = $"Activating {fate.Name}";
        Diag($"FATE {fate.Id} in Preparation, walking to MotivationNpc {fate.MotivationNpcId:X}");

        var npcDeadline = Environment.TickCount64 + NpcSpawnTimeoutMs;
        while (Environment.TickCount64 < npcDeadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            if (fate.State == FateState.Running) return;
            if (fate.MotivationNpc?.IsTargetable == true) break;
            await NextFrame(30);
        }

        if (fate.State == FateState.Running) return;
        var npc = fate.MotivationNpc;
        if (npc is null || !npc.IsTargetable)
        {
            Diag($"NPC for FATE {fate.Id} never spawned within {NpcSpawnTimeoutMs/1000}s; blacklisting for session");
            sessionStuckFateIds.Add(fate.Id);
            return;
        }

        try
        {
            var activateLabel = $"Activating {fate.Name}";
            var npcPos = npc.Position;
            var move = new MoveOp(o => o.Move(zone.TerritoryId, npcPos,
                MovementConfig.InteractRange,
                allowTeleportIfFaster: false,
                stopCondition: () => { Status = activateLabel; return fate.State == FateState.Running; },
                allowAethernetWithinTerritory: false));
            await RunCancellable(move, ActivateMoveWatchdogMs, $"activate-move-{fate.Id}");

            if (fate.State == FateState.Running) return;
            if (Svc.Condition[ConditionFlag.Mounted]) await DismountViaOp($"dismount-activate-{fate.Id}");

            var interact = new MoveOp(o => o.Interact(npc,
                waitUntil: () => fate.State == FateState.Running,
                skip: UiSkipOptions.Talk | UiSkipOptions.YesNo));
            await RunCancellable(interact, NpcSpawnTimeoutMs, $"activate-interact-{fate.Id}");
        }
        catch (Exception ex)
        {
            Diag($"ActivateFate caught: {ex.Message}");
        }
    }

    // BossMod navigates around terrain only when an obstacle map is active; regenerate if we engaged
    // without one (death-teleport return, follow-up, or a FATE that popped on us).
    private async Task EnsureObstacleMapForEngage(PublicEvent fate)
    {
        if (!BossModIPC.Instance.IsAvailable) return;
        if (!fate.IsOnMap) return;
        if (BossModIPC.Instance.HasTempObstacleMap()) return;
        await GenerateObstacleMap(fate);
    }

    private enum MoveStopReason { None, StuckRetry, StuckTeleport, StuckInCombat, HigherPriority, NpcSpawned, FateInvalid }

    // After a teleport the destination zone's navmesh is still building; any obstacle-map or pathfind IPC
    // issued now races vnavmesh and faults with "navmesh creation is in progress". Hold here until ready.
    private async Task WaitForNavmeshReady()
    {
        if (NavmeshIPC.Instance.IsReady()) return;

        var deadline = Environment.TickCount64 + NavmeshReadyWaitMs;
        while (!NavmeshIPC.Instance.IsReady())
        {
            if (CancelToken.IsCancellationRequested) return;
            if (Environment.TickCount64 >= deadline)
            {
                Diag($"WAIT TIMEOUT: navmesh not ready within {NavmeshReadyWaitMs / 1000}s; proceeding anyway");
                return;
            }
            var progress = NavmeshIPC.Instance.BuildProgress();
            Status = progress is >= 0f and <= 1f
                ? $"Please wait — navmesh is loading ({progress * 100f:F0}%)"
                : "Please wait — navmesh is loading…";
            await NextFrame(60);
        }
    }

    private async Task GenerateObstacleMap(PublicEvent fate)
    {
        if (obstacleMapBlacklist.Contains(fate.Id)) return;
        if (Plugin.Cfg.RuntimeBadObstacleMaps.Contains(fate.Id)) return;
        if (!BossModIPC.Instance.IsAvailable) return;
        if (!NavmeshIPC.Instance.IsReady()) return;

        var safe = NavmeshIPC.Instance.NearestPointReachable(fate.Position, 5f, 5f);
        var anchor = safe ?? fate.Position;
        var margin = safe.HasValue ? Vector3.Distance(fate.Position, safe.Value) : 0f;
        var radius = Math.Max(fate.Radius + margin, 10f);

        if (!BossModIPC.Instance.GenerateObstacleMap(anchor, radius, writeToFile: false))
        {
            Diag($"Obstacle map generate IPC returned false for FATE {fate.Id}");
            return;
        }

        var deadline = Environment.TickCount64 + 5_000;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            var status = BossModIPC.Instance.GetObstacleMapStatus();
            if (status is null) return;
            if (status == TaskStatus.RanToCompletion) break;
            if (status == TaskStatus.Faulted || status == TaskStatus.Canceled)
            {
                Diag($"Obstacle map generation {status} for FATE {fate.Id}");
                return;
            }
            await NextFrame();
        }

        if (BossModIPC.Instance.EvaluateTempMapQualityIsBad())
        {
            Diag($"Obstacle map quality too poor for FATE {fate.Id}; clearing and adding to runtime blacklist");
            Plugin.Cfg.RuntimeBadObstacleMaps.Add(fate.Id);
            BossModIPC.Instance.ClearTempObstacleMap();
        }
    }

    private async Task<MoveStopReason> MoveToFate(PublicEvent fate)
    {
        await WaitForNavmeshReady();
        await GenerateObstacleMap(fate);

        var rnd = RandomPointInsideRadius(fate.Position, fate.Radius * 0.5f);
        var dest = rnd.OnMesh();
        if (dest == rnd)
            Diag($"OnMesh did not project FATE {fate.Id} dest {rnd}; vnav may struggle");

        var config = MovementConfig.Everything.WithTolerance(3f);
        var label = $"Moving to {fate.Name}";
        var deadline = Environment.TickCount64 + MoveToFateWatchdogMs;
        var lastRetargetAtMs = Environment.TickCount64;
        var nextProgressLogMs = Environment.TickCount64 + MoveProgressLogMs;
        var stopReason = MoveStopReason.None;
        var targetId = fate.Id;

        // Graceful exits clib can observe while it is actively following a path: a deadline backstop,
        // the FATE vanishing/finishing, its prep NPC spawning, or a closer FATE appearing. Returning
        // true here lets clib's MoveTo stop vnav and unwind on its own. Physical "stuck" is handled by
        // the abort tracker below, not here, so the two never race.
        bool StopCondition()
        {
            Status = label;

            if (Environment.TickCount64 >= deadline) { stopReason = MoveStopReason.StuckTeleport; return true; }
            if (stopReason != MoveStopReason.None) return true;

            var refreshed = PublicEvent.GetFateById(targetId);
            if (refreshed is null) { stopReason = MoveStopReason.FateInvalid; return true; }
            if (refreshed.State != FateState.Running)
            {
                // Preparing → NPC may have just spawned; bail so ActivateFate runs.
                if (refreshed.State == FateState.Preparing && refreshed.MotivationNpc?.IsTargetable == true)
                    stopReason = MoveStopReason.NpcSpawned;
                else
                    stopReason = MoveStopReason.FateInvalid;
                return true;
            }

            // Mid-path retargeting (skip when we're heading back to a FATE we died in).
            if (returnToFateId != targetId
             && Environment.TickCount64 - lastRetargetAtMs >= MidPathRetargetIntervalMs)
            {
                lastRetargetAtMs = Environment.TickCount64;
                var player = Svc.Objects.LocalPlayer;
                if (player is not null)
                {
                    var distToCurrent = Vector3.Distance(player.Position, refreshed.Position);
                    // Once we've basically reached the target, finish the trip rather than re-path.
                    if (distToCurrent > RetargetNearArrivalLockMeters)
                    {
                        var better = FateScanner.PickNext(Plugin.Cfg, player.Position, sessionStuckFateIds, null);
                        if (better is not null && better.Id != targetId
                         && Vector3.Distance(player.Position, better.Position) + RetargetDistanceMarginMeters < distToCurrent)
                        {
                            Diag($"Mid-path retarget: {targetId} -> {better.Id} ({better.Name}) (closer by >{RetargetDistanceMarginMeters:F0}m)");
                            stopReason = MoveStopReason.HigherPriority;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // Progress-or-recover: poll every frame across ALL of clib's phases (teleport, aethernet, mount,
        // pathfind, follow) and abort the move the moment forward progress stalls in a way that isn't a
        // legitimate wait. The tracker distinguishes a vnav terrain wedge from a fully-idle pre-pathfind
        // wedge (e.g. a teleport that never started casting) so neither phase is blind.
        var stuck = new TravelStuckTracker();
        bool AbortIfFrozen()
        {
            if (stopReason != MoveStopReason.None) return false;

            if (Environment.TickCount64 >= nextProgressLogMs)
            {
                nextProgressLogMs = Environment.TickCount64 + MoveProgressLogMs;
                var pp = Svc.Objects.LocalPlayer?.Position;
                var pStr = pp is { } v ? $"({v.X:F0},{v.Y:F0},{v.Z:F0})" : "?";
                Diag($"Still moving to FATE {targetId}: pos={pStr} navRun={NavmeshIPC.Instance.IsRunning()} busy={NavmeshIPC.Instance.IsBusy()} inCombat={Svc.Condition[ConditionFlag.InCombat]}");
            }

            var kind = stuck.Check();
            if (kind == StallKind.None) return false;

            stopReason = Svc.Condition[ConditionFlag.InCombat] ? MoveStopReason.StuckInCombat : MoveStopReason.StuckRetry;
            Diag(Svc.Condition[ConditionFlag.InCombat]
                ? $"Move to FATE {targetId} ({fate.Name}) stalled in combat ({kind}); cancelling to clear aggro (teleport is blocked in combat)"
                : kind == StallKind.NavWedge
                    ? $"Move to FATE {targetId} ({fate.Name}) wedged: vnav following but no progress in {HardStuckTimeoutMs/1000}s; cancelling to retry"
                    : $"Move to FATE {targetId} ({fate.Name}) idle: no nav/cast/mount progress in {IdleStallTimeoutMs/1000}s (clib teleport likely never started); cancelling to retry");
            return true;
        }

        var op = new MoveOp(o => o.Move(zone.TerritoryId, dest, config,
            allowTeleportIfFaster: !FateScanner.PlayerHasTwistOfFate(),
            stopCondition: StopCondition,
            allowAethernetWithinTerritory: true));

        var completed = await RunCancellable(op, MoveToFateWatchdogMs + MoveOpUnwindSlackMs, label, AbortIfFrozen);
        if (CancelToken.IsCancellationRequested) return MoveStopReason.None;

        // Cancelled by the hard timeout while wedged in a phase clib wasn't polling (e.g. a mount loop):
        // treat as a teleport-worthy stuck.
        if (!completed && stopReason == MoveStopReason.None)
            stopReason = MoveStopReason.StuckTeleport;

        // A clib fault (pathfind/teleport failure) completes the op without arriving; don't mistake it for
        // a clean arrival. Retry from here — MoveAndArrive escalates to a teleport if it recurs.
        if (stopReason == MoveStopReason.None && op.Fault is { } fault)
        {
            Diag($"Move to FATE {targetId} ({fate.Name}) faulted: {fault.Message}; retrying");
            stopReason = MoveStopReason.StuckRetry;
        }

        if (stopReason != MoveStopReason.None) return stopReason;

        // Clean arrival. clib only dismounts when it lands inside tolerance; a flying mount routinely
        // stops a few metres ABOVE the point (the Y gap), so it would otherwise enter the FATE still
        // mounted. Dismount explicitly — clib's Dismount descends to a reachable point first when in
        // flight — as its own cancellable op so a failed landing can't park the run.
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            var dismount = new MoveOp(o => o.DismountNow());
            await RunCancellable(dismount, DismountWatchdogMs, $"dismount-{targetId}");
        }
        return MoveStopReason.None;
    }

    // Every clib movement primitive — including dismount in combat/engage — goes through a cancellable
    // MoveOp so a wedged op (e.g. clib can't find a landing point in flight) can never park the parent
    // loop. The parent never awaits a raw clib MoveTo/Teleport/Dismount directly.
    private Task DismountViaOp(string label)
        => RunCancellable(new MoveOp(o => o.DismountNow()), DismountWatchdogMs, label);

    private async Task<bool> TryTeleportToFate(PublicEvent fate)
    {
        var before = Svc.Objects.LocalPlayer?.Position;
        Status = $"Teleporting to {fate.Name}";
        Diag($"Teleport recovery to FATE {fate.Id} ({fate.Position})");

        var fatePos = fate.Position;
        // Same-zone teleport to the aetheryte nearest the FATE. Idle-stall guard catches a teleport that
        // never starts casting in ~8s instead of the full watchdog.
        var tp = new MoveOp(o => o.Teleport(zone.TerritoryId, fatePos, allowSameZoneTeleport: true));
        if (!await RunCancellable(tp, TeleportWatchdogMs, $"teleport-recovery-{fate.Id}", IdleStallAbort(IdleStallTimeoutMs)))
            return false;
        var aeth = new MoveOp(o => o.Aethernet(zone.TerritoryId, fatePos));
        await RunCancellable(aeth, AethernetWatchdogMs, $"aethernet-recovery-{fate.Id}", IdleStallAbort(IdleStallTimeoutMs));

        var after = Svc.Objects.LocalPlayer?.Position;
        if (before is null || after is null) return false;

        var moved = Vector3.Distance(before.Value, after.Value);
        if (moved < TeleportRetryProgressMeters)
        {
            Diag($"Teleport moved only {moved:F1}m; treating as failed");
            return false;
        }
        return true;
    }

    // Teleport is blocked in combat, so fight off a mob that aggroed mid-travel (auto-target) to drop
    // combat before the loop re-paths.
    private async Task ClearBlockingCombat()
    {
        if (!Svc.Condition[ConditionFlag.InCombat]) return;

        Status = "Clearing aggro";
        Diag("In combat during travel; enabling rotation to fight free before resuming");

        var preset = Plugin.Cfg.CombatPresetName;
        EnsureCombatPreset(preset);
        if (Svc.Condition[ConditionFlag.Mounted]) await DismountViaOp("dismount-clearcombat");
        AssertPresetActive(preset);

        var deadline = Environment.TickCount64 + CombatClearTimeoutMs;
        try
        {
            while (Environment.TickCount64 < deadline)
            {
                if (CancelToken.IsCancellationRequested) return;
                if (!Svc.Condition[ConditionFlag.InCombat]) break;
                if (IsPlayerKO()) break;
                // A real FATE may have started on top of us; let the state machine take over.
                if (PublicEvent.CurrentFate is { State: FateState.Running }) break;
                if (Svc.Condition[ConditionFlag.Mounted]) { BossModIPC.Instance.ClearActive(); await DismountViaOp("dismount-clearcombat"); }
                AssertPresetActive(preset);
                await NextFrame(30);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
        }

        if (Svc.Condition[ConditionFlag.InCombat])
            Diag($"Still in combat after {CombatClearTimeoutMs / 1000}s of fighting; will retry travel");
    }

    internal enum StallKind { None, NavWedge, Idle }

    // Watches a single move for lack of forward progress. It partitions every non-progress case a clib
    // MoveTo can land in, so no phase is blind (the IsRunning-only gate used to miss the teleport phase):
    //   • NavWedge — vnav is actively following a path yet the character hasn't moved (terrain snag).
    //   • Idle     — no movement while NOTHING legitimate is happening: not following, not pathfinding,
    //                not casting/mounting/zone-transitioning. That is a wedged pre-pathfind phase, almost
    //                always a clib teleport that was issued but never started casting.
    // Anything legitimate (movement, vnav busy, or a frozen-legit state like a teleport cast) resets the
    // matching timer, so neither false-fires. Both surface as StuckRetry/StuckInCombat; the caller
    // escalates to a teleport-recovery only if the same FATE stalls again, so a transient block is given
    // a chance to clear on retry before we resort to teleporting.
    private sealed class TravelStuckTracker
    {
        private Vector3? lastPos;
        private long navWedgeSinceMs = Environment.TickCount64;
        private long idleSinceMs = Environment.TickCount64;

        public StallKind Check()
        {
            var player = Svc.Objects.LocalPlayer;
            if (player is null) return StallKind.None;

            var now = Environment.TickCount64;
            var pos = player.Position;

            // lastPos is a PERSISTENT anchor, advanced only once we've actually displaced past the
            // threshold — NOT every poll. (Resetting it each poll made steady travel look stationary,
            // because <1.5m moves between 67ms polls never cleared the threshold, false-firing the wedge
            // timer mid-flight.) Real displacement resets both timers; "stuck" is measured from the
            // anchor.
            if (lastPos is null || Vector3.Distance(lastPos.Value, pos) > StuckMoveThresholdMeters)
            {
                lastPos = pos;
                navWedgeSinceMs = now;
                idleSinceMs = now;
                return StallKind.None;
            }

            var legitFrozen = IsPositionFrozenLegit();
            var navRunning = NavmeshIPC.Instance.IsRunning();
            var navBusy = NavmeshIPC.Instance.IsBusy(); // running OR pathfind-in-progress

            // vnav following but no displacement from the anchor → terrain snag.
            if (legitFrozen || !navRunning) navWedgeSinceMs = now;
            else if (now - navWedgeSinceMs >= HardStuckTimeoutMs) return StallKind.NavWedge;

            // No displacement and nothing legitimate in progress → wedged pre-pathfind phase.
            if (legitFrozen || navBusy) idleSinceMs = now;
            else if (now - idleSinceMs >= IdleStallTimeoutMs) return StallKind.Idle;

            return StallKind.None;
        }
    }

    private void EnsureCombatPreset(string preset)
    {
        if (presetEnsured) return;
        if (preset != DefaultCombatPreset.Name) { presetEnsured = true; return; }

        if (BossModIPC.Instance.GetPreset(preset) is null)
        {
            Diag($"Default preset '{preset}' missing from BossMod, creating it.");
            if (!BossModIPC.Instance.CreatePreset(DefaultCombatPreset.GetSerialized(), overwrite: false))
                Diag($"BossMod.Presets.Create returned false for '{preset}'.");
        }
        presetEnsured = true;
    }

    private void AssertPresetActive(string preset)
    {
        if (BossModIPC.Instance.GetActive() == preset) return;

        if (!BossModIPC.Instance.SetActive(preset))
        {
            Diag($"BossMod.Presets.SetActive('{preset}') returned false — preset may not exist.");
            return;
        }
        BossModIPC.Instance.AddTransientStrategy(preset, "BossMod.Autorotation.MiscAI.AutoTarget", "MaxTargets", PullSize().ToString());
    }

    private static unsafe void SyncToFate(uint fateId)
    {
        var mgr = CSFateManager.Instance();
        if (mgr is null) return;
        if (mgr->CurrentFate is null) return;
        if (mgr->CurrentFate->FateId != fateId) return;
        if (mgr->SyncedFateId == fateId) return;
        mgr->LevelSync();
    }

    private static int PullSize()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return 3;
        var role = player.ClassJob.Value.Role;
        return role switch
        {
            1 => 0,
            4 => 5,
            _ => 3,
        };
    }

    private static Vector3 RandomPointInsideRadius(Vector3 center, float radius)
    {
        var angle = rng.NextDouble() * Math.PI * 2;
        var r = (float)(Math.Sqrt(rng.NextDouble()) * radius);
        return new Vector3(
            center.X + (float)Math.Cos(angle) * r,
            center.Y,
            center.Z + (float)Math.Sin(angle) * r);
    }

    private bool StopConditionMet()
        => Plugin.Cfg.ActiveMode.IsComplete(new ModeContext { CompletedCount = session.CompletedCount, Zones = zones });

    private bool AdvanceClassQueueIfCapHit()
    {
        var cfg = Plugin.Cfg;
        if (!cfg.ApplyClassOnStart) return false;
        if (cfg.ClassQueue.Count == 0) return false;

        var idx = ClassSwitcher.FindActiveEntryIndex(cfg.ClassQueue);
        if (idx < 0)
        {
            if (cfg.AfterClassQueueDone == AfterClassQueueDone.StopRun)
            {
                Status = "Class queue done";
                Diag("All queued classes hit their level caps, stopping run");
                return true;
            }
            return false;
        }

        var entry = cfg.ClassQueue[idx];
        var jobId = ClassSwitcher.JobIdForUserIndex(entry.GearsetIndex);
        var currentJob = Svc.Objects.LocalPlayer?.ClassJob.RowId ?? 0;
        if (jobId == 0 || jobId == currentJob) return false;

        Diag($"Class cap reached; switching to gearset {entry.GearsetIndex} ({ClassSwitcher.JobNameForUserIndex(entry.GearsetIndex)})");
        ClassSwitcher.TryEquip(entry);
        return false;
    }

    private static bool textAdvanceArmed;
    private const string TextAdvanceScope = "AutoFateGrind";

    private static void EnableTextAdvanceForCollect()
    {
        if (textAdvanceArmed) return;
        if (!ExternalPlugins.IsInstalled(ExternalPlugin.TextAdvance)) return;
        try
        {
            TextAdvanceIPC.EnableExternalControl(TextAdvanceScope, talkSkip: true, requestFill: true, requestHandin: true);
            textAdvanceArmed = true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[AFG] TextAdvance enable failed");
        }
    }

    private static void DisableTextAdvance()
    {
        if (!textAdvanceArmed) return;
        try { TextAdvanceIPC.DisableExternalControl(TextAdvanceScope); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[AFG] TextAdvance disable failed"); }
        textAdvanceArmed = false;
    }
}

public sealed class AutoFateSession
{
    public int CompletedCount;
    public DateTime StartedAt = DateTime.UtcNow;
    public int GemstoneStart;
    public int GemstoneCurrent;

    public ZoneInfo? PendingTradeFromZone;
    public bool PendingRepair;
    public ZoneInfo? PendingRepairFromZone;

    public TimeSpan Elapsed => DateTime.UtcNow - StartedAt;
    public int GemstonesEarned => Math.Max(0, GemstoneCurrent - GemstoneStart);
    public double FatesPerHour => Elapsed.TotalHours > 0 ? CompletedCount / Elapsed.TotalHours : 0;
}
