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

    private const float StuckMoveThresholdMeters = 1.5f;
    private const int   HardStuckTimeoutMs = 3_000;
    private const int   InFightStuckTimeoutMs = 2_500;
    private const float InFightTargetReachMeters = 6.0f;
    private const int   InFightJumpCooldownMs = 1_500;
    private const float TeleportRetryProgressMeters = 3.0f;
    private const int   MoveToFateWatchdogMs = 60_000;
    private const int   MoveToUnwindGraceMs = 5_000;
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
    private const int   TerritoryWaitMs = 45_000;
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
        if (!await AwaitWatchdog(TeleportTo(zone.TerritoryId, zone.CentralLanding, allowSameZoneTeleport: false), TeleportWatchdogMs, "teleport-to-zone"))
        {
            await NextFrame(60);
            return;
        }
        await AwaitWatchdog(WaitUntilTerritory(zone.TerritoryId), TerritoryWaitMs, "wait-territory-zone");
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
        var inFightStuck = new InFightStuckTracker();
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
                    await Dismount();
                    AssertPresetActive(preset);
                }
                else
                {
                    AssertPresetActive(preset);
                }

                SyncToFate(fateId);

                if (inFightStuck.ShouldJump())
                {
                    Diag($"In-fight stuck on FATE {fateId}: target out of reach with no movement for {InFightStuckTimeoutMs/1000.0:0.#}s; jumping to unstick");
                    Jump();
                }

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
            try
            {
                if (await AwaitWatchdog(TeleportTo(zone.TerritoryId, retFate.Position, allowSameZoneTeleport: true), TeleportWatchdogMs, $"revive-return-tp-{retId}"))
                    await AwaitWatchdog(UseAethernet(zone.TerritoryId, retFate.Position), AethernetWatchdogMs, $"revive-return-aethernet-{retId}");
            }
            catch (Exception ex)
            {
                Diag($"Return-to-FATE teleport threw: {ex.Message}");
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
            var moveTask = MoveTo(zone.TerritoryId, npc.Position,
                MovementConfig.InteractRange,
                allowTeleportIfFaster: false,
                stopCondition: () =>
                {
                    Status = activateLabel;
                    return fate.State == FateState.Running;
                },
                onStopReached: null,
                allowAethernetWithinTerritory: false);
            await AwaitWatchdog(moveTask, ActivateMoveWatchdogMs, $"activate-move-{fate.Id}");

            if (fate.State == FateState.Running) return;
            if (Svc.Condition[ConditionFlag.Mounted]) await Dismount();

            var interactTask = InteractWith(npc,
                waitUntil: () => fate.State == FateState.Running,
                selectStringIndex: null,
                skip: UiSkipOptions.Talk | UiSkipOptions.YesNo);
            await AwaitWatchdog(interactTask, NpcSpawnTimeoutMs, $"activate-interact-{fate.Id}", stopNavOnTimeout: false);
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

    private static unsafe void Jump()
    {
        var am = ActionManager.Instance();
        if (am is null) return;
        am->UseAction(ActionType.GeneralAction, 2); // GeneralAction #2 = Jump
    }

    // Jumps only when a target sits beyond reach yet we haven't physically moved — BossMod wedged on
    // terrain — so it stays silent while legitimately stood in a hitbox. A jump never repositions.
    private sealed class InFightStuckTracker
    {
        private Vector3? lastPos;
        private long lastMoveAtMs = Environment.TickCount64;
        private long lastJumpAtMs;

        public bool ShouldJump()
        {
            var player = Svc.Objects.LocalPlayer;
            if (player is null) return false;

            var now = Environment.TickCount64;
            var pos = player.Position;

            var target = player.TargetObject;
            if (target is null
             || !Svc.Condition[ConditionFlag.InCombat]
             || Svc.Condition[ConditionFlag.Mounted]
             || IsPositionFrozenLegit()
             || Vector3.Distance(pos, target.Position) <= InFightTargetReachMeters)
            {
                lastPos = pos;
                lastMoveAtMs = now;
                return false;
            }

            if (lastPos is null || Vector3.Distance(lastPos.Value, pos) > StuckMoveThresholdMeters)
            {
                lastPos = pos;
                lastMoveAtMs = now;
                return false;
            }

            if (now - lastMoveAtMs < InFightStuckTimeoutMs) return false;
            if (now - lastJumpAtMs < InFightJumpCooldownMs) return false;

            lastJumpAtMs = now;
            return true;
        }
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
        var stuck = new StuckTracker();
        var label = $"Moving to {fate.Name}";
        var deadline = Environment.TickCount64 + MoveToFateWatchdogMs;
        var lastRetargetAtMs = Environment.TickCount64;
        var stopReason = MoveStopReason.None;
        var targetId = fate.Id;

        var moveTask = MoveTo(zone.TerritoryId, dest, config,
            allowTeleportIfFaster: !FateScanner.PlayerHasTwistOfFate(),
            stopCondition: () =>
            {
                Status = label;

                if (Environment.TickCount64 >= deadline)
                {
                    stopReason = MoveStopReason.StuckTeleport;
                    return true;
                }

                if (stopReason != MoveStopReason.None) return true;

                var refreshed = PublicEvent.GetFateById(targetId);
                if (refreshed is null)
                {
                    stopReason = MoveStopReason.FateInvalid;
                    return true;
                }
                if (refreshed.State != FateState.Running)
                {
                    // Preparing → NPC may have just spawned; bail so ActivateFate runs.
                    if (refreshed.State == FateState.Preparing && refreshed.MotivationNpc?.IsTargetable == true)
                    {
                        stopReason = MoveStopReason.NpcSpawned;
                        return true;
                    }
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

                var stuckReason = stuck.Check();
                if (stuckReason != MoveStopReason.None)
                {
                    stopReason = stuckReason;
                    return true;
                }
                return false;
            },
            onStopReached: null,
            allowAethernetWithinTerritory: true);

        var nextProgressLogMs = Environment.TickCount64 + MoveProgressLogMs;
        // Wall-clock backstop for when clib parks without polling stopCondition (so the StuckTracker
        // can't see it): catch the physical freeze in ~8s instead of waiting out the 60s watchdog.
        Vector3? wallclockLastPos = Svc.Objects.LocalPlayer?.Position;
        var wallclockLastMoveAtMs = Environment.TickCount64;
        var wallclockStall = MoveStopReason.None;
        while (!moveTask.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) break;

            var nowPos = Svc.Objects.LocalPlayer?.Position;
            if (nowPos is { } np)
            {
                // Only a vnav-driven freeze is recoverable by stopping nav. During clib's sequential
                // teleport/aethernet/mount phases vnav isn't busy and standing still is legitimate;
                // counting it as a wedge spuriously escalated to teleport during mount-up and on
                // arrival (and stacked teleports against clib's own loop). Gate on IsBusy.
                if (!NavmeshIPC.Instance.IsBusy()
                 || wallclockLastPos is null
                 || Vector3.Distance(wallclockLastPos.Value, np) > StuckMoveThresholdMeters)
                {
                    wallclockLastPos = np;
                    wallclockLastMoveAtMs = Environment.TickCount64;
                }
                else if (Environment.TickCount64 - wallclockLastMoveAtMs >= HardStuckTimeoutMs
                      && !IsPositionFrozenLegit())
                {
                    // Teleport is blocked in combat, so fight free first; otherwise escalate to teleport.
                    wallclockStall = Svc.Condition[ConditionFlag.InCombat]
                        ? MoveStopReason.StuckInCombat
                        : MoveStopReason.StuckTeleport;
                    break;
                }
            }

            if (Environment.TickCount64 >= nextProgressLogMs)
            {
                nextProgressLogMs = Environment.TickCount64 + MoveProgressLogMs;
                var p = Svc.Objects.LocalPlayer?.Position;
                var pStr = p is { } v ? $"({v.X:F0},{v.Y:F0},{v.Z:F0})" : "?";
                Diag($"Still moving to FATE {targetId}: pos={pStr} navRun={NavmeshIPC.Instance.IsRunning()} inCombat={Svc.Condition[ConditionFlag.InCombat]}");
            }
            await NextFrame(120);
        }

        // Any path where the move didn't finish cleanly must tear moveTask down before we hand off: a
        // still-running MoveTo keeps driving vnav and fights whatever runs next. (This was the combat
        // break — the old combat-stall returned without unwinding, leaving a zombie that re-pathed
        // under ClearBlockingCombat and wedged the run.) Set stopReason so the closure unwinds if clib
        // still polls it, stop vnav, grace-wait, and abandon (observed) only if it refuses to die.
        if (wallclockStall != MoveStopReason.None || !moveTask.IsCompleted)
        {
            if (stopReason == MoveStopReason.None)
                stopReason = wallclockStall != MoveStopReason.None ? wallclockStall : MoveStopReason.StuckTeleport;

            Diag(wallclockStall == MoveStopReason.StuckInCombat
                ? $"MoveTo combat-stall: stationary in combat {HardStuckTimeoutMs/1000}s with no nav completion for FATE {targetId} ({fate.Name}); stopping vnav and clearing aggro (teleport is blocked in combat)"
                : wallclockStall == MoveStopReason.StuckTeleport
                    ? $"MoveTo froze: no physical progress in {HardStuckTimeoutMs/1000}s and not in a frozen-legit state for FATE {targetId} ({fate.Name}); forcing vnav stop and escalating"
                    : $"MoveTo watchdog: no completion after {MoveToFateWatchdogMs/1000}s for FATE {targetId} ({fate.Name}); forcing vnav stop and escalating");
            try { NavmeshIPC.Instance.Stop(); } catch { /* best-effort */ }

            var graceDeadline = Environment.TickCount64 + MoveToUnwindGraceMs;
            while (!moveTask.IsCompleted && Environment.TickCount64 < graceDeadline)
                await NextFrame(120);

            if (!moveTask.IsCompleted)
            {
                Diag($"MoveTo did not unwind within {MoveToUnwindGraceMs/1000}s of NavmeshIPC.Stop; abandoning task (fault observed)");
                ObserveLeak(moveTask);
            }
            else
                await moveTask;

            return stopReason;
        }

        await moveTask;

        if (stopReason != MoveStopReason.None) return stopReason;

        if (Svc.Condition[ConditionFlag.Mounted]) await Dismount();
        return MoveStopReason.None;
    }

    private async Task<bool> TryTeleportToFate(PublicEvent fate)
    {
        var before = Svc.Objects.LocalPlayer?.Position;
        Status = $"Teleporting to {fate.Name}";
        Diag($"Teleport recovery to FATE {fate.Id} ({fate.Position})");

        try
        {
            if (!await AwaitWatchdog(TeleportTo(zone.TerritoryId, fate.Position, allowSameZoneTeleport: true), TeleportWatchdogMs, $"teleport-recovery-{fate.Id}"))
                return false;
            await AwaitWatchdog(UseAethernet(zone.TerritoryId, fate.Position), AethernetWatchdogMs, $"aethernet-recovery-{fate.Id}");
        }
        catch (Exception ex)
        {
            Diag($"Teleport recovery threw: {ex.Message}");
            return false;
        }

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
        if (Svc.Condition[ConditionFlag.Mounted]) await Dismount();
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
                if (Svc.Condition[ConditionFlag.Mounted]) { BossModIPC.Instance.ClearActive(); await Dismount(); }
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

    // Excludes Mounted: a mount stuck on terrain is a real freeze.
    private static bool IsPositionFrozenLegit()
        => Svc.Condition[ConditionFlag.Casting]
        || Svc.Condition[ConditionFlag.Casting87]
        || Svc.Condition[ConditionFlag.BetweenAreas]
        || Svc.Condition[ConditionFlag.BetweenAreas51]
        || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]
        || Svc.Condition[ConditionFlag.WatchingCutscene]
        || Svc.Condition[ConditionFlag.WatchingCutscene78];

    // Observe an abandoned MoveTo's eventual fault so it can't reach the finalizer as an unobserved
    // exception. Must not touch nav — a later firing could stop the next FATE's navigation.
    private static void ObserveLeak(Task leaked)
        => leaked.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);

    private sealed class StuckTracker
    {
        private Vector3? lastPhysicalPos;
        private long lastPhysicalMoveAtMs = Environment.TickCount64;
        private Vector3? retryPos;
        private bool retriedOnce;

        // Wedged = no physical movement for HardStuckTimeoutMs outside a legit frozen state. Teleport
        // is blocked in combat, so signal clear-aggro there; otherwise retry-then-teleport.
        public MoveStopReason Check()
        {
            var player = Svc.Objects.LocalPlayer;
            if (player is null) return MoveStopReason.None;

            var now = Environment.TickCount64;
            var pos = player.Position;

            // Stillness is only a wedge while vnav is actively driving us. A mob rooting us mid-path
            // keeps vnav running (IsBusy true), so combat recovery still triggers; clib's own
            // teleport/aethernet/mount phases leave vnav idle, where standing still is expected.
            if (!NavmeshIPC.Instance.IsBusy() || IsPositionFrozenLegit())
            {
                lastPhysicalPos = pos;
                lastPhysicalMoveAtMs = now;
                return MoveStopReason.None;
            }

            if (lastPhysicalPos is null
             || Vector3.Distance(lastPhysicalPos.Value, pos) > StuckMoveThresholdMeters)
            {
                lastPhysicalPos = pos;
                lastPhysicalMoveAtMs = now;
                return MoveStopReason.None;
            }

            if (now - lastPhysicalMoveAtMs < HardStuckTimeoutMs)
                return MoveStopReason.None;

            return Svc.Condition[ConditionFlag.InCombat]
                ? MoveStopReason.StuckInCombat
                : EscalateOrRetry(pos);
        }

        private MoveStopReason EscalateOrRetry(Vector3 currentPos)
        {
            if (retriedOnce && retryPos.HasValue
                && Vector3.Distance(currentPos, retryPos.Value) <= 3f)
                return MoveStopReason.StuckTeleport;

            retryPos = currentPos;
            retriedOnce = true;
            return MoveStopReason.StuckRetry;
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

    public TimeSpan Elapsed => DateTime.UtcNow - StartedAt;
    public int GemstonesEarned => Math.Max(0, GemstoneCurrent - GemstoneStart);
    public double FatesPerHour => Elapsed.TotalHours > 0 ? CompletedCount / Elapsed.TotalHours : 0;
}
