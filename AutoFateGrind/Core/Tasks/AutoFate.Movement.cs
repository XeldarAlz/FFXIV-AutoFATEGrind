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

public sealed partial class AutoFate
{
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
        => Plugin.Cfg.ActiveMode.IsComplete(new ModeContext { CompletedCount = session.CompletedCount, Zones = zones, Elapsed = session.Elapsed });

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
