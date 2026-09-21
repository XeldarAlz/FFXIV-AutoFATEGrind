using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Modes;

namespace AutoFateGrind.Core.Zones;

internal static class ZoneSelection
{
    public static bool GoalPlansZones(Configuration cfg) => cfg.ActiveMode.Id == YokaiMedalsMode.ModeId;

    public static IReadOnlyList<ZoneInfo> ResolveStartList(Configuration cfg)
    {
        if (GoalPlansZones(cfg))
        {
            return YokaiProgress.ZonesFor(YokaiProgress.ResolveTargetIndex(cfg, 0));
        }

        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        return cfg.SelectedZones.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }
}
