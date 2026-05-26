using AutoFateGrind.Core.Zones;
using clib.Services;

namespace AutoFateGrind.Core.Tasks;

internal sealed class AutoFateController
{
    public bool Running => Svc.Automation.Running;
    public string Status => Svc.Automation.CurrentTask?.Status ?? "Idle";

    private AutoFateSession? session;
    public AutoFateSession? SessionSnapshot => session;

    public void Run(ZoneInfo zone)
    {
        session = new AutoFateSession();
        Svc.Automation.Start(new AutoFate(zone, session));
    }

    public void RunAll(IEnumerable<ZoneInfo> zones)
    {
        session = new AutoFateSession();
        var list = zones.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            Svc.Automation.Start(new AutoFate(list[i], session), queue: i > 0);
        }
        Svc.Automation.Start(new AutoFateBridge(this, session), queue: true);
    }

    public void Stop() => Svc.Automation.Stop();

    internal void HandleTradeIfPending()
    {
        if (session?.PendingTradeFromZone is not { } origin) return;

        var itemId = Plugin.Cfg.TargetTradeItemId;
        if (itemId == 0) { session.PendingTradeFromZone = null; return; }

        Svc.Automation.Start(new AutoTrade(itemId, origin.TerritoryId, origin.Expansion), queue: true);

        if (Plugin.Cfg.AfterTrade == AfterTradeAction.Resume)
            Svc.Automation.Start(new AutoFate(origin, session), queue: true);

        session.PendingTradeFromZone = null;
    }
}
