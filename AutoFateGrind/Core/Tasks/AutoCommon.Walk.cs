using ECommons.DalamudServices;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int WalkAttempts = 3;
    // clib parks a mount a few metres off the point and lands before the arrival check runs.
    private const float WalkArrivalSlackMeters = 2f;

    internal static bool WithinReach(Vector3 destination, float tolerance)
        => Svc.Objects.LocalPlayer is { } player
        && Vector3.Distance(player.Position, destination) <= tolerance + WalkArrivalSlackMeters;

    // A vnav walk that re-paths from wherever the character stopped when it wedges. vnavmesh's own retry
    // re-issues the same route into the same wall, and a stalled walk that simply fell through to the
    // interaction used to fail on range.
    internal async Task<bool> WalkWithRetries(Func<MoveOp> makeOp, int watchdogMs, string label, Func<bool> arrived)
    {
        for (var attempt = 1; attempt <= WalkAttempts; attempt++)
        {
            if (CancelToken.IsCancellationRequested) return false;
            var scope = $"{label}#{attempt}";
            var op = makeOp();
            var completed = await RunCancellable(op, watchdogMs, scope, StuckDetector.MoveStallAbort(scope));
            if (arrived()) return true;
            if (CancelToken.IsCancellationRequested) return false;
            if (op.Fault is { } fault) Diag($"{scope} faulted: {fault.Message}; re-pathing from here");
            else if (completed) Diag($"{scope} ended short of the destination; re-pathing from here");
            else Diag($"{scope} stalled; re-pathing from here");
        }
        return arrived();
    }
}
