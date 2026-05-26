using clib.Utils;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;

namespace AutoFateGrind.Core.Game;

internal static class FateScanner
{
    public static PublicEvent? PickNext(Configuration cfg, Vector3 playerPos, IReadOnlySet<uint>? sessionBlacklist = null)
    {
        var fates = PublicEvent.Fates;
        if (fates is null) return null;

        return fates
            .Where(f => IsEligible(f, cfg, sessionBlacklist))
            .OrderByDescending(f => f.HasBonus)
            .ThenBy(f => f.TimeRemaining)
            .ThenBy(f => Vector3.DistanceSquared(f.Position, playerPos))
            .FirstOrDefault();
    }

    private static bool IsEligible(PublicEvent f, Configuration cfg, IReadOnlySet<uint>? sessionBlacklist)
    {
        if (f.State != FateState.Running) return false;
        if (cfg.BlacklistedFateIds.Contains(f.Id)) return false;
        if (sessionBlacklist is not null && sessionBlacklist.Contains(f.Id)) return false;
        if (cfg.SkippedFateRules.Contains((int)f.Rule)) return false;
        if (f.TimeRemaining < cfg.MinTimeRemainingSec) return false;
        if (f.Progress > cfg.MaxProgressPct) return false;
        if (!f.IsOnMap) return false;
        return true;
    }
}
