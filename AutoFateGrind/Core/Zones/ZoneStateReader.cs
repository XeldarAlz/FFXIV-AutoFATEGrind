using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Zones;

// Refreshes per-zone live state. v1 stub: leave Unlocked=true and zero achievement
// progress until the AgentFateProgress reader is wired in. ActiveFateCount is filled
// for the current territory only by counting Svc.Fates entries.
internal static class ZoneStateReader
{
    public static void Refresh(ZoneInfo zone)
    {
        // TODO: read AgentFateProgress.Instance()->Tabs[].Zones[] for AchievementCurrent/Max + Unlocked.
        // TODO: read achievement progress via Achievement.Instance()->IsComplete / progress payload.
        zone.Unlocked = true;

        zone.ActiveFateCount = Svc.ClientState.TerritoryType == zone.TerritoryId
            ? CountActiveFatesInCurrentZone()
            : 0;
    }

    private static int CountActiveFatesInCurrentZone()
    {
        var count = 0;
        foreach (var f in Svc.Fates)
        {
            if (f.State == Dalamud.Game.ClientState.Fates.FateState.Running) count++;
        }
        return count;
    }
}
