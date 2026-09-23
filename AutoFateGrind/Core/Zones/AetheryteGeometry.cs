using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoFateGrind.Core.Zones;

// Map offsets are in world units; clib and ECommons scale them by 0.001, which shifts every shard with no Level
// row by the whole offset (86 m in Tuliyollal, 89 m in Solution Nine: issue #75). A marker can also sit on a
// territory's second map (Eulmore, Steps of Thal), so each map converts its own marker with its own scale.
internal static class AetheryteGeometry
{
    private const byte AetheryteMarkerType = 3;
    private const byte AethernetMarkerType = 4;
    private const float MapCentrePixels = 1024f;
    private const float SizeFactorToScale = 0.01f;

    public static bool TryResolvePosition(Aetheryte row, out Vector3 position)
    {
        if (row.Level[0].ValueNullable is { } level)
        {
            position = new Vector3(level.X, level.Y, level.Z);
            return true;
        }

        position = default;
        if (row.Territory.ValueNullable is not { } territory) return false;
        if (territory.Map.ValueNullable is { } mainMap && TryResolveOnMap(row, mainMap, out position)) return true;

        var maps = Svc.Data.GetExcelSheet<Map>();
        if (maps is null) return false;
        foreach (var map in maps)
        {
            if (map.TerritoryType.RowId != territory.RowId || map.RowId == territory.Map.RowId) continue;
            if (TryResolveOnMap(row, map, out position)) return true;
        }
        return false;
    }

    private static bool TryResolveOnMap(Aetheryte row, Map map, out Vector3 position)
    {
        position = default;
        if (!TryFindMarker(row, map.MapMarkerRange, out var marker)) return false;

        var scale = map.SizeFactor * SizeFactorToScale;
        position = new Vector3(PixelToWorld(marker.X, scale, map.OffsetX), 0f, PixelToWorld(marker.Y, scale, map.OffsetY));
        return true;
    }

    private static bool TryFindMarker(Aetheryte row, uint markerRange, out MapMarker marker)
    {
        marker = default;
        if (Svc.Data.GetSubrowExcelSheet<MapMarker>()?.GetRowOrDefault(markerRange) is not { } markers) return false;

        for (var index = 0; index < markers.Count; index++)
        {
            if (markers[index].DataType != AetheryteMarkerType || markers[index].DataKey.RowId != row.RowId) continue;
            marker = markers[index];
            return true;
        }

        if (row.AethernetName.RowId == 0) return false;
        for (var index = 0; index < markers.Count; index++)
        {
            if (markers[index].DataType != AethernetMarkerType || markers[index].DataKey.RowId != row.AethernetName.RowId) continue;
            marker = markers[index];
            return true;
        }
        return false;
    }

    private static float PixelToWorld(short pixel, float scale, short offset)
        => (pixel - MapCentrePixels) / scale - offset;
}
