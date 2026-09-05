using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Zones;

internal static class ZoneStateReader
{
    private static HashSet<uint>? attunedAetheryteCache;
    private static long attunedCacheTickMs;
    private const int AttunedCacheLifetimeMs = 1000;

    public static void Refresh(ZoneInfo zone)
    {
        zone.Unlocked = IsTerritoryUnlocked(zone.TerritoryId);
        zone.ActiveFateCount = Svc.ClientState.TerritoryType == zone.TerritoryId
            ? CountActiveFatesInCurrentZone()
            : 0;
    }

    // Keyed by attuned aetheryte, not by the territory that aetheryte sits in: The Dravanian Hinterlands
    // holds no aetheryte of its own, so a territory-keyed set left it locked forever (issue #48).
    private static bool IsTerritoryUnlocked(uint territoryId)
    {
        var now = Environment.TickCount64;
        if (attunedAetheryteCache is null || now - attunedCacheTickMs > AttunedCacheLifetimeMs)
        {
            attunedAetheryteCache = BuildAttunedSet();
            attunedCacheTickMs = now;
        }

        var own = ZoneAetherytes.AttunableIdsIn(territoryId);
        for (var index = 0; index < own.Length; index++)
        {
            if (attunedAetheryteCache.Contains(own[index])) return true;
        }

        return ZoneAetherytes.TryFindGateway(territoryId, out var gateway)
            && attunedAetheryteCache.Contains(gateway.AetheryteId);
    }

    private static HashSet<uint> BuildAttunedSet()
    {
        var set = new HashSet<uint>(capacity: 64);
        foreach (var entry in Svc.AetheryteList)
        {
            if (entry.AetheryteId != 0) set.Add(entry.AetheryteId);
        }
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
