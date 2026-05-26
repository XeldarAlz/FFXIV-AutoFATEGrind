using AutoFateGrind.Core.Zones;
using clib.Services;

namespace AutoFateGrind.Core.Tasks;

internal sealed class AutoFateController
{
    public bool Running => Svc.Automation.Running;
    public string Status => Svc.Automation.CurrentTask?.Status ?? "Idle";

    private AutoFateSession? session;
    private IReadOnlyList<ZoneInfo> activeZones = [];
    public AutoFateSession? SessionSnapshot => session;

    public void RunAll(IEnumerable<ZoneInfo> zones)
    {
        session = new AutoFateSession();
        activeZones = zones.ToList();
        if (activeZones.Count == 0) return;
        Svc.Automation.Start(new AutoFate(activeZones, session));
        Svc.Automation.Start(new AutoFateBridge(this, session), queue: true);
    }

    public void Stop() => Svc.Automation.Stop();

    internal void HandleTradeIfPending()
    {
        if (session?.PendingTradeFromZone is not { } origin) return;

        var itemId = Plugin.Cfg.TargetTradeItemId;
        if (itemId == 0) { session.PendingTradeFromZone = null; return; }

        Svc.Automation.Start(new AutoTrade(itemId, origin.TerritoryId, origin.Expansion), queue: true);

        if (Plugin.Cfg.AfterTrade == AfterTradeAction.Resume && activeZones.Count > 0)
        {
            var resumeIndex = 0;
            for (var i = 0; i < activeZones.Count; i++)
                if (activeZones[i].TerritoryId == origin.TerritoryId) { resumeIndex = i; break; }
            Svc.Automation.Start(new AutoFate(activeZones, session, resumeIndex), queue: true);
            Svc.Automation.Start(new AutoFateBridge(this, session), queue: true);
        }

        session.PendingTradeFromZone = null;
    }
}
