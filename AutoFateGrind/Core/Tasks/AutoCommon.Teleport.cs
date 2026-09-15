using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Zones;
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
    private const int UnstickMoveMs = 20_000;
    private const int ZoneLoadSettleMs = 5_000;
    private const int ReturnHomePollMs = 250;
    private const int TeleportRetryBackoffMs = 2_000;

    internal readonly record struct TeleportOutcome(bool Completed, Exception? Fault)
    {
        public bool Succeeded => Completed && Fault is null;
    }

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

    // Swimming or diving may block the cast; the 2026-05-30 creek wedge left clib spinning on one that never started.
    private static bool InWater()
        => Svc.Condition[ConditionFlag.Diving]
        || Svc.Condition[ConditionFlag.Swimming];

    // Pre-teleport gate for every teleport entry path: stop vnav (BeingMoved blocks the cast), wait for combat/cast to
    // clear, and leave the water. A mount stays on, in the air too: landing first stranded characters on wall edges and
    // hilltops (issue #66), so RunTeleport lands only when a mounted cast does not go through.
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

        if (InWater()) await LeaveWaterForTeleport(scope);
    }

    // Walk (clib will swim/fly) to the nearest reachable mesh point so a teleport can cast;
    // allowTeleportIfFaster:false keeps it from re-entering the broken teleport.
    private async Task LeaveWaterForTeleport(string scope)
    {
        var here = Svc.Objects.LocalPlayer?.Position;
        if (here is not { } pos) return;

        var safe = NavmeshIPC.Instance.NearestPointReachable(pos, 30f, 30f);
        if (safe is not { } dest)
        {
            Warn($"{scope}: in the water ({ConditionTag()}) with no reachable mesh point to climb out at; teleport may fail");
            return;
        }
        if (Vector3.Distance(pos, dest) < 2f) return;

        Status = "Leaving the water before teleport";
        Diag($"{scope}: in the water ({ConditionTag()}); moving ~{Vector3.Distance(pos, dest):F0}m to a reachable point before teleport");
        var territory = Svc.ClientState.TerritoryType;
        var move = new MoveOp(o => o.Move(territory, dest, MovementConfig.Everything.WithTolerance(3f),
            allowTeleportIfFaster: false, stopCondition: null, allowAethernetWithinTerritory: false));
        await RunCancellable(move, UnstickMoveMs, $"{scope}-water", StuckDetector.MoveStallAbort($"{scope}-water"));
    }

    // Casts from wherever the character is, mounted or in the air. A mounted cast that is refused or never starts gets
    // one more try after a proper landing and dismount, so a mount can never be what blocks a teleport.
    internal async Task<TeleportOutcome> RunTeleport(uint territoryId, Vector3 destination, bool allowSameZoneTeleport, int timeoutMs, string scope)
    {
        var outcome = await RunTeleportOnce(territoryId, destination, allowSameZoneTeleport, timeoutMs, scope);
        if (outcome.Succeeded || CancelToken.IsCancellationRequested || !Svc.Condition[ConditionFlag.Mounted])
        {
            return outcome;
        }

        Diag($"{scope}: teleport did not go through while mounted ({ConditionTag()}); dismounting and casting once more");
        if (!await SafeDismount($"{scope}-dismount"))
        {
            return outcome;
        }
        return await RunTeleportOnce(territoryId, destination, allowSameZoneTeleport, timeoutMs, $"{scope}-dismounted");
    }

    // The idle-stall guard catches a teleport that was accepted but never started casting in ~8s, not the full watchdog.
    private async Task<TeleportOutcome> RunTeleportOnce(uint territoryId, Vector3 destination, bool allowSameZoneTeleport, int timeoutMs, string scope)
    {
        var operation = new MoveOp(move => move.Teleport(territoryId, destination, allowSameZoneTeleport));
        var completed = await RunCancellable(operation, timeoutMs, scope, StuckDetector.IdleStallAbort(StuckDetector.IdleStallTimeoutMs));
        return new TeleportOutcome(completed, operation.Fault);
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
                await DelayMs(ZoneLoadSettleMs);
                return true;
            }
            if (Environment.TickCount64 >= nextCastAt
                && !Svc.Condition[ConditionFlag.Casting]
                && !Svc.Condition[ConditionFlag.BetweenAreas]
                && !Svc.Condition[ConditionFlag.BetweenAreas51])
            {
                UseGeneralAction(ReturnGeneralActionId);
                nextCastAt = Environment.TickCount64 + ReturnHomeReissueMs;
            }
            await DelayMs(ReturnHomePollMs);
        }
        if (Svc.ClientState.TerritoryType != startTerr) return true;
        Diag($"{scope}: Return home did not complete within {ReturnHomeWaitMs / 1000}s");
        return false;
    }

    private static unsafe void UseGeneralAction(uint generalActionId)
    {
        var actionManager = ActionManager.Instance();
        if (actionManager is null) return;
        actionManager->UseAction(ActionType.GeneralAction, generalActionId);
    }

    private const int MaxTeleportFaults = 2;
    private const int StallsBeforeReturnHome = 2;

    // Resilient cross-zone teleport. clib's TeleportTo can accept the teleport yet never start casting
    // (a brief post-FATE/combat teleport lock), then spin until a watchdog fires. IdleStallAbort catches
    // that in ~8s; we retry with a short backoff so the lock can clear, instead of failing the whole
    // operation on one slow timeout. A fault is a different animal: the request was answered (no
    // aetheryte in that territory, not attuned) and the same request gets the same answer, so it earns
    // one retry and never the Return-home escalation. Returns true once we are in the target territory.
    internal async Task<bool> TeleportToTerritory(uint territoryId, Vector3 dest, string label, int perAttemptTimeoutMs, int attempts = 4)
    {
        if (Svc.ClientState.TerritoryType == territoryId) return true;

        // A zone with no aetheryte of its own is a two-leg trip: teleport to the hub the game routes it
        // through, then ride that hub's aethernet in. Every other zone keeps the single-leg path.
        var viaGateway = ZoneAetherytes.TryFindGateway(territoryId, out var gateway);
        var hopTerritoryId = viaGateway ? gateway.TerritoryId : territoryId;
        var hopDest = viaGateway ? gateway.Position : dest;
        // A gateway attempt costs more than twice a plain one, so it stops at the point the escalation
        // ladder is fully exercised: try, try and clear the lock by going home, try once from home.
        var maxAttempts = viaGateway ? Math.Min(attempts, GatewayAttempts) : attempts;

        if (ZoneAetherytes.AttunableIdsIn(hopTerritoryId).Length == 0)
        {
            Warn($"{label}: territory {hopTerritoryId} has no aetheryte to teleport to; giving up");
            return false;
        }

        var returnedHome = false;
        var stalls = 0;
        var faults = 0;
        for (var attempt = 1; attempt <= maxAttempts && !CancelToken.IsCancellationRequested; attempt++)
        {
            if (Svc.ClientState.TerritoryType == territoryId) return true;

            var scope = $"{label}#{attempt}";
            await PrepareForTeleport(scope);
            if (CancelToken.IsCancellationRequested) break;

            if (Svc.ClientState.TerritoryType != hopTerritoryId)
            {
                var outcome = await RunTeleport(hopTerritoryId, hopDest, allowSameZoneTeleport: false, perAttemptTimeoutMs, scope);
                if (outcome.Fault is { } fault)
                {
                    faults++;
                    Diag($"{scope} teleport faulted ({faults}/{MaxTeleportFaults}): {fault.Message}");
                }
                else if (!outcome.Completed)
                {
                    stalls++;
                }
            }

            if (viaGateway && Svc.ClientState.TerritoryType == hopTerritoryId)
                await RideAethernetInto(territoryId, dest, gateway, scope);

            if (Svc.ClientState.TerritoryType == territoryId) return true;

            if (faults >= MaxTeleportFaults)
            {
                Diag($"{label}: teleport rejected {faults} times, not a stuck cast; giving up without Return");
                break;
            }

            // After two stalled attempts the teleport is almost certainly blocked by one "already underway";
            // Return home (a separate path) clears it, and the next attempt teleports from the home city.
            if (!returnedHome && stalls >= StallsBeforeReturnHome)
            {
                returnedHome = true;
                await TryReturnHome(label);
            }
            else
            {
                await DelayMs(TeleportRetryBackoffMs);
            }
        }
        return Svc.ClientState.TerritoryType == territoryId;
    }

    private const int GatewayAttempts = 3;
    private const int AethernetLegMs = 90_000;
    private const int AethernetNavmeshWaitMs = 60_000;

    // Second leg of a gateway zone: walk to the hub's aetheryte and ride the aethernet in. Deliberately
    // runs without IdleStallAbort — the walk-up, the aetheryte menu and the shard hop all hold the
    // character still in states that guard reads as a teleport that never started.
    private async Task RideAethernetInto(uint territoryId, Vector3 dest, ZoneGateway gateway, string scope)
    {
        Status = $"Riding the aethernet from {gateway.Name}";
        Diag($"{scope}: reached {gateway.Name}; riding its aethernet into territory {territoryId}");

        await WaitForNavmeshReady(AethernetNavmeshWaitMs, 60);
        if (CancelToken.IsCancellationRequested) return;

        var op = new MoveOp(o => o.Aethernet(territoryId, dest));
        await RunCancellable(op, AethernetLegMs, $"{scope}-aethernet");
        if (op.Fault is { } fault) Diag($"{scope}: aethernet leg faulted: {fault.Message}");
    }
}
