using clib.Utils;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Collections.Generic;
using System.Numerics;

namespace AutoFateGrind.Core.Game.Fates;

internal static class FateScanner
{
    private const int UrgentTimeThresholdSec = 240;
    private const uint TwistOfFateStatusId = 1288;

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

        var comparer = ComparerFor(cfg.FateSortOrder, playerPos);
        PublicEvent? best = null;
        foreach (var fate in fates)
        {
            if (!IsEligible(fate, cfg, sessionBlacklist)) continue;
            if (best is null || comparer.Compare(fate, best) < 0) best = fate;
        }
        return best;
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

    // Fills `into` with every eligible FATE (optionally excluding one id), sorted by the active order.
    // Reuses the caller's buffer so the per-frame UI path allocates nothing.
    public static void CollectEligible(
        Configuration cfg, Vector3 playerPos, uint? excludeId, List<PublicEvent> into)
    {
        into.Clear();
        var fates = PublicEvent.Fates;
        if (fates is null) return;

        foreach (var fate in fates)
        {
            if (excludeId is { } id && fate.Id == id) continue;
            if (!IsEligible(fate, cfg, null)) continue;
            into.Add(fate);
        }
        into.Sort(ComparerFor(cfg.FateSortOrder, playerPos));
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

    private static FateComparer ComparerFor(IReadOnlyList<FateSortEntry> sortOrder, Vector3 playerPos)
    {
        var order = sortOrder is { Count: > 0 } ? sortOrder : DefaultSortOrder;
        return new FateComparer(order, playerPos, PlayerHasTwistOfFate());
    }

    public static bool PlayerHasTwistOfFate()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return false;
        foreach (var status in player.StatusList)
            if (status.StatusId == TwistOfFateStatusId) return true;
        return false;
    }

    private readonly struct FateComparer(
        IReadOnlyList<FateSortEntry> order, Vector3 playerPos, bool hasTwist) : IComparer<PublicEvent>
    {
        public int Compare(PublicEvent? x, PublicEvent? y)
        {
            if (x is null || y is null) return 0;
            for (var index = 0; index < order.Count; index++)
            {
                var entry = order[index];
                var comparison = CompareBy(entry.Criterion, x, y);
                if (comparison != 0) return entry.Descending ? -comparison : comparison;
            }
            return 0;
        }

        private int CompareBy(FateSortCriterion criterion, PublicEvent a, PublicEvent b) => criterion switch
        {
            FateSortCriterion.HasBonusWithTwist   => (a.HasBonus && !hasTwist).CompareTo(b.HasBonus && !hasTwist),
            FateSortCriterion.Progress            => a.Progress.CompareTo(b.Progress),
            FateSortCriterion.HasBonus            => a.HasBonus.CompareTo(b.HasBonus),
            FateSortCriterion.TimeRemainingUrgent => IsUrgent(a).CompareTo(IsUrgent(b)),
            FateSortCriterion.Distance            => DistanceSq(a).CompareTo(DistanceSq(b)),
            FateSortCriterion.TimeRemaining       => CompareTimeRemaining(a, b),
            FateSortCriterion.Level               => a.Level.CompareTo(b.Level),
            FateSortCriterion.Name                => string.CompareOrdinal(a.Name ?? string.Empty, b.Name ?? string.Empty),
            _                                     => 0,
        };

        private float DistanceSq(PublicEvent f) => Vector3.DistanceSquared(f.Position, playerPos);

        private static bool IsUrgent(PublicEvent f) => f.TimeRemaining is >= 0 and < UrgentTimeThresholdSec;

        // Urgent FATEs sort before non-urgent ones and by actual remaining time; non-urgent ones tie so
        // later criteria break it. Mirrors the old clamp-to-threshold key without boxing.
        private static int CompareTimeRemaining(PublicEvent a, PublicEvent b)
        {
            var urgentA = IsUrgent(a);
            var urgentB = IsUrgent(b);
            if (urgentA != urgentB) return urgentA ? -1 : 1;
            if (!urgentA) return 0;
            return a.TimeRemaining.CompareTo(b.TimeRemaining);
        }
    }
}
