using ECommons.DalamudServices;
using System.Numerics;
using EcMap = ECommons.GameHelpers.Map;

namespace AutoFateGrind.Core.Zones;

internal readonly record struct ZoneAetheryte(uint Id, string Name, Vector3 Position);

internal static class ZoneAetherytes
{
    private static readonly Dictionary<uint, ZoneAetheryte[]> byTerritory = new();

    public static bool TryFindNearest(uint territoryId, Vector3 target, out ZoneAetheryte nearest)
    {
        var candidates = InTerritory(territoryId);
        nearest = default;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < candidates.Length; index++)
        {
            var distance = Vector3.DistanceSquared(candidates[index].Position, target);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            nearest = candidates[index];
        }
        return bestDistance < float.MaxValue;
    }

    private static ZoneAetheryte[] InTerritory(uint territoryId)
    {
        if (byTerritory.TryGetValue(territoryId, out var cached)) return cached;
        var resolved = ResolveTeleportableAetherytes(territoryId);
        byTerritory[territoryId] = resolved;
        return resolved;
    }

    private static ZoneAetheryte[] ResolveTeleportableAetherytes(uint territoryId)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
        if (sheet is null) return [];

        var found = new List<ZoneAetheryte>(4);
        foreach (var row in sheet)
        {
            if (!row.IsAetheryte) continue;
            if (row.Territory.RowId != territoryId) continue;
            if (!TryResolvePosition(row, out var position)) continue;
            found.Add(new ZoneAetheryte(row.RowId, ResolveName(row), position));
        }
        return found.ToArray();
    }

    private static string ResolveName(Lumina.Excel.Sheets.Aetheryte row)
    {
        var name = row.PlaceName.ValueNullable?.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? $"aetheryte #{row.RowId}" : name;
    }

    private static bool TryResolvePosition(Lumina.Excel.Sheets.Aetheryte row, out Vector3 position)
    {
        try
        {
            position = EcMap.AetherytePosition(row);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, $"[AFG] Could not resolve a position for aetheryte {row.RowId}; skipping it as a teleport target");
            position = default;
            return false;
        }
    }
}
