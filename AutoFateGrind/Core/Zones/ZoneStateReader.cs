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
    public static bool IsTerritoryUnlocked(uint territoryId)
    {
        var attuned = AttunedSet();

        var own = ZoneAetherytes.AttunableIdsIn(territoryId);
        for (var index = 0; index < own.Length; index++)
        {
            if (attuned.Contains(own[index])) return true;
        }

        return ZoneAetherytes.TryFindGateway(territoryId, out var gateway)
            && attuned.Contains(gateway.AetheryteId);
    }

    // An empty list is "the game has not published one yet", not "nothing is attuned": callers that would
    // otherwise lock themselves out of every destination (issue #54) use this to fall back to no filtering.
    public static bool AnyAetheryteAttuned() => AttunedSet().Count > 0;

    private static HashSet<uint> AttunedSet()
    {
        var now = Environment.TickCount64;
        if (attunedAetheryteCache is null || now - attunedCacheTickMs > AttunedCacheLifetimeMs)
        {
            attunedAetheryteCache = BuildAttunedSet();
            attunedCacheTickMs = now;
        }
        return attunedAetheryteCache;
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
