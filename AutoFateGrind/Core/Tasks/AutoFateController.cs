using AutoFateGrind.Core.Game;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;
using clib.Services;

namespace AutoFateGrind.Core.Tasks;

internal sealed class AutoFateController
{
    public bool Running => Svc.Automation.Running;
    public string Status => Svc.Automation.CurrentTask?.Status ?? "Idle";
    public AutoPhase Phase { get; private set; } = AutoPhase.Idle;

    private AutoFateSession? session;
    private IReadOnlyList<ZoneInfo> activeZones = [];
    public AutoFateSession? SessionSnapshot => session;

    private static readonly Random rng = new();

    private static void Diag(string message)
        => ECommons.DalamudServices.Svc.Log.Info($"[AFG] {message}");

    public void RunAll(IEnumerable<ZoneInfo> zones)
    {
        activeZones = zones.ToList();
        if (activeZones.Count == 0)
        {
            Diag("Start aborted: no zones selected.");
            return;
        }

        var startWallet = GemstoneCatalog.CurrentWalletCount();
        var s = new AutoFateSession
        {
            GemstoneStart = startWallet,
            GemstoneCurrent = startWallet,
        };
        session = s;
        Diag($"Run starting: {activeZones.Count} zone(s), mode {Plugin.Cfg.ActiveMode.DisplayName}, wallet {startWallet}g, threshold {Plugin.Cfg.TradeThreshold}g, trade-on-cap {(Plugin.Cfg.TradeOnCap ? "on" : "off")}.");

        ApplyStartingClass();
        StartFateGrind(0, s);
    }

    private static void ApplyStartingClass()
    {
        var cfg = Plugin.Cfg;
        if (!cfg.ApplyClassOnStart) return;
        if (cfg.ClassQueue.Count == 0) return;

        var idx = ClassSwitcher.FindActiveEntryIndex(cfg.ClassQueue);
        if (idx < 0)
        {
            ECommons.DalamudServices.Svc.Chat.Print("[AFG] Class queue: every entry is at its level cap, staying on current class.");
            return;
        }
        var entry = cfg.ClassQueue[idx];
        var label = $"gearset {entry.GearsetIndex} ({ClassSwitcher.JobNameForUserIndex(entry.GearsetIndex)})";
        if (ClassSwitcher.TryEquip(entry))
            ECommons.DalamudServices.Svc.Chat.Print($"[AFG] Switching to {label}.");
        else
            ECommons.DalamudServices.Svc.Chat.PrintError($"[AFG] Could not equip {label} (game refused — combat, mount, or transient lock?). See /xllog for details.");
    }

    public void Stop()
    {
        var wasRunning = session is not null;
        Svc.Automation.Stop();
        session = null;
        activeZones = [];
        Phase = AutoPhase.Idle;
        if (wasRunning) Diag("Stop requested; session cleared.");
    }

    // clib.Automation buffers only one queued task; multi-step handoffs chain via OnCompleted.
    private void StartFateGrind(int startZoneIndex, AutoFateSession owningSession)
    {
        Phase = AutoPhase.Grinding;
        var startName = startZoneIndex < activeZones.Count ? activeZones[startZoneIndex].Name : "?";
        Diag($"FATE grind phase entering at zone[{startZoneIndex}] {startName}.");
        Svc.Automation.Start(
            new AutoFate(activeZones, owningSession, startZoneIndex),
            OnCompleted: () => HandlePostFateHandoffs(owningSession));
    }

    private void HandlePostFateHandoffs(AutoFateSession owningSession)
    {
        if (owningSession != session)
        {
            Diag("FATE grind ended: owning session is stale (Stop was called or a new run replaced it). Ignoring hand-offs.");
            Phase = AutoPhase.Idle;
            return;
        }

        if (owningSession.PendingRepair)
        {
            owningSession.PendingRepair = false;
            Phase = AutoPhase.Repairing;
            Diag("Repair phase entering.");
            // After repair, fall back into this same dispatcher so any pending trade also runs before
            // we resume the FATE grind.
            Svc.Automation.Start(
                new AutoRepair(),
                OnCompleted: () => HandlePostFateHandoffs(owningSession));
            return;
        }

        if (owningSession.PendingTradeFromZone is not { } origin)
        {
            // No more hand-offs; either we never queued one (stop condition / error), or we just
            // finished the trade phase. Resume the FATE grind if we ended on repair-only.
            if (Phase == AutoPhase.Repairing && activeZones.Count > 0)
            {
                var resumeIndex = 0;
                if (owningSession.PendingRepairFromZone is { } repairOrigin)
                    for (var i = 0; i < activeZones.Count; i++)
                        if (activeZones[i].TerritoryId == repairOrigin.TerritoryId) { resumeIndex = i; break; }
                owningSession.PendingRepairFromZone = null;
                Diag($"Repair finished with no pending trade; resuming FATE grind at {activeZones[resumeIndex].Name}.");
                ResumeGrindOrHumanize(owningSession, resumeIndex);
                return;
            }
            if (owningSession.PendingHumanize && activeZones.Count > 0)
            {
                var resumeIndex = 0;
                if (owningSession.PendingHumanizeFromZone is { } humOrigin)
                    for (var i = 0; i < activeZones.Count; i++)
                        if (activeZones[i].TerritoryId == humOrigin.TerritoryId) { resumeIndex = i; break; }
                Diag($"Humanize triggered with no other hand-offs; entering break from {activeZones[resumeIndex].Name}.");
                ResumeGrindOrHumanize(owningSession, resumeIndex);
                return;
            }
            if (owningSession.EndedWithFault && TryAutoResumeAfterFault(owningSession))
                return;
            Diag("FATE grind ended without queueing further hand-offs. Run ends.");
            Phase = AutoPhase.Idle;
            return;
        }

        owningSession.PendingTradeFromZone = null;

        var itemId = GemstoneCatalog.EnsurePersistedTarget();
        if (itemId == 0)
        {
            Diag("Trade hand-off aborted: EnsurePersistedTarget returned 0 (no purchasable item resolvable). Run ends.");
            Phase = AutoPhase.Idle;
            return;
        }

        Phase = AutoPhase.Trading;
        Diag($"Trade phase entering: item {itemId}, origin zone {origin.Name} ({origin.TerritoryId}).");
        Svc.Automation.Start(
            new AutoTrade(itemId, origin.TerritoryId, origin.Expansion),
            OnCompleted: () =>
            {
                if (owningSession != session)
                {
                    Diag("AutoTrade finished: owning session is stale; not resuming.");
                    Phase = AutoPhase.Idle;
                    return;
                }
                if (Plugin.Cfg.AfterTrade != AfterTradeAction.Resume)
                {
                    Diag($"AutoTrade finished: AfterTrade = {Plugin.Cfg.AfterTrade}; not resuming.");
                    Phase = AutoPhase.Idle;
                    return;
                }
                if (activeZones.Count == 0)
                {
                    Diag("AutoTrade finished: no active zones recorded; cannot resume.");
                    Phase = AutoPhase.Idle;
                    return;
                }

                var resumeIndex = 0;
                for (var i = 0; i < activeZones.Count; i++)
                    if (activeZones[i].TerritoryId == origin.TerritoryId) { resumeIndex = i; break; }

                Diag($"AutoTrade finished: resuming FATE grind at {activeZones[resumeIndex].Name}.");
                ResumeGrindOrHumanize(owningSession, resumeIndex);
            });
    }

    private const int  MaxFaultResumes     = 3;
    private const long FaultResumeWindowMs = 5 * 60_000;

    // Bounded restart after the grind task ended by throwing (gated on AutoResumeOnFault). The window
    // resets once it lapses, so sparse faults over a long run each get a fresh budget; only a burst — a
    // hard wedge that re-faults immediately — exhausts the budget and lets the run end for real.
    private bool TryAutoResumeAfterFault(AutoFateSession s)
    {
        s.EndedWithFault = false;
        if (activeZones.Count == 0) return false;

        var now = Environment.TickCount64;
        if (now - s.FaultWindowStartedAtMs > FaultResumeWindowMs)
        {
            s.FaultResumeCount = 0;
            s.FaultWindowStartedAtMs = now;
        }
        if (s.FaultResumeCount >= MaxFaultResumes)
        {
            Diag($"Grind task faulted {s.FaultResumeCount}× within {FaultResumeWindowMs / 60000}m; not resuming. Run ends.");
            return false;
        }

        s.FaultResumeCount++;
        var idx = Math.Clamp(s.FaultResumeZoneIndex, 0, activeZones.Count - 1);
        Diag($"Grind task ended with an unexpected fault; auto-resuming at {activeZones[idx].Name} (resume {s.FaultResumeCount}/{MaxFaultResumes} this {FaultResumeWindowMs / 60000}m window).");
        StartFateGrind(idx, s);
        return true;
    }

    // Runs after every other post-FATE hand-off has cleared. If the humanize threshold tripped while
    // we were repairing/trading, this is where the break actually fires; otherwise we resume the grind
    // directly. Bookkeeping (FatesSinceLastBreak reset, origin zone, city selection) lives here so the
    // trigger site only has to set a flag.
    private void ResumeGrindOrHumanize(AutoFateSession owningSession, int resumeIndex)
    {
        if (!owningSession.PendingHumanize)
        {
            StartFateGrind(resumeIndex, owningSession);
            return;
        }

        owningSession.PendingHumanize = false;
        var origin = owningSession.PendingHumanizeFromZone;
        owningSession.PendingHumanizeFromZone = null;

        var cfg = Plugin.Cfg;
        if (!cfg.HumanizerEnabled || cfg.HumanizerCities.Count == 0)
        {
            Diag("Humanize hand-off skipped: feature disabled or no cities selected.");
            owningSession.FatesSinceLastBreak = 0;
            StartFateGrind(resumeIndex, owningSession);
            return;
        }

        // Filter against the catalog so cities removed from the registry (e.g. Ul'dah, dropped due to
        // navmesh issues) are ignored even if they're still in an older saved config.
        var cities = cfg.HumanizerCities.Where(id => Core.Zones.CityCatalog.Find(id) is not null).ToArray();
        if (cities.Length == 0)
        {
            Diag("Humanize hand-off skipped: no selected cities are in the current catalog.");
            owningSession.FatesSinceLastBreak = 0;
            StartFateGrind(resumeIndex, owningSession);
            return;
        }
        var cityId = cities[rng.Next(cities.Length)];
        var minMin = Math.Max(1, cfg.HumanizerBreakMinMinutes);
        var maxMin = Math.Max(minMin, cfg.HumanizerBreakMaxMinutes);
        var minutes = rng.Next(minMin, maxMin + 1);
        var durationMs = minutes * 60_000;

        // If the origin zone was dropped from the selection (e.g. user edited zones during the break),
        // fall back to the resume index we already have.
        var resumeIdx = resumeIndex;
        if (origin is not null)
            for (var i = 0; i < activeZones.Count; i++)
                if (activeZones[i].TerritoryId == origin.TerritoryId) { resumeIdx = i; break; }

        Phase = AutoPhase.Humanizing;
        Diag($"Humanize phase entering: city {cityId}, duration {minutes}m, resume zone {activeZones[resumeIdx].Name}.");
        Svc.Automation.Start(
            new AutoHumanize(cityId, durationMs),
            OnCompleted: () =>
            {
                if (owningSession != session)
                {
                    Diag("Humanize finished: owning session is stale; not resuming.");
                    Phase = AutoPhase.Idle;
                    return;
                }
                owningSession.FatesSinceLastBreak = 0;
                Diag($"Humanize finished: resuming FATE grind at {activeZones[resumeIdx].Name}.");
                StartFateGrind(resumeIdx, owningSession);
            });
    }
}

internal enum AutoPhase { Idle, Grinding, Trading, Repairing, Humanizing }
