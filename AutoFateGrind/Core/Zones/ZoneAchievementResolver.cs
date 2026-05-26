using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Zones;

// "Free Market Friend: <ZoneName>" achievements (ShB/EW/DT). Pre-ShB has no equivalent → 0.
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
