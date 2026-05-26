using AutoFateGrind.Core.Game;
using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Zones;
using clib.TaskSystem;
using clib.Utils;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed class AutoFate(ZoneInfo zone, AutoFateSession session) : AutoCommon
{
    private readonly ZoneInfo zone = zone;
    private readonly AutoFateSession session = session;

    // FATEs this session has bailed on because pathfinding got us stuck. Scoped to this
    // task instance so it resets next run (the FATE could be reachable next time — the
    // hangup is usually transient: party member blocking a chokepoint, vnav cache, etc).
    private readonly HashSet<uint> sessionStuckFateIds = new();

    private const float StuckMoveThresholdMeters = 1.0f;
    private const int StuckMoveTimeoutMs = 8_000;

    private static readonly Random rng = new();

    protected override async Task Execute()
    {
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

    private async Task ExecuteInner()
    {
        ErrorIf(!BossModIPC.Instance.IsAvailable, "BossMod (or BossMod Reborn) not installed or not loaded.");

        var idleScans = 0;
        const int idleScanLimitNoFates = 30;

        while (!CancelToken.IsCancellationRequested)
        {
            // Recover from KO before any in-zone work. Resolves the case where the player
            // released back to a home point — once released and respawned, the territory
            // check below teleports us back to the grind zone.
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
                if (Plugin.Cfg.SwapZonesWhenEmpty)
                {
                    Status = $"No eligible FATEs ({idleScans}/{idleScanLimitNoFates})";
                    if (idleScans >= idleScanLimitNoFates)
                    {
                        Diag("No eligible FATEs after timeout, yielding to next queued zone");
                        return;
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
            Status = $"Moving to {fate.Level} {fate.Name}";
            Diag($"Picked FATE {fate.Id} ({fate.Name}) at {fate.Position}");

            if (!await MoveToFate(fate))
            {
                // Stuck en route — skip this FATE for the rest of the session and pick
                // a different one next iteration.
                sessionStuckFateIds.Add(fate.Id);
                Diag($"Stuck en route to FATE {fate.Id} ({fate.Name}); blacklisting for this session");
                continue;
            }
            if (CancelToken.IsCancellationRequested) return;
            if (await HandleKoIfNeeded()) continue;

            await EngageFate(fate);
            if (CancelToken.IsCancellationRequested) return;
            // KO during the fight — skip completion accounting and let next iter re-teleport.
            if (await HandleKoIfNeeded()) continue;

            session.CompletedCount++;
            zone.CompletedThisRun++;
            session.GemstoneCurrent = GemstoneCount();
            // Force a re-fetch so AchievementCurrent reflects the FATE we just finished
            // before the next StopConditionMet() check.
            AchievementProgress.Request(zone.AchievementId, force: true);
            Diag($"FATE {fate.Id} done (session total: {session.CompletedCount})");

            if (Plugin.Cfg.TradeOnCap
                && Plugin.Cfg.TargetTradeItemId != 0
                && session.GemstoneCurrent >= Plugin.Cfg.TradeThreshold)
            {
                Diag($"Gemstone threshold {Plugin.Cfg.TradeThreshold} reached, queueing auto-trade.");
                session.PendingTradeFromZone = zone;
                return;
            }
        }
    }

    // Returns true if a KO was detected and handled (either raised or released).
    // Caller should `continue` so the outer loop can re-evaluate territory/state.
    private async Task<bool> HandleKoIfNeeded()
    {
        if (!IsPlayerKO()) return false;

        Status = "KO — waiting for raise";
        Diag("Player KO detected, waiting up to 30s for a raise");

        // Pause the combat backend so it stops trying to act on a dead player.
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
                // Brief settle to let weakness/transcendent statuses register
                // before the next move/teleport attempt.
                await NextFrame(60);
                return true;
            }
            await NextFrame(30);
        }

        Status = "No raise — releasing to home point";
        Diag("No raise within 30s, sending /release");
        try { Chat.SendMessage("/release"); }
        catch (Exception ex) { Diag($"/release send failed: {ex.Message}"); }

        // Wait for the home-point teleport: ConditionFlag.Unconscious clears AND we are
        // not currently mid-zone-transition. Cap at 60s in case the release prompt is
        // intercepted by something else (party offer, etc.).
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

    // Returns true if we either reached the FATE or it ended/exited normally.
    // Returns false if stuck detection tripped — caller should blacklist this FATE.
    private async Task<bool> MoveToFate(PublicEvent fate)
    {
        var dest = RandomPointInsideRadius(fate.Position, fate.Radius * 0.5f);

        var config = MovementConfig.Everything.WithTolerance(3f);
        var stuck = new StuckTracker();

        await MoveTo(zone.TerritoryId, dest, config,
            allowTeleportIfFaster: !PlayerHasTwistOfFate(),
            stopCondition: () => fate.State != FateState.Running
                              || NearFate(fate)
                              || stuck.IsStuck(),
            onStopReached: null,
            allowAethernetWithinTerritory: true);

        if (stuck.Tripped && !NearFate(fate) && fate.State == FateState.Running)
            return false;

        if (Svc.Condition[ConditionFlag.Mounted]) await Dismount();
        return true;
    }

    // Position-delta watchdog. Trips when the player hasn't traveled more than
    // StuckMoveThresholdMeters within StuckMoveTimeoutMs — but only when not in a
    // state that legitimately freezes the avatar (zoning, cutscene, cast bar).
    private sealed class StuckTracker
    {
        private Vector3? lastPos;
        private long lastMoveTickMs = Environment.TickCount64;
        public bool Tripped { get; private set; }

        public bool IsStuck()
        {
            if (Tripped) return true;

            var player = Svc.Objects.LocalPlayer;
            if (player is null) return false;

            var now = Environment.TickCount64;

            // Pause the timer during legitimate stationary states; otherwise an 8s
            // teleport cast or a zone load would trip false positives.
            if (Svc.Condition[ConditionFlag.BetweenAreas]
             || Svc.Condition[ConditionFlag.BetweenAreas51]
             || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]
             || Svc.Condition[ConditionFlag.WatchingCutscene]
             || Svc.Condition[ConditionFlag.WatchingCutscene78]
             || Svc.Condition[ConditionFlag.Casting]
             || Svc.Condition[ConditionFlag.Casting87])
            {
                lastPos = player.Position;
                lastMoveTickMs = now;
                return false;
            }

            var pos = player.Position;
            if (lastPos is null)
            {
                lastPos = pos;
                lastMoveTickMs = now;
                return false;
            }

            if (Vector3.Distance(lastPos.Value, pos) > StuckMoveThresholdMeters)
                lastMoveTickMs = now;

            lastPos = pos;

            if (now - lastMoveTickMs > StuckMoveTimeoutMs)
            {
                Tripped = true;
                return true;
            }

            return false;
        }
    }

    private async Task EngageFate(PublicEvent fate)
    {
        var preset = Plugin.Cfg.CombatPresetName;
        Status = $"Engaging {fate.Name}";
        BossModIPC.Instance.SetActive(preset);
        BossModIPC.Instance.AddTransientStrategy(preset, "BossMod.Autorotation.MiscAI.AutoTarget", "MaxTargets", PullSize().ToString());

        try
        {
            // Break out on KO too — HandleKoIfNeeded in the outer loop will release/raise.
            await WaitUntil(
                condition: () => fate.State != FateState.Running || IsPlayerKO(),
                scopeName: $"engage:{fate.Id}",
                checkFrequency: 30,
                logContinuously: false);
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
        }
    }

    private static bool NearFate(PublicEvent fate)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return false;
        return Vector3.Distance(player.Position, fate.Position) <= fate.Radius;
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
        GrindMode.MaxFates     => zone.AchievementDone,
        GrindMode.MaxGemstones => GemstoneCount() >= Plugin.Cfg.TradeThreshold,
        GrindMode.RunCount     => session.CompletedCount >= Plugin.Cfg.TargetFateCount,
        _ => false,
    };

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

    // Set by AutoFate when it bails early on a cap-hit so the controller can route to AutoTrade.
    public ZoneInfo? PendingTradeFromZone;

    public TimeSpan Elapsed => DateTime.UtcNow - StartedAt;
    public int GemstonesEarned => Math.Max(0, GemstoneCurrent - GemstoneStart);
    public double FatesPerHour => Elapsed.TotalHours > 0 ? CompletedCount / Elapsed.TotalHours : 0;
}
