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
using ECommons.Automation;
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
    private const int   StuckMoveTimeoutMs = 2_000;
    private const int   VnavIdleTimeoutMs = 1_500;
    // Absolute no-physical-movement floor that fires even while casting / in combat, so a wandering
    // mob that roots us mid-travel recovers in seconds instead of falling through to the wall-clock.
    private const int   HardStuckTimeoutMs = 8_000;
    private const float TeleportRetryProgressMeters = 3.0f;
    private const int   MoveToFateWatchdogMs = 60_000;
    private const int   MoveToUnwindGraceMs = 5_000;
    private const int   MoveProgressLogMs = 15_000;
    private const int   FollowUpWatchMs = 15_000;
    private const int   NpcSpawnTimeoutMs = 30_000;
    private const int   CollectExpiryTimeoutMs = 90_000;
    private const int   EngageStallTimeoutMs = 60_000;
    private const int   EngageOutOfCombatGraceMs = 30_000;
    private const int   RaiseWaitMs = 30_000;
    private const int   ReleaseTransitionWaitMs = 60_000;
    private const int   IdleScansBeforeSwap = 30;
    private const int   MidPathRetargetIntervalMs = 1_500;
    private const int   TeleportWatchdogMs = 60_000;
    private const int   TerritoryWaitMs = 45_000;
    private const int   AethernetWatchdogMs = 60_000;
    private const int   ActivateMoveWatchdogMs = 60_000;
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

    // Game-side revive command IDs (resolved via GameMain.Instance()->ExecuteCommand).
    private const uint ReviveCommandId = 0x115;
    private const int  ReviveParamReturn = 0;
    private const int  ReviveParamAccept = 5;

    private enum GrindState
    {
        Idle,                 // Transient; player object not available, etc.
        WrongZone,            // Not in target territory.
        SwapZone,             // Rotate to next eligible zone (MaxFates or empty-current).
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
        ErrorIf(!BossModIPC.Instance.IsAvailable, "BossMod (or BossMod Reborn) not installed or not loaded.");

        if (Plugin.Cfg.ActiveMode.RotatesSharedFateZones && zone.AchievementDone)
            AdvanceZone();

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
                        Status = "All achievements done";
                        Diag("All selected zones finished, exiting");
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

        if (Plugin.Cfg.ActiveMode.RotatesSharedFateZones && zone.AchievementDone)
            return GrindState.SwapZone;

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
        if (zones.Count <= 1)
            return !Plugin.Cfg.ActiveMode.RotatesSharedFateZones || !zone.AchievementDone;

        for (var step = 1; step <= zones.Count; step++)
        {
            var candidate = (zoneIndex + step) % zones.Count;
            if (Plugin.Cfg.ActiveMode.RotatesSharedFateZones && zones[candidate].AchievementDone) continue;
            zoneIndex = candidate;
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
                    await Dismount();
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
            AchievementProgress.Request(zone.AchievementId, force: true);
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

        if (soloWait)
        {
            await TriggerReturnHome();
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
                await TriggerReturnHome();
            }
        }

        await WaitForReviveOrTransition();

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

    private async Task TriggerReturnHome()
    {
        if (!TryExecuteReviveCommand(ReviveParamReturn))
        {
            try { Chat.SendMessage("/release"); }
            catch (Exception ex) { Diag($"/release send failed: {ex.Message}"); }
        }
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

    private async Task WaitForReviveOrTransition()
    {
        var deadline = Environment.TickCount64 + ReleaseTransitionWaitMs;
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

    private enum MoveStopReason { None, StuckRetry, StuckTeleport, HigherPriority, NpcSpawned, FateInvalid }

    private async Task GenerateObstacleMap(PublicEvent fate)
    {
        if (obstacleMapBlacklist.Contains(fate.Id)) return;
        if (Plugin.Cfg.RuntimeBadObstacleMaps.Contains(fate.Id)) return;
        if (!BossModIPC.Instance.IsAvailable) return;

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
                        var better = FateScanner.PickNext(Plugin.Cfg, player.Position, sessionStuckFateIds, null);
                        if (better is not null && better.Id != targetId)
                        {
                            Diag($"Mid-path retarget: {targetId} -> {better.Id} ({better.Name})");
                            stopReason = MoveStopReason.HigherPriority;
                            return true;
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

        // Wall-clock backstop: if clib parks inside a sub-flow without polling stopCondition,
        // the watchdog forces a stop here.
        var nextProgressLogMs = Environment.TickCount64 + MoveProgressLogMs;
        while (!moveTask.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) break;
            if (Environment.TickCount64 >= nextProgressLogMs)
            {
                nextProgressLogMs = Environment.TickCount64 + MoveProgressLogMs;
                var p = Svc.Objects.LocalPlayer?.Position;
                var pStr = p is { } v ? $"({v.X:F0},{v.Y:F0},{v.Z:F0})" : "?";
                Diag($"Still moving to FATE {targetId}: pos={pStr} navRun={NavmeshIPC.Instance.IsRunning()} inCombat={Svc.Condition[ConditionFlag.InCombat]}");
            }
            await NextFrame(120);
        }

        if (!moveTask.IsCompleted)
        {
            stopReason = MoveStopReason.StuckTeleport;
            Diag($"MoveTo watchdog: no completion after {MoveToFateWatchdogMs/1000}s for FATE {targetId} ({fate.Name}); forcing vnav stop and escalating");
            try { NavmeshIPC.Instance.Stop(); } catch { /* best-effort */ }

            var graceDeadline = Environment.TickCount64 + MoveToUnwindGraceMs;
            while (!moveTask.IsCompleted && Environment.TickCount64 < graceDeadline)
                await NextFrame(120);

            if (!moveTask.IsCompleted)
                Diag($"MoveTo did not unwind within {MoveToUnwindGraceMs/1000}s of NavmeshIPC.Stop; continuing without await — task may leak");
            else
                await moveTask;

            return MoveStopReason.StuckTeleport;
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

    private sealed class StuckTracker
    {
        private Vector3? lastProgressPos;
        private long lastProgressAtMs = Environment.TickCount64;
        private long lastPathActivityAtMs = Environment.TickCount64;
        private Vector3? lastPhysicalPos;
        private long lastPhysicalMoveAtMs = Environment.TickCount64;
        private Vector3? retryPos;
        private bool retriedOnce;
        private bool wasRunning;

        public MoveStopReason Check()
        {
            var player = Svc.Objects.LocalPlayer;
            if (player is null) return MoveStopReason.None;

            var now = Environment.TickCount64;
            var pos = player.Position;

            // Genuinely position-frozen, bounded states. Reset everything including the hard floor.
            if (Svc.Condition[ConditionFlag.BetweenAreas]
             || Svc.Condition[ConditionFlag.BetweenAreas51]
             || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]
             || Svc.Condition[ConditionFlag.WatchingCutscene]
             || Svc.Condition[ConditionFlag.WatchingCutscene78])
            {
                lastProgressPos = pos;
                lastProgressAtMs = now;
                lastPhysicalPos = pos;
                lastPhysicalMoveAtMs = now;
                lastPathActivityAtMs = now;
                return MoveStopReason.None;
            }

            // Hard floor: fires even while casting / in combat. A mob that roots us mid-travel must
            // not suppress recovery (the casting flag used to do exactly that). Re-pathing won't move
            // a rooted character, so if we're in combat, teleport straight past it.
            if (lastPhysicalPos is null
             || Vector3.Distance(lastPhysicalPos.Value, pos) > StuckMoveThresholdMeters)
            {
                lastPhysicalPos = pos;
                lastPhysicalMoveAtMs = now;
            }
            else if (now - lastPhysicalMoveAtMs >= HardStuckTimeoutMs)
            {
                return Svc.Condition[ConditionFlag.InCombat]
                    ? MoveStopReason.StuckTeleport
                    : EscalateOrRetry(pos);
            }

            // Soft pause during a cast (teleport / aethernet / mount) so the fast timer doesn't
            // false-flag; the hard floor above still backstops if a cast-like state persists.
            if (Svc.Condition[ConditionFlag.Casting] || Svc.Condition[ConditionFlag.Casting87])
            {
                lastProgressPos = pos;
                lastProgressAtMs = now;
                return MoveStopReason.None;
            }

            var isRunning = NavmeshIPC.Instance.IsRunning();
            var isPathfinding = NavmeshIPC.Instance.IsBusy() && !isRunning;

            if (isRunning || isPathfinding)
                lastPathActivityAtMs = now;

            if (!isRunning)
            {
                wasRunning = false;
                lastProgressPos = pos;
                lastProgressAtMs = now;

                if (!isPathfinding && now - lastPathActivityAtMs >= VnavIdleTimeoutMs)
                    return EscalateOrRetry(pos);
                return MoveStopReason.None;
            }

            if (!wasRunning)
            {
                wasRunning = true;
                lastProgressPos = pos;
                lastProgressAtMs = now;
                return MoveStopReason.None;
            }

            if (lastProgressPos is null
             || Vector3.Distance(lastProgressPos.Value, pos) > StuckMoveThresholdMeters)
            {
                lastProgressPos = pos;
                lastProgressAtMs = now;
                return MoveStopReason.None;
            }

            if (now - lastProgressAtMs < StuckMoveTimeoutMs)
                return MoveStopReason.None;

            return EscalateOrRetry(pos);
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
