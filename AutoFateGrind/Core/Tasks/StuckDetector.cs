using AutoFateGrind.Core.Ipc;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System;
using System.Numerics;

namespace AutoFateGrind.Core.Tasks;

internal static class StuckDetector
{
    internal const float StuckMoveThresholdMeters = 1.5f;
    // Idle = no movement while NOTHING legitimate is happening (no vnav, no pathfind, no cast/mount/zone
    // transition). That is a wedged op — typically a clib teleport that was issued but never started
    // casting. Long enough that the ~1-2s gap before a real teleport's cast can't trip it.
    internal const int IdleStallTimeoutMs = 8_000;
    // A vnav-driven move that closes on its current waypoint by less than the epsilon for this long is wedged.
    internal const int   NavWedgeTimeoutMs = 3_000;
    internal const float ProgressEpsilonMeters = 1.0f;
    private const int AirborneFreezeMs = 2_000;
    private const float AirborneFreezeMeters = 0.25f;

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

    // Abort predicate for a vnav move: trips on a terrain wedge or an idle wedge (see MoveStallTracker).
    internal static Func<bool> MoveStallAbort(string label)
    {
        var tracker = new MoveStallTracker();
        return () =>
        {
            var kind = tracker.Check();
            if (kind == StallKind.None) return false;
            Svc.Log.Info($"{AfgConstants.LogPrefix} {label} stalled ({kind}); aborting the move");
            return true;
        };
    }

    // A reusable abort predicate: trips when the player makes no physical progress while nothing
    // legitimate is in progress — no vnav follow/pathfind, no cast/mount/zone-transition. That is a clib
    // op (usually a teleport) that accepted its command but never started; a real teleport's cast and
    // zone load set frozen-legit flags, so its idle time never accrues. Returns a fresh stateful closure.
    internal static Func<bool> IdleStallAbort(int timeoutMs)
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

    // The game's own descent after an air dismount is not vnav's, so the stall tracker never sees it; a position that
    // stops changing while still airborne is the mount pressed against something it cannot land on.
    internal static Func<bool> AirborneFreezeAbort(string label)
    {
        Vector3? anchor = null;
        var frozenSinceMs = Environment.TickCount64;
        return () =>
        {
            var now = Environment.TickCount64;
            if (Svc.Objects.LocalPlayer is not { } player || !Svc.Condition[ConditionFlag.InFlight])
            {
                anchor = null;
                frozenSinceMs = now;
                return false;
            }

            var position = player.Position;
            if (anchor is null || Vector3.Distance(anchor.Value, position) > AirborneFreezeMeters)
            {
                anchor = position;
                frozenSinceMs = now;
                return false;
            }

            if (now - frozenSinceMs < AirborneFreezeMs)
            {
                return false;
            }

            Svc.Log.Info($"{AfgConstants.LogPrefix} {label} froze in the air for {AirborneFreezeMs}ms at {position}; aborting the descent");
            return true;
        };
    }
}
