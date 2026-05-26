using AutoFateGrind.Core.Trading;
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
        var s = new AutoFateSession();
        session = s;
        activeZones = zones.ToList();
        if (activeZones.Count == 0) return;
        StartFateGrind(0, s);
    }

    public void Stop()
    {
        Svc.Automation.Stop();
        session = null;
        activeZones = [];
    }

    // clib.Automation only buffers one queued task, so multi-step handoffs (FATE → trade →
    // resume FATE) must chain through OnCompleted instead of stacking Start(queue:true) calls.
    // Capturing `owningSession` lets stale callbacks from a stopped/restarted run bail cleanly.
    private void StartFateGrind(int startZoneIndex, AutoFateSession owningSession)
    {
        Svc.Automation.Start(
            new AutoFate(activeZones, owningSession, startZoneIndex),
            OnCompleted: () => HandleTradeIfPending(owningSession));
    }

    private void HandleTradeIfPending(AutoFateSession owningSession)
    {
        if (owningSession != session) return;
        if (owningSession.PendingTradeFromZone is not { } origin) return;

        owningSession.PendingTradeFromZone = null;

        var itemId = GemstoneCatalog.EnsurePersistedTarget();
        if (itemId == 0) return;

        Svc.Automation.Start(
            new AutoTrade(itemId, origin.TerritoryId, origin.Expansion),
            OnCompleted: () =>
            {
                if (owningSession != session) return;
                if (Plugin.Cfg.AfterTrade != AfterTradeAction.Resume) return;
                if (activeZones.Count == 0) return;

                var resumeIndex = 0;
                for (var i = 0; i < activeZones.Count; i++)
                    if (activeZones[i].TerritoryId == origin.TerritoryId) { resumeIndex = i; break; }

                StartFateGrind(resumeIndex, owningSession);
            });
    }
}
