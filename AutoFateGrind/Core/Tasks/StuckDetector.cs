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
}
