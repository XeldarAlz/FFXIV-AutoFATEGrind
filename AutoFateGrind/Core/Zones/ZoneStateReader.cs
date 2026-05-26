using AutoFateGrind.Core.Game;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Zones;

internal static class ZoneStateReader
{
    private static HashSet<uint>? unlockedTerritoryCache;
    private static long unlockedCacheTickMs;
    private const int UnlockedCacheLifetimeMs = 1000;

    public static void Refresh(ZoneInfo zone)
    {
        zone.Unlocked = IsTerritoryUnlocked(zone.TerritoryId);
        zone.ActiveFateCount = Svc.ClientState.TerritoryType == zone.TerritoryId
            ? CountActiveFatesInCurrentZone()
            : 0;
        RefreshAchievement(zone);
    }

    private static void RefreshAchievement(ZoneInfo zone)
    {
        if (zone.AchievementId == 0) return;

        if (AchievementProgress.TryGet(zone.AchievementId, out var current, out var max))
        {
            zone.AchievementCurrent = (int)current;
            zone.AchievementMax = (int)max;
        }
        else
        {
            AchievementProgress.Request(zone.AchievementId);
        }
    }

    public static void InvalidateUnlockedCache() => unlockedTerritoryCache = null;

    private static bool IsTerritoryUnlocked(uint territoryId)
    {
        var now = Environment.TickCount64;
        if (unlockedTerritoryCache is null || now - unlockedCacheTickMs > UnlockedCacheLifetimeMs)
        {
            unlockedTerritoryCache = BuildUnlockedSet();
            unlockedCacheTickMs = now;
        }
        return unlockedTerritoryCache.Contains(territoryId);
    }

    private static HashSet<uint> BuildUnlockedSet()
    {
        var set = new HashSet<uint>(capacity: 64);
        foreach (var a in Svc.AetheryteList)
            if (a.TerritoryId != 0) set.Add(a.TerritoryId);
        return set;
    }

    private static int CountActiveFatesInCurrentZone()
    {
        var count = 0;
        foreach (var f in Svc.Fates)
            if (f.State == Dalamud.Game.ClientState.Fates.FateState.Running) count++;
        return count;
    }
}
