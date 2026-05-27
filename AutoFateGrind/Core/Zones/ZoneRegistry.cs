using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Zones;

public static class ZoneRegistry
{
    private static ZoneInfo[]? cached;

    public static ZoneInfo[] Zones => cached ??= LoadFromLumina();

    public static IEnumerable<ZoneInfo> ByExpansion(ExpansionKind exp) =>
        Zones.Where(z => z.Expansion == exp);

    public static void Invalidate() => cached = null;

    private static ZoneInfo[] LoadFromLumina()
    {
        var sheet = Svc.Data.GetExcelSheet<TerritoryType>();
        if (sheet is null) return [];

        var result = new List<ZoneInfo>(capacity: 80);
        foreach (var t in sheet)
        {
            if (!IsFateZone(t)) continue;

            var name = t.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;

            result.Add(new ZoneInfo
            {
                TerritoryId = t.RowId,
                Name        = name,
                Expansion   = ExpansionKindExtensions.FromExVersion(t.ExVersion.RowId),
                MinLevel    = 1,
            });
        }

        return [.. result
            .OrderBy(z => z.Expansion)
            .ThenBy(z => z.Name, StringComparer.OrdinalIgnoreCase)];
    }

    // 1 = Standard Field (overworld zones with FATEs).
    private const byte StandardFieldUse = 1;

    private static bool IsFateZone(TerritoryType t)
    {
        if (t.RowId == 0) return false;
        if (t.IsPvpZone) return false;
        if (t.ExVersion.RowId > 5) return false;
        if (t.TerritoryIntendedUse.ValueNullable?.RowId != StandardFieldUse) return false;
        return true;
    }
}
