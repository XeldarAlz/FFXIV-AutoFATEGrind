using AutoFateGrind.Core.Ipc;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public abstract class AutoCommon : TaskBase
{
    protected const int SubTaskUnwindGraceMs = 5_000;
    internal const float StuckMoveThresholdMeters = 1.5f;
    // Idle = no movement while NOTHING legitimate is happening (no vnav, no pathfind, no cast/mount/zone
    // transition). That is a wedged op — typically a clib teleport that was issued but never started
    // casting. Long enough that the ~1-2s gap before a real teleport's cast can't trip it.
    internal const int IdleStallTimeoutMs = 8_000;

    // Stationary-but-legitimate states. Excludes Mounted (a mount snagged on terrain is a real freeze)
    // but includes Mounting (the summon holds the character still for ~1-2s).
    internal static bool IsPositionFrozenLegit()
        => Svc.Condition[ConditionFlag.Casting]
        || Svc.Condition[ConditionFlag.Casting87]
        || Svc.Condition[ConditionFlag.Mounting]
        || Svc.Condition[ConditionFlag.Mounting71]
        || Svc.Condition[ConditionFlag.BetweenAreas]
        || Svc.Condition[ConditionFlag.BetweenAreas51]
        || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]
        || Svc.Condition[ConditionFlag.WatchingCutscene]
        || Svc.Condition[ConditionFlag.WatchingCutscene78];

    // A reusable abort predicate: trips when the player makes no physical progress while nothing
    // legitimate is in progress — no vnav follow/pathfind, no cast/mount/zone-transition. That is a clib
    // op (usually a teleport) that accepted its command but never started; a real teleport's cast and
    // zone load set frozen-legit flags, so its idle time never accrues. Returns a fresh stateful closure.
    internal Func<bool> IdleStallAbort(int timeoutMs)
    {
        Vector3? anchor = null;
        var idleSinceMs = Environment.TickCount64;
        return () =>
        {
            var player = Svc.Objects.LocalPlayer;
            if (player is null) return false;
            var now = Environment.TickCount64;
            var pos = player.Position;
            if (anchor is null
             || Vector3.Distance(anchor.Value, pos) > StuckMoveThresholdMeters
             || NavmeshIPC.Instance.IsBusy()
             || IsPositionFrozenLegit())
            {
                anchor = pos;
                idleSinceMs = now;
                return false;
            }
            return now - idleSinceMs >= timeoutMs;
        };
    }

    private const int TeleportCombatClearMs = 30_000;
    private const int DismountForTeleportMs = 30_000;
    private const int UnstickMoveMs = 20_000;

    // Compact condition snapshot for diagnostics — surfaces exactly which state blocks a teleport cast.
    internal static string ConditionTag()
    {
        var c = Svc.Condition;
        var tags = new List<string>(6);
        if (c[ConditionFlag.InCombat]) tags.Add("combat");
        if (c[ConditionFlag.Casting] || c[ConditionFlag.Casting87]) tags.Add("cast");
        if (c[ConditionFlag.Mounted] || c[ConditionFlag.RidingPillion]) tags.Add("mount");
        if (c[ConditionFlag.InFlight]) tags.Add("flight");
        if (c[ConditionFlag.Diving]) tags.Add("dive");
        if (c[ConditionFlag.Swimming]) tags.Add("swim");
        if (c[ConditionFlag.Jumping] || c[ConditionFlag.Jumping61]) tags.Add("jump");
        if (c[ConditionFlag.BeingMoved]) tags.Add("moved");
        if (c[ConditionFlag.BetweenAreas] || c[ConditionFlag.BetweenAreas51]) tags.Add("zoning");
        if (c[ConditionFlag.Occupied33] || c[ConditionFlag.Occupied38] || c[ConditionFlag.Occupied39]) tags.Add("occupied");
        return tags.Count == 0 ? "grounded" : string.Join(",", tags);
    }

    // Teleport can only cast from solid ground: flying, diving, or swimming all leave clib spinning on a
    // cast that never starts (the 2026-05-30 wedges). Off the ground in any of those states => the char
    // is somewhere clib's own dismount can't recover from.
    private static bool NotOnSolidGround()
        => Svc.Condition[ConditionFlag.InFlight]
        || Svc.Condition[ConditionFlag.Diving]
        || Svc.Condition[ConditionFlag.Swimming];

    // Pre-teleport gate for every teleport entry path. (1) stop any lingering vnav movement (BeingMoved
    // blocks the cast); (2) wait for combat/cast to clear; (3) if off the ground (air/water), walk to the
    // nearest reachable mesh point to land/surface — clib's Teleport never casts otherwise and just spins;
    // (4) dismount. The 2026-05-30 wedges (a flying mount over no landing; a water FATE leaving the char in
    // the creek) both come down to "can't cast Teleport from here" with no recovery.
    internal async Task PrepareForTeleport(string scope)
    {
        NavmeshIPC.Instance.Stop();

        if (Svc.Condition[ConditionFlag.InCombat] || Svc.Condition[ConditionFlag.Casting])
        {
            Status = "Waiting for combat to clear before teleport";
            Diag($"{scope}: combat/casting ({ConditionTag()}), waiting up to {TeleportCombatClearMs / 1000}s for a castable window");
            await WaitUntilTimed(
                () => !Svc.Condition[ConditionFlag.InCombat] && !Svc.Condition[ConditionFlag.Casting],
                TeleportCombatClearMs, $"{scope}-wait-teleportable");
        }
        if (CancelToken.IsCancellationRequested) return;

        if (NotOnSolidGround()) await GroundForTeleport(scope);
        if (CancelToken.IsCancellationRequested) return;

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            Diag($"{scope}: dismounting before teleport ({ConditionTag()})");
            await RunCancellable(new MoveOp(o => o.DismountNow()), DismountForTeleportMs, $"{scope}-dismount");
        }
    }

    // Walk (clib will fly/swim) to the nearest standable mesh point so a teleport can cast. Surfaces from
    // water and lands from flight; allowTeleportIfFaster:false keeps it from re-entering the broken teleport.
    private async Task GroundForTeleport(string scope)
    {
        var here = Svc.Objects.LocalPlayer?.Position;
        if (here is not { } pos) return;

        var safe = NavmeshIPC.Instance.NearestPointReachable(pos, 30f, 30f);
        if (safe is not { } dest)
        {
            Warn($"{scope}: off solid ground ({ConditionTag()}) with no reachable mesh point to relocate to; teleport may fail");
            return;
        }
        if (Vector3.Distance(pos, dest) < 2f) return;

        Status = "Returning to solid ground before teleport";
        Diag($"{scope}: off solid ground ({ConditionTag()}); relocating ~{Vector3.Distance(pos, dest):F0}m to a reachable point before teleport");
        var territory = Svc.ClientState.TerritoryType;
        var move = new MoveOp(o => o.Move(territory, dest, MovementConfig.Everything.WithTolerance(3f),
            allowTeleportIfFaster: false, stopCondition: null, allowAethernetWithinTerritory: false));
        await RunCancellable(move, UnstickMoveMs, $"{scope}-ground", IdleStallAbort(IdleStallTimeoutMs));
    }

    private const int  ReturnHomeWaitMs    = 30_000;
    private const int  ReturnHomeReissueMs = 3_000;
    private const uint ReturnGeneralActionId = 8; // "Return" — home-point teleport, a separate path from aetheryte Teleport.

    // Clears a stuck "another teleport is already underway" state, which silently blocks every new aetheryte
    // teleport (clib then spins on a cast that never starts). Return uses a different path that still fires,
    // and lands us in a city we can teleport out of. Returns true once we leave the starting territory.
    internal async Task<bool> TryReturnHome(string scope)
    {
        var startTerr = Svc.ClientState.TerritoryType;
        Diag($"{scope}: teleport not starting ({ConditionTag()}); casting Return to home aetheryte to clear a stuck teleport");
        Status = "Returning home to clear a stuck teleport";

        var deadline = Environment.TickCount64 + ReturnHomeWaitMs;
        var nextCastAt = 0L;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (Svc.ClientState.TerritoryType != startTerr)
            {
                await NextFrame(300); // settle after the zone load
                return true;
            }
            if (Environment.TickCount64 >= nextCastAt
                && !Svc.Condition[ConditionFlag.Casting]
                && !Svc.Condition[ConditionFlag.BetweenAreas]
                && !Svc.Condition[ConditionFlag.BetweenAreas51])
            {
                CastReturn();
                nextCastAt = Environment.TickCount64 + ReturnHomeReissueMs;
            }
            await NextFrame(100);
        }
        if (Svc.ClientState.TerritoryType != startTerr) return true;
        Diag($"{scope}: Return home did not complete within {ReturnHomeWaitMs / 1000}s");
        return false;
    }

    private static unsafe void CastReturn()
    {
        var am = ActionManager.Instance();
        if (am is null) return;
        am->UseAction(ActionType.GeneralAction, ReturnGeneralActionId);
    }

    // Resilient cross-zone teleport. clib's TeleportTo can accept the teleport yet never start casting
    // (a brief post-FATE/combat teleport lock), then spin until a watchdog fires. IdleStallAbort catches
    // that in ~8s; we retry with a short backoff so the lock can clear, instead of failing the whole
    // operation on one slow timeout. Returns true once we are in the target territory.
    internal async Task<bool> TeleportToTerritory(uint territoryId, Vector3 dest, string label, int perAttemptTimeoutMs, int attempts = 4)
    {
        var returnedHome = false;
        for (var i = 1; i <= attempts && !CancelToken.IsCancellationRequested; i++)
        {
            if (Svc.ClientState.TerritoryType == territoryId) return true;
            await PrepareForTeleport($"{label}#{i}");
            if (CancelToken.IsCancellationRequested) break;
            var op = new MoveOp(o => o.Teleport(territoryId, dest, allowSameZoneTeleport: false));
            await RunCancellable(op, perAttemptTimeoutMs, $"{label}#{i}", IdleStallAbort(IdleStallTimeoutMs));
            if (Svc.ClientState.TerritoryType == territoryId) return true;
            if (op.Fault is not null) Diag($"{label}#{i} teleport faulted: {op.Fault.Message}");

            // After two stalled attempts the teleport is almost certainly blocked by one "already underway";
            // Return home (a separate path) clears it, and the next attempt teleports from the home city.
            if (!returnedHome && i >= 2)
            {
                returnedHome = true;
                await TryReturnHome(label);
            }
            else
            {
                await NextFrame(120); // brief backoff so a transient teleport lock can clear before the retry
            }
        }
        return Svc.ClientState.TerritoryType == territoryId;
    }

    // Run a single clib operation as its own cancellable AutoTask and bound it with a wall-clock cap.
    // Unlike AwaitWatchdog (which could only NavmeshIPC.Stop and then abandon a still-running clib
    // task), this owns the operation's CancellationTokenSource: on timeout — or when abortIf trips —
    // it calls op.Cancel(), which cancels every await inside the operation and fires its registered
    // cleanups (movement hook off, the MoveTo OnDispose stops vnav). The operation genuinely unwinds,
    // so it can never linger and fight the next move/teleport. Returns true if the op finished on its
    // own, false if it had to be cancelled (timeout, abort, or run cancellation).
    internal async Task<bool> RunCancellable(MoveOp op, int timeoutMs, string label, Func<bool>? abortIf = null)
    {
        var tcs = new TaskCompletionSource();
        op.Run(() => tcs.TrySetResult());
        var work = tcs.Task;

        // The op runs on its own CancellationTokenSource, so cancelling the whole run (Stop) would not
        // reach it. Bind them: if our token fires, cancel the op too, so a Stop tears everything down.
        using var reg = CancelToken.Register(() => TryCancel(op, label));

        var deadline = Environment.TickCount64 + timeoutMs;
        var aborted = false;
        var abortThrew = false;
        while (!work.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) break;
            if (abortIf is not null)
            {
                bool trip;
                try { trip = abortIf(); }
                catch (Exception ex)
                {
                    if (!abortThrew) { Warn($"RunCancellable '{label}' abortIf threw (abort disabled, watchdog still active): {ex.Message}"); abortThrew = true; }
                    trip = false;
                }
                if (trip) { aborted = true; break; }
            }
            await NextFrame(4);
        }

        if (work.IsCompleted) return true;

        if (CancelToken.IsCancellationRequested)
        {
            TryCancel(op, label);
            return false;
        }

        Diag(aborted
            ? $"RunCancellable '{label}' aborting sub-task (abort condition met)"
            : $"WATCHDOG: '{label}' exceeded {timeoutMs / 1000}s; cancelling sub-task");
        TryCancel(op, label);

        var grace = Environment.TickCount64 + SubTaskUnwindGraceMs;
        while (!work.IsCompleted && Environment.TickCount64 < grace)
            await NextFrame(4);

        if (!work.IsCompleted)
            Diag($"WATCHDOG: '{label}' did not unwind {SubTaskUnwindGraceMs / 1000}s after Cancel (unexpected)");

        return false;
    }

    private void TryCancel(MoveOp op, string label)
    {
        // The op can complete (and dispose its CTS) between our IsCompleted check and here; Cancel on a
        // disposed CTS throws. Swallow it — a completed op needs no cancelling.
        try { op.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Warn($"RunCancellable '{label}' Cancel threw: {ex.Message}"); }
    }

    protected void Diag(string message)
    {
        Svc.Log.Info($"[AFG] {message}");
    }

    protected void Warn(string message)
    {
        Svc.Log.Warning($"[AFG] {message}");
    }

    protected void Trace(string message)
    {
        Svc.Log.Debug($"[AFG] {message}");
    }

    // Pins Status every frame to override clib's internal coordinate strings during teleport/aethernet.
    protected async Task RunWithStatusPinned(string label, Func<Task> work)
    {
        Status = label;
        void Pin(object _) => Status = label;
        Svc.Framework.Update += Pin;
        try { await work(); }
        finally { Svc.Framework.Update -= Pin; }
    }

    protected async Task<bool> WaitUntilTimed(Func<bool> condition, int timeoutMs, string scope, int checkMs = 30)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var threw = false;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return false;
            bool ok;
            try { ok = condition(); }
            catch (Exception ex)
            {
                if (!threw) { Warn($"WaitUntilTimed '{scope}' condition threw (treating as unsatisfied; will retry until timeout): {ex.Message}"); threw = true; }
                ok = false;
            }
            if (ok) return true;
            await NextFrame(checkMs);
        }
        Diag($"WAIT TIMEOUT: '{scope}' not satisfied within {timeoutMs / 1000}s");
        return false;
    }
}
