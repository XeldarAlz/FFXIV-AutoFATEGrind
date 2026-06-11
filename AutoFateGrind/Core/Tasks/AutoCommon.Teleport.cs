using AutoFateGrind.Core.Ipc;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public abstract partial class AutoCommon
{
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
        await RunCancellable(move, UnstickMoveMs, $"{scope}-ground", StuckDetector.IdleStallAbort(StuckDetector.IdleStallTimeoutMs));
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
            await RunCancellable(op, perAttemptTimeoutMs, $"{label}#{i}", StuckDetector.IdleStallAbort(StuckDetector.IdleStallTimeoutMs));
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
}
