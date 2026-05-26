using AutoFateGrind.Core.Game;
using AutoFateGrind.Core.Ipc;
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

public sealed class AutoFate(IReadOnlyList<ZoneInfo> zones, AutoFateSession session, int startIndex = 0) : AutoCommon
{
    private readonly IReadOnlyList<ZoneInfo> zones = zones;
    private readonly AutoFateSession session = session;
    private int zoneIndex = zones.Count == 0 ? 0 : Math.Clamp(startIndex, 0, zones.Count - 1);
    private ZoneInfo zone => zones[zoneIndex];

    // Session-scoped so transient blockers get a fresh attempt next run.
    private readonly HashSet<uint> sessionStuckFateIds = new();

    // Known-bad terrain for BossMod's obstacle map generator.
    private static readonly HashSet<uint> obstacleMapBlacklist = new() { 1831, 1832, 1914, 1915 };

    private const float StuckMoveThresholdMeters = 1.5f;
    private const int StuckMoveTimeoutMs = 2_000;
    private const int FollowUpWatchMs = 15_000;
    private const int VnavIdleTimeoutMs = 1_500;
    private const float TeleportRetryProgressMeters = 3.0f;

    private uint? lastStuckFateId;
    private int consecutiveStuckRetries;
    private uint? lastTeleportedFateId;

    private static readonly Random rng = new();

    protected override async Task Execute()
    {
        ErrorIf(zones.Count == 0, "No zones to grind.");

        if (Plugin.Cfg.Mode == GrindMode.MaxFates && zone.AchievementDone)
            AdvanceZone();

        Svc.Chat.Print($"[AFG] Starting {zone.Name}...");
        try
        {
            await ExecuteInner();
            Svc.Chat.Print($"[AFG] {zone.Name}: zone done.");
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            var lastBracket = msg.LastIndexOf("] ");
            if (lastBracket >= 0) msg = msg[(lastBracket + 2)..];
            Svc.Chat.PrintError($"[AFG] {zone.Name} stopped: {msg}");
            throw;
        }
    }

    private bool AdvanceZone()
    {
        if (zones.Count <= 1)
        {
            return Plugin.Cfg.Mode != GrindMode.MaxFates || !zone.AchievementDone;
        }
        for (var step = 1; step <= zones.Count; step++)
        {
            var candidate = (zoneIndex + step) % zones.Count;
            if (Plugin.Cfg.Mode == GrindMode.MaxFates && zones[candidate].AchievementDone) continue;
            zoneIndex = candidate;
            sessionStuckFateIds.Clear();
            lastStuckFateId = null;
            consecutiveStuckRetries = 0;
            lastTeleportedFateId = null;
            return true;
        }
        return false;
    }

    private async Task ExecuteInner()
    {
        ErrorIf(!BossModIPC.Instance.IsAvailable, "BossMod (or BossMod Reborn) not installed or not loaded.");

        var idleScans = 0;
        const int idleScanLimitNoFates = 30;

        while (!CancelToken.IsCancellationRequested)
        {
            // Run before any in-zone work — released-to-home is recovered by the territory check below.
            if (await HandleKoIfNeeded()) continue;

            if (Svc.ClientState.TerritoryType != zone.TerritoryId)
            {
                Status = $"Teleporting to {zone.Name}";
                Diag($"Off-zone (in {Svc.ClientState.TerritoryType}), teleporting to {zone.TerritoryId}");
                await TeleportTo(zone.TerritoryId, zone.CentralLanding, allowSameZoneTeleport: false);
                await WaitUntilTerritory(zone.TerritoryId);
                continue;
            }

            if (StopConditionMet())
            {
                Status = "Stop condition met";
                Diag("Global stop condition tripped, exiting zone");
                return;
            }

            // Rotate to the next unfinished zone; only exit when none remain.
            if (Plugin.Cfg.Mode == GrindMode.MaxFates && zone.AchievementDone)
            {
                Diag($"{zone.Name} achievement done");
                if (!AdvanceZone())
                {
                    Status = "All achievements done";
                    Diag("All selected zones finished, exiting");
                    return;
                }
                idleScans = 0;
                continue;
            }

            var player = Svc.Objects.LocalPlayer;
            if (player is null)
            {
                await NextFrame();
                continue;
            }

            var fate = FateScanner.PickNext(Plugin.Cfg, player.Position, sessionStuckFateIds);
            if (fate is null)
            {
                idleScans++;
                if (Plugin.Cfg.SwapZonesWhenEmpty && zones.Count > 1)
                {
                    Status = $"No eligible FATEs ({idleScans}/{idleScanLimitNoFates})";
                    if (idleScans >= idleScanLimitNoFates)
                    {
                        if (!AdvanceZone())
                        {
                            Status = "All achievements done";
                            Diag("No eligible FATEs in any selected zone, exiting");
                            return;
                        }
                        Status = $"Swapping to {zone.Name}";
                        Diag($"Swapping to {zone.Name}");
                        idleScans = 0;
                        continue;
                    }
                }
                else
                {
                    Status = $"Waiting for FATEs in {zone.Name}";
                }
                await NextFrame(60);
                continue;
            }

            idleScans = 0;
            Status = $"Moving to {fate.Name}";
            Diag($"Picked FATE {fate.Id} ({fate.Name}) at {fate.Position}");

            var moveResult = await MoveToFate(fate);
            if (CancelToken.IsCancellationRequested) return;

            // Already teleported once and still stuck — blacklist to break the teleport→stuck loop.
            if (lastTeleportedFateId == fate.Id && moveResult != MoveStopReason.None)
            {
                Diag($"Still stuck after teleport recovery for FATE {fate.Id} ({fate.Name}); blacklisting for this session");
                sessionStuckFateIds.Add(fate.Id);
                lastTeleportedFateId = null;
                lastStuckFateId = null;
                consecutiveStuckRetries = 0;
                continue;
            }

            // Ladder: first stuck → retry; second stuck on same FATE → teleport; third → blacklist.
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
                    continue;
                }
            }

            if (moveResult == MoveStopReason.StuckTeleport)
            {
                if (await TryTeleportToFate(fate))
                {
                    lastTeleportedFateId = fate.Id;
                    lastStuckFateId = null;
                    consecutiveStuckRetries = 0;
                    continue;
                }

                sessionStuckFateIds.Add(fate.Id);
                lastTeleportedFateId = null;
                lastStuckFateId = null;
                consecutiveStuckRetries = 0;
                Diag($"Teleport recovery failed for FATE {fate.Id}; blacklisting for this session");
                continue;
            }

            if (lastStuckFateId == fate.Id) { lastStuckFateId = null; consecutiveStuckRetries = 0; }
            if (lastTeleportedFateId == fate.Id) lastTeleportedFateId = null;

            if (await HandleKoIfNeeded()) continue;

            // Boss FATEs need the issuing NPC talked to before Running. 0xE0000000 = no NPC.
            if (fate.State == FateState.Preparing && fate.MotivationNpcId != 0xE0000000)
                await ActivateFate(fate);
            if (CancelToken.IsCancellationRequested) return;
            if (await HandleKoIfNeeded()) continue;

            await EngageFate(fate);
            if (CancelToken.IsCancellationRequested) return;
            if (await HandleKoIfNeeded()) continue;

            // Collect FATEs award rewards on row-expiry, not Progress==100.
            if (fate.Rule == PublicEvent.FateRule.Collect)
                await WaitForFateExpiry(fate.Id);

            session.CompletedCount++;
            zone.CompletedThisRun++;
            session.GemstoneCurrent = GemstoneCount();
            // Force re-fetch so AchievementCurrent is fresh before StopConditionMet().
            AchievementProgress.Request(zone.AchievementId, force: true);
            Diag($"FATE {fate.Id} done (session total: {session.CompletedCount})");

            // Some FATEs spawn a chained sequel at the same Location within ~15s.
            await WatchForFollowUp(fate.Id);

            if (AdvanceClassQueueIfCapHit()) return;

            if (Plugin.Cfg.TradeOnCap && session.GemstoneCurrent >= Plugin.Cfg.TradeThreshold)
            {
                var targetId = GemstoneCatalog.EnsurePersistedTarget();
                var target = targetId == 0 ? null : GemstoneCatalog.FindById(targetId);
                if (target is not null
                    && GemstoneCatalog.ComputeBuyQuantity(session.GemstoneCurrent, target.CostPerOne) > 0
                    && GemstoneTrader.PickForItem(targetId, zone.TerritoryId, zone.Expansion) is { } trader)
                {
                    Diag($"Gemstone threshold {Plugin.Cfg.TradeThreshold} reached, queueing auto-trade for {target.ItemName} at {trader.Name}.");
                    session.PendingTradeFromZone = zone;
                    return;
                }
            }
        }
    }

    private async Task<bool> HandleKoIfNeeded()
    {
        if (!IsPlayerKO()) return false;

        Status = "KO — waiting for raise";
        Diag("Player KO detected, waiting up to 30s for a raise");

        try { BossModIPC.Instance.ClearActive(); } catch { /* best-effort */ }

        const int raiseWaitMs = 30_000;
        var raiseDeadline = Environment.TickCount64 + raiseWaitMs;
        while (Environment.TickCount64 < raiseDeadline)
        {
            if (CancelToken.IsCancellationRequested) return true;
            if (!IsPlayerKO())
            {
                Status = "Raised, resuming";
                Diag("Raised by another player, resuming loop");
                // Settle so weakness/transcendent statuses register before the next move.
                await NextFrame(60);
                return true;
            }
            await NextFrame(30);
        }

        Status = "No raise — releasing to home point";
        Diag("No raise within 30s, sending /release");
        try { Chat.SendMessage("/release"); }
        catch (Exception ex) { Diag($"/release send failed: {ex.Message}"); }

        // 60s cap in case the release prompt is intercepted (party offer, etc.).
        var teleportDeadline = Environment.TickCount64 + 60_000;
        while (Environment.TickCount64 < teleportDeadline)
        {
            if (CancelToken.IsCancellationRequested) return true;
            var stillKO = IsPlayerKO();
            var transitioning = Svc.Condition[ConditionFlag.BetweenAreas]
                             || Svc.Condition[ConditionFlag.BetweenAreas51];
            if (!stillKO && !transitioning)
            {
                Diag($"Released, now in territory {Svc.ClientState.TerritoryType}");
                await NextFrame(60);
                return true;
            }
            await NextFrame(30);
        }

        Diag("Release timed out, falling through; outer loop will retry");
        return true;
    }

    private static bool IsPlayerKO() => Svc.Condition[ConditionFlag.Unconscious];

    private async Task ActivateFate(PublicEvent fate)
    {
        Status = $"Activating {fate.Name}";
        Diag($"FATE {fate.Id} in Preparation, walking to MotivationNpc {fate.MotivationNpcId:X}");

        await WaitUntil(
            condition: () => (fate.MotivationNpc?.IsTargetable ?? false) || fate.State == FateState.Running,
            scopeName: "wait-npc-spawn",
            checkFrequency: 30,
            logContinuously: false);

        if (fate.State == FateState.Running) return;
        var npc = fate.MotivationNpc;
        if (npc is null || !npc.IsTargetable) return;

        try
        {
            var activateLabel = $"Activating {fate.Name}";
            await MoveTo(zone.TerritoryId, npc.Position,
                MovementConfig.InteractRange,
                allowTeleportIfFaster: false,
                stopCondition: () =>
                {
                    Status = activateLabel;
                    return fate.State == FateState.Running;
                },
                onStopReached: null,
                allowAethernetWithinTerritory: false);

            if (fate.State == FateState.Running) return;
            if (Svc.Condition[ConditionFlag.Mounted]) await Dismount();

            await InteractWith(npc,
                waitUntil: () => fate.State == FateState.Running,
                selectStringIndex: null,
                skip: UiSkipOptions.Talk | UiSkipOptions.YesNo);
        }
        catch (Exception ex)
        {
            Diag($"ActivateFate caught: {ex.Message}");
        }
    }

    private async Task WaitForFateExpiry(uint fateId)
    {
        Status = "Waiting for Collect rewards";
        Diag($"Collect FATE {fateId} done, holding in zone until row expires");
        await WaitUntil(
            condition: () => PublicEvent.GetFateById(fateId) is null,
            scopeName: $"wait-collect-expiry:{fateId}",
            checkFrequency: 60,
            logContinuously: false);
    }

    private async Task WatchForFollowUp(uint parentFateId)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>();
        var parent = sheet?.GetRowOrDefault(parentFateId);
        if (parent?.HasFollowUp != true) return;

        var locationId = parent.Value.Location;
        if (locationId == 0) return;

        Status = "Watching for follow-up FATE";
        var deadline = Environment.TickCount64 + FollowUpWatchMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            var matched = PublicEvent.Fates?.Any(f =>
                f.Id > parentFateId
                && sheet?.GetRowOrDefault(f.Id)?.Location == locationId) ?? false;
            if (matched)
            {
                Diag($"Follow-up FATE detected for parent {parentFateId}; resuming");
                return;
            }
            var remaining = (deadline - Environment.TickCount64) / 1000 + 1;
            Status = $"Watching for follow-up ({remaining}s)";
            await NextFrame(100);
        }
    }

    private enum MoveStopReason { None, StuckRetry, StuckTeleport }

    // Anchor at nearest reachable point so terrain-blocked centers still get a usable map.
    private async Task GenerateObstacleMap(PublicEvent fate)
    {
        if (obstacleMapBlacklist.Contains(fate.Id)) return;
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
            if (status == TaskStatus.RanToCompletion) return;
            if (status == TaskStatus.Faulted || status == TaskStatus.Canceled)
            {
                Diag($"Obstacle map generation {status} for FATE {fate.Id}");
                return;
            }
            await NextFrame();
        }
        Diag($"Obstacle map generation timed out for FATE {fate.Id}");
    }

    private async Task<MoveStopReason> MoveToFate(PublicEvent fate)
    {
        await GenerateObstacleMap(fate);

        // Snap to navmesh — raw FATE-center Y is often inside terrain in vertical zones.
        var rnd = RandomPointInsideRadius(fate.Position, fate.Radius * 0.5f);
        var dest = rnd.OnMesh();
        if (dest == rnd)
            Diag($"OnMesh did not project FATE {fate.Id} dest {rnd}; vnav may struggle");

        var config = MovementConfig.Everything.WithTolerance(3f);
        var stuck = new StuckTracker();
        var label = $"Moving to {fate.Name}";

        await MoveTo(zone.TerritoryId, dest, config,
            allowTeleportIfFaster: !PlayerHasTwistOfFate(),
            stopCondition: () =>
            {
                Status = label;
                return fate.State != FateState.Running
                    || stuck.Check() != MoveStopReason.None;
            },
            onStopReached: null,
            allowAethernetWithinTerritory: true);

        if (stuck.Reason != MoveStopReason.None)
            return stuck.Reason;

        if (fate.State != FateState.Running)
            return MoveStopReason.None;

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
            await TeleportTo(zone.TerritoryId, fate.Position, allowSameZoneTeleport: true);
            await UseAethernet(zone.TerritoryId, fate.Position);
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

    // Distinguishes vnav-gave-up (fast fail) from player-not-moving (slow fail); pauses for cutscenes/casts.
    private sealed class StuckTracker
    {
        private Vector3? lastProgressPos;
        private long lastProgressAtMs = Environment.TickCount64;
        private long lastPathActivityAtMs = Environment.TickCount64;
        private Vector3? retryPos;
        private bool retriedOnce;
        private bool wasRunning;
        public MoveStopReason Reason { get; private set; } = MoveStopReason.None;

        public MoveStopReason Check()
        {
            if (Reason != MoveStopReason.None) return Reason;

            var player = Svc.Objects.LocalPlayer;
            if (player is null) return MoveStopReason.None;

            if (Svc.Condition[ConditionFlag.BetweenAreas]
             || Svc.Condition[ConditionFlag.BetweenAreas51]
             || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]
             || Svc.Condition[ConditionFlag.WatchingCutscene]
             || Svc.Condition[ConditionFlag.WatchingCutscene78]
             || Svc.Condition[ConditionFlag.Casting]
             || Svc.Condition[ConditionFlag.Casting87])
            {
                lastProgressPos = player.Position;
                lastProgressAtMs = Environment.TickCount64;
                return MoveStopReason.None;
            }

            var now = Environment.TickCount64;
            var pos = player.Position;
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
                {
                    Reason = EscalateOrRetry(pos);
                    return Reason;
                }
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

            Reason = EscalateOrRetry(pos);
            return Reason;
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

    private async Task EngageFate(PublicEvent fate)
    {
        var preset = Plugin.Cfg.CombatPresetName;
        Status = $"Engaging {fate.Name}";

        EnsureCombatPreset(preset);
        SyncToFate(fate.Id);
        AssertPresetActive(preset);

        try
        {
            // Per-tick re-assert covers transient deactivation (death, /vbm clear, BossMod reload).
            while (!CancelToken.IsCancellationRequested)
            {
                if (fate.State != FateState.Running) break;
                if (IsPlayerKO()) break;

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

                // Re-sync covers mid-fight level bumps and death+revive sync drops.
                SyncToFate(fate.Id);

                await NextFrame(30);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
        }
    }

    private bool presetEnsured;

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

    // Direct call mirrors the FATE-icon button so we don't depend on "Allow level sync down" setting.
    private static unsafe void SyncToFate(uint fateId)
    {
        var mgr = CSFateManager.Instance();
        if (mgr is null) return;
        if (mgr->CurrentFate is null) return;
        if (mgr->CurrentFate->FateId != fateId) return;
        if (mgr->SyncedFateId == fateId) return;
        mgr->LevelSync();
    }

    private static bool PlayerHasTwistOfFate()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return false;
        const uint twistOfFateStatusId = 1288;
        foreach (var s in player.StatusList)
            if (s.StatusId == twistOfFateStatusId) return true;
        return false;
    }

    // MaxTargets by role: tank unlimited, healer 5, DPS / other 3.
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

    private bool StopConditionMet() => Plugin.Cfg.Mode switch
    {
        GrindMode.Endless      => false,
        // Per-zone case is handled by the rotation check at the top of the loop.
        GrindMode.MaxFates     => zones.All(z => z.AchievementDone),
        GrindMode.MaxGemstones => GemstoneCount() >= Plugin.Cfg.TradeThreshold,
        GrindMode.RunCount     => session.CompletedCount >= Plugin.Cfg.TargetFateCount,
        _ => false,
    };

    // Class swap is fire-and-forget; returns true when the caller should bail out of the FATE loop.
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

    private static unsafe int GemstoneCount()
    {
        const uint bicolorItemId = 26807;
        return InventoryManager.Instance()->GetInventoryItemCount(bicolorItemId);
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
