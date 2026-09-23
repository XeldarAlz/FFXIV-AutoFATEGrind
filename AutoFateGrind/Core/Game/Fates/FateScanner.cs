using AutoFateGrind.Core.Zones;
using clib.Utils;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;

namespace AutoFateGrind.Core.Game.Fates;

internal static class FateScanner
{
    private const int UrgentTimeThresholdSec = 240;
    private const uint TwistOfFateStatusId = 1288;
    private const int   MaxEquivalentPicks = 3;
    private const float EquivalentDistanceRatio = 1.2f;
    private const float EquivalentDistanceSlackMeters = 30f;
    private const int   EquivalentProgressPoints = 20;

    public const uint NoMotivationNpcId = 0xE0000000;

    public static bool AwaitsNpcStart(PublicEvent f)
        => f.State == FateState.Preparing && f.MotivationNpcId != NoMotivationNpcId;

    // forcedReturnId (set after a KO) returns the FATE we died in unconditionally, bypassing normal
    // eligibility like low TimeRemaining — but still respects the blacklists so broken FATEs skip.
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

    // The top-ranked FATE plus the runners-up a player would call just as good: same leading priorities,
    // progress within a few points, and not much further away. Stays top-ranked-only without a Distance key.
    public static void CollectEquivalentPicks(
        Configuration cfg,
        Vector3 playerPos,
        IReadOnlySet<uint>? sessionBlacklist,
        List<PublicEvent> picks)
    {
        picks.Clear();
        var fates = PublicEvent.Fates;
        if (fates is null) return;

        var order = EffectiveOrder(cfg.FateSortOrder);
        var ranked = ApplySort(fates.Where(f => IsEligible(f, cfg, sessionBlacklist)), order, playerPos).ToList();
        if (ranked.Count == 0) return;

        var best = ranked[0];
        picks.Add(best);

        var distanceIndex = IndexOfCriterion(order, FateSortCriterion.Distance);
        if (distanceIndex < 0) return;

        var maxDistance = Vector3.Distance(best.Position, playerPos) * EquivalentDistanceRatio + EquivalentDistanceSlackMeters;
        for (var rankIndex = 1; rankIndex < ranked.Count && picks.Count < MaxEquivalentPicks; rankIndex++)
        {
            var candidate = ranked[rankIndex];
            if (Vector3.Distance(candidate.Position, playerPos) > maxDistance) continue;
            if (!RanksAlike(best, candidate, order, distanceIndex, playerPos)) continue;
            picks.Add(candidate);
        }
    }

    private static bool RanksAlike(PublicEvent best, PublicEvent candidate, IReadOnlyList<FateSortEntry> order, int leadingCount, Vector3 playerPos)
    {
        for (var orderIndex = 0; orderIndex < leadingCount; orderIndex++)
        {
            var criterion = order[orderIndex].Criterion;
            if (criterion == FateSortCriterion.Progress)
            {
                if (Math.Abs(best.Progress - candidate.Progress) > EquivalentProgressPoints) return false;
                continue;
            }

            var key = KeyFor(criterion, playerPos);
            if (key(best).CompareTo(key(candidate)) != 0) return false;
        }
        return true;
    }

    private static int IndexOfCriterion(IReadOnlyList<FateSortEntry> order, FateSortCriterion criterion)
    {
        for (var orderIndex = 0; orderIndex < order.Count; orderIndex++)
        {
            if (order[orderIndex].Criterion == criterion) return orderIndex;
        }
        return -1;
    }

    private static IReadOnlyList<FateSortEntry> EffectiveOrder(IReadOnlyList<FateSortEntry> sortOrder)
        => sortOrder is { Count: > 0 } ? sortOrder : DefaultSortOrder;

    public static bool IsEligible(PublicEvent f, Configuration cfg, IReadOnlySet<uint>? sessionBlacklist)
    {
        var awaitsNpcStart = AwaitsNpcStart(f);
        if (f.State != FateState.Running && !awaitsNpcStart) return false;
        if (FateBlacklist.Contains(cfg, f)) return false;
        if (sessionBlacklist is not null && sessionBlacklist.Contains(f.Id)) return false;
        if (cfg.SkippedFateRules.Contains((int)f.Rule)) return false;
        if (!awaitsNpcStart && FateClock.Remaining(f) < cfg.MinTimeRemainingSec) return false;
        if (f.Progress > cfg.MaxProgressPct) return false;
        // A Collect FATE stays Running at 100% as its hand-in window; nothing can be contributed to it anymore.
        if (f.Progress >= 100) return false;
        if (!FateClock.IsOnMap(f)) return false;
        // A goal that picks its own zones sends a capped character into low-level ones, where the band would reject everything.
        if (cfg.LevelRangeFilterEnabled && !ZoneSelection.GoalPlansZones(cfg) && !IsWithinLevelRange(f, cfg))
        {
            return false;
        }
        return true;
    }

    // Player level comes from PlayerState rather than the (possibly synced) FATE level so the range is
    // always measured against the character's real level, matching what will actually take damage.
    private static bool IsWithinLevelRange(PublicEvent f, Configuration cfg)
    {
        var playerLevel = Svc.PlayerState.Level;
        if (playerLevel <= 0 || f.Level <= 0)
        {
            return true;
        }

        return f.Level >= playerLevel - cfg.MaxLevelBelow && f.Level <= playerLevel + cfg.MaxLevelAbove;
    }

    public static IOrderedEnumerable<PublicEvent> ApplySort(
        IEnumerable<PublicEvent> source,
        IReadOnlyList<FateSortEntry> sortOrder,
        Vector3 playerPos)
    {
        var order = EffectiveOrder(sortOrder);
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
        FateSortCriterion.TimeRemainingUrgent => f => IsUrgent(f),
        FateSortCriterion.Distance          => f => Vector3.DistanceSquared(f.Position, playerPos),
        // Urgent FATEs sort by actual remaining time; non-urgent ones tie at the threshold so later
        // criteria break the tie.
        FateSortCriterion.TimeRemaining     => f => IsUrgent(f) ? FateClock.Remaining(f) : UrgentTimeThresholdSec,
        FateSortCriterion.Level             => f => f.Level,
        FateSortCriterion.Name              => f => f.Name ?? string.Empty,
        _                                   => _ => 0,
    };

    private static bool IsUrgent(PublicEvent f)
        => !AwaitsNpcStart(f) && FateClock.Remaining(f) is >= 0 and < UrgentTimeThresholdSec;

    public static bool PlayerHasTwistOfFate()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return false;
        foreach (var s in player.StatusList)
            if (s.StatusId == TwistOfFateStatusId) return true;
        return false;
    }
}
