using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Zones;

// Resolves the per-zone "Date with Destiny" Achievement.RowId by scanning the Lumina
// Achievement sheet for entries whose Description text mentions the zone's PlaceName.
//
// Works for HW+ (one achievement per zone, e.g. "Like a Boss" for the Sea of Clouds)
// and ARR (one regional achievement listing all member zones, e.g. "A Date with the
// Twelveswood" listing Central/East/South/North Shroud). When multiple tiers exist
// for the same zone, picks the lowest Order (60-FATE first tier).
internal static class ZoneAchievementResolver
{
    private readonly record struct FateAchievement(uint RowId, string DescriptionLower, ushort Order);

    private static FateAchievement[]? cached;

    public static uint Resolve(string placeName)
    {
        if (string.IsNullOrWhiteSpace(placeName)) return 0;
        var entries = cached ??= LoadFateAchievements();
        if (entries.Length == 0) return 0;

        var needle = placeName.ToLowerInvariant();

        uint bestRowId = 0;
        ushort bestOrder = ushort.MaxValue;

        foreach (var e in entries)
        {
            if (!e.DescriptionLower.Contains(needle)) continue;
            if (e.Order < bestOrder)
            {
                bestOrder = e.Order;
                bestRowId = e.RowId;
            }
        }

        return bestRowId;
    }

    public static void Invalidate() => cached = null;

    private static FateAchievement[] LoadFateAchievements()
    {
        var sheet = Svc.Data.GetExcelSheet<Achievement>();
        if (sheet is null) return [];

        var result = new List<FateAchievement>(capacity: 64);
        foreach (var a in sheet)
        {
            if (a.RowId == 0) continue;
            var desc = a.Description.ExtractText();
            if (string.IsNullOrEmpty(desc)) continue;

            var descLower = desc.ToLowerInvariant();
            // Narrow to "Complete N FATEs in <place>" entries. "complete" + "fate" is
            // the smallest pair of tokens that reliably catches the Date-with-Destiny
            // series across all expansions without sweeping in slay/boss-in-FATE
            // achievements that don't track per-zone FATE counts.
            if (!descLower.Contains("fate")) continue;
            if (!descLower.Contains("complete")) continue;

            result.Add(new FateAchievement(a.RowId, descLower, (ushort)a.Order));
        }

        return [.. result];
    }
}
