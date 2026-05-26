using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Zones;

// Resolves the per-zone Shared FATE Achievement.RowId — i.e., the "Free Market Friend"
// series introduced in Shadowbringers and extended in Endwalker/Dawntrail. Each Shared
// FATE zone has exactly one such achievement (rank 3 for ShB/EW, rank 4 for DT), with
// Name "Free Market Friend: <ZoneName>". The suffix after the colon matches the
// TerritoryType PlaceName 1:1.
//
// Pre-ShB expansions (ARR/HW/SB) have no per-zone Shared FATE mechanic — there is no
// in-game achievement that tracks FATE progress per zone for those expansions, so they
// resolve to 0. Diadem ("Crowning Achievement") and Occult Crescent ("Occult Erudition")
// are instanced and excluded from the zone registry.
internal static class ZoneAchievementResolver
{
    private const string NamePrefix = "Free Market Friend: ";

    private static Dictionary<string, uint>? cached;

    public static uint Resolve(string placeName)
    {
        if (string.IsNullOrWhiteSpace(placeName)) return 0;
        var map = cached ??= LoadFreeMarketFriendMap();
        return map.TryGetValue(NormalizeZone(placeName), out var rowId) ? rowId : 0;
    }

    public static void Invalidate() => cached = null;

    private static Dictionary<string, uint> LoadFreeMarketFriendMap()
    {
        var sheet = Svc.Data.GetExcelSheet<Achievement>();
        if (sheet is null)
        {
            Svc.Log.Warning("[AFG] Achievement sheet unavailable; per-zone resolution disabled.");
            return [];
        }

        var map = new Dictionary<string, uint>(capacity: 24);
        foreach (var a in sheet)
        {
            if (a.RowId == 0) continue;
            var name = a.Name.ExtractText();
            if (string.IsNullOrEmpty(name) || !name.StartsWith(NamePrefix, StringComparison.Ordinal)) continue;

            var zoneSuffix = name[NamePrefix.Length..];
            map[NormalizeZone(zoneSuffix)] = a.RowId;
        }

        Svc.Log.Info($"[AFG] Resolved {map.Count} Shared FATE achievements from Lumina.");
        return map;
    }

    private static string NormalizeZone(string s) => s.Trim().ToLowerInvariant();
}
