using AutoFateGrind.Core.Ipc;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int MaxLandingSpots = 3;
    private const float LandingRingRadiusMeters = 6f;
    private const int LandingRingPoints = 6;
    private const int LandingCandidateCount = 1 + LandingRingPoints;
    private const float LandingFloorHalfExtentMeters = 3f;
    // Probed from well above, so the floor found is the top layer under the spot and not a cave or tunnel below it.
    private const float LandingProbeLiftMeters = 30f;
    private const float LandingArriveMeters = 1.5f;
    private const int LandingFlightWatchdogMs = 20_000;
    private const int LandingDismountWatchdogMs = 6_000;
    private const int GroundDismountWatchdogMs = 8_000;
    private const float LandingBackOffMeters = 8f;
    private const int LandingBackOffMs = 1_500;
    private const uint JumpGeneralActionId = 2;

    private readonly Vector3[] landingSpots = new Vector3[LandingCandidateCount];

    // Every dismount goes through here. In the air clib's own dismount flies to the nearest mesh point, which from
    // altitude is a wall edge or a hilltop (issue #66), so the landing is aimed at the floor under the character first.
    protected async Task<bool> SafeDismount(string scope)
    {
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (!Svc.Condition[ConditionFlag.InFlight])
        {
            return await GroundDismount(scope);
        }

        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        return await LandAndDismount(player.Position, scope);
    }

    private async Task<bool> LandAndDismount(Vector3 around, string scope)
    {
        Status = "Landing";
        var found = FindLandingSpots(around, landingSpots);
        if (found == 0)
        {
            Diag($"{scope}: no landable floor within {LandingRingRadiusMeters:F0}m of {FormatPosition(around)}; descending where the flight ended");
            return await DescendAndDismount(scope);
        }

        var attempts = Math.Min(found, MaxLandingSpots);
        for (var spotIndex = 0; spotIndex < attempts; spotIndex++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            var spot = landingSpots[spotIndex];
            var legScope = $"{scope}-land#{spotIndex + 1}";
            Diag($"{legScope}: flying {DistanceTo(spot):F0}m to a landable spot at {FormatPosition(spot)}");
            // No mount flags: clib keeps flying because the character is airborne, and skips its own landing.
            var flight = new MoveOp(move => move.MoveInZone(spot, MovementConfig.Default.WithTolerance(LandingArriveMeters), null));
            await RunCancellable(flight, LandingFlightWatchdogMs, legScope, StuckDetector.MoveStallAbort(legScope));
            if (flight.Fault is { } fault)
            {
                Diag($"{legScope}: the landing flight faulted: {fault.Message}");
            }

            if (await DescendAndDismount(legScope))
            {
                return true;
            }

            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            await BackOffInFlight(legScope);
        }

        Warn($"{scope}: could not land near {FormatPosition(around)} after {attempts} spot(s) ({ConditionTag()})");
        return false;
    }

    private async Task<bool> DescendAndDismount(string scope)
    {
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (!Svc.Condition[ConditionFlag.InFlight])
        {
            return await GroundDismount(scope);
        }

        await DismountViaOp(scope, LandingDismountWatchdogMs, StuckDetector.AirborneFreezeAbort(scope));
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        Diag($"{scope}: the descent did not land ({ConditionTag()})");
        CancelDescent();
        return false;
    }

    private async Task<bool> GroundDismount(string scope)
    {
        await DismountViaOp(scope, GroundDismountWatchdogMs);
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        Diag($"{scope}: still mounted after the dismount ({ConditionTag()})");
        return false;
    }

    private Task<bool> DismountViaOp(string label, int watchdogMs, Func<bool>? abortIf = null)
        => RunCancellable(new MoveOp(move => move.DismountNow()), watchdogMs, label, abortIf);

    // Jumping in the air ends the game's own descent, which otherwise carries on after the operation is cancelled and
    // reads as player input to vnavmesh, dropping the next path the moment it is queued.
    private static void CancelDescent()
    {
        if (Svc.Condition[ConditionFlag.InFlight])
        {
            UseGeneralAction(JumpGeneralActionId);
        }
    }

    // Straight up is the one direction known to be clear, since the flight came from there.
    private async Task BackOffInFlight(string scope)
    {
        if (Svc.Objects.LocalPlayer is not { } player || !Svc.Condition[ConditionFlag.InFlight])
        {
            return;
        }

        var above = player.Position with { Y = player.Position.Y + LandingBackOffMeters };
        Diag($"{scope}: backing off {LandingBackOffMeters:F0}m upward");
        NavmeshIPC.Instance.MoveAlong([above], fly: true);
        await DelayMs(LandingBackOffMs);
        NavmeshIPC.Instance.Stop();
    }

    // The spot itself first, then a ring around it, each snapped to the top layer of floor under it.
    private static int FindLandingSpots(Vector3 around, Vector3[] spots)
    {
        var navmesh = NavmeshIPC.Instance;
        var found = 0;
        if (LandableFloor(navmesh, around) is { } center)
        {
            spots[found++] = center;
        }

        for (var ringIndex = 0; ringIndex < LandingRingPoints; ringIndex++)
        {
            var angle = MathF.Tau * ringIndex / LandingRingPoints;
            var candidate = around + new Vector3(MathF.Cos(angle) * LandingRingRadiusMeters, 0f, MathF.Sin(angle) * LandingRingRadiusMeters);
            if (LandableFloor(navmesh, candidate) is { } floor)
            {
                spots[found++] = floor;
            }
        }

        return found;
    }

    private static Vector3? LandableFloor(NavmeshIPC navmesh, Vector3 point)
        => navmesh.PointOnFloor(point with { Y = point.Y + LandingProbeLiftMeters }, allowUnreachable: false, LandingFloorHalfExtentMeters);

    private static float DistanceTo(Vector3 target)
        => Svc.Objects.LocalPlayer is { } player ? Vector3.Distance(player.Position, target) : float.MaxValue;

    private static string FormatPosition(Vector3 position)
        => $"({position.X:F0},{position.Y:F0},{position.Z:F0})";
}
