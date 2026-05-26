using clib.Utils;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;

namespace AutoFateGrind.Core.Game;

internal static class FateScanner
{
    // Below this, TimeRemainingUrgent kicks in to prioritize about-to-expire FATEs.
    private const int UrgentTimeThresholdSec = 240;
    // Twist of Fate status from a previous bonus FATE — we avoid burning a second bonus while it's up.
    private const uint TwistOfFateStatusId = 1288;

    // forcedReturnId: if set and the FATE still exists with Progress < 100, return it unconditionally.
    // Used after a KO so we re-engage the FATE we died in even if it now fails normal eligibility
    // (e.g. low TimeRemaining). Honors player blacklist/session-stuck so genuinely-broken FATEs still skip.
    public static PublicEvent? PickNext(
        Configuration cfg,
        Vector3 playerPos,
        IReadOnlySet<uint>? sessionBlacklist = null,
        uint? forcedReturnId = null)
    {
        var fates = PublicEvent.Fates;
        if (fates is null) return null;

        if (forcedReturnId is { } returnId
            && PublicEvent.GetFateById(returnId) is { Progress: < 100 } ret
            && !FateBlacklist.Contains(cfg, ret)
            && (sessionBlacklist is null || !sessionBlacklist.Contains(ret.Id)))
        {
            return ret;
        }

        var eligible = fates.Where(f => IsEligible(f, cfg, sessionBlacklist));
        return ApplySort(eligible, cfg.FateSortOrder, playerPos).FirstOrDefault();
    }

    public static bool IsEligible(PublicEvent f, Configuration cfg, IReadOnlySet<uint>? sessionBlacklist)
    {
        if (f.State != FateState.Running) return false;
        if (FateBlacklist.Contains(cfg, f)) return false;
        if (sessionBlacklist is not null && sessionBlacklist.Contains(f.Id)) return false;
        if (cfg.SkippedFateRules.Contains((int)f.Rule)) return false;
        if (f.TimeRemaining < cfg.MinTimeRemainingSec) return false;
        if (f.Progress > cfg.MaxProgressPct) return false;
        if (!f.IsOnMap) return false;
        return true;
    }

    public static IOrderedEnumerable<PublicEvent> ApplySort(
        IEnumerable<PublicEvent> source,
        IReadOnlyList<FateSortEntry> sortOrder,
        Vector3 playerPos)
    {
        var order = sortOrder is { Count: > 0 } ? sortOrder : DefaultSortOrder;
        IOrderedEnumerable<PublicEvent>? ordered = null;
        foreach (var entry in order)
        {
            var key = KeyFor(entry.Criterion, playerPos);
            ordered = ordered is null
                ? (entry.Descending ? source.OrderByDescending(key) : source.OrderBy(key))
                : (entry.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        }
        return ordered ?? source.OrderBy(_ => 0);
    }

    public static readonly IReadOnlyList<FateSortEntry> DefaultSortOrder =
    [
        new() { Criterion = FateSortCriterion.HasBonusWithTwist,   Descending = true  },
        new() { Criterion = FateSortCriterion.Progress,            Descending = true  },
        new() { Criterion = FateSortCriterion.HasBonus,            Descending = true  },
        new() { Criterion = FateSortCriterion.TimeRemainingUrgent, Descending = true  },
        new() { Criterion = FateSortCriterion.Distance,            Descending = false },
        new() { Criterion = FateSortCriterion.TimeRemaining,       Descending = false },
    ];

    private static Func<PublicEvent, IComparable> KeyFor(FateSortCriterion c, Vector3 playerPos) => c switch
    {
        FateSortCriterion.HasBonusWithTwist => f => f.HasBonus && !PlayerHasTwistOfFate(),
        FateSortCriterion.Progress          => f => f.Progress,
        FateSortCriterion.HasBonus          => f => f.HasBonus,
        // Negative TimeRemaining = unactivated; treat as non-urgent.
        FateSortCriterion.TimeRemainingUrgent => f => f.TimeRemaining is >= 0 and < UrgentTimeThresholdSec,
        FateSortCriterion.Distance          => f => Vector3.DistanceSquared(f.Position, playerPos),
        // Bucket: urgent FATEs sort by their actual remaining time; non-urgent tie at the threshold so
        // later criteria (distance, bonus) can break ties.
        FateSortCriterion.TimeRemaining     => f => f.TimeRemaining is >= 0 and < UrgentTimeThresholdSec
            ? f.TimeRemaining
            : UrgentTimeThresholdSec,
        FateSortCriterion.Level             => f => f.Level,
        FateSortCriterion.Name              => f => f.Name ?? string.Empty,
        _                                   => _ => 0,
    };

    public static bool PlayerHasTwistOfFate()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return false;
        foreach (var s in player.StatusList)
            if (s.StatusId == TwistOfFateStatusId) return true;
        return false;
    }
}
