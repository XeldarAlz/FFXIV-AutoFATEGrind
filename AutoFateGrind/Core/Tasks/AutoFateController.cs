using AutoFateGrind.Core.Game;
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
        ApplyStartingClass();
        StartFateGrind(0, s);
    }

    private static void ApplyStartingClass()
    {
        var cfg = Plugin.Cfg;
        if (!cfg.ApplyClassOnStart) return;
        if (cfg.ClassQueue.Count == 0) return;

        var idx = ClassSwitcher.FindActiveEntryIndex(cfg.ClassQueue);
        if (idx < 0) return;
        var entry = cfg.ClassQueue[idx];
        if (ClassSwitcher.TryEquip(entry))
            ECommons.DalamudServices.Svc.Chat.Print($"[AFG] Switching to gearset {entry.GearsetIndex} ({ClassSwitcher.JobNameForUserIndex(entry.GearsetIndex)}).");
    }

    public void Stop()
    {
        Svc.Automation.Stop();
        session = null;
        activeZones = [];
    }

    // clib.Automation buffers only one queued task; multi-step handoffs chain via OnCompleted.
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
