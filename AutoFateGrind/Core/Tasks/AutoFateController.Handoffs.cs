using AutoFateGrind.Core.Trading;
using clib.Services;
using System;

namespace AutoFateGrind.Core.Tasks;

internal sealed partial class AutoFateController
{
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
            EndRun(owningSession);
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
                var resumeIndex = ResumeIndexFor(owningSession.PendingRepairFromZone);
                owningSession.PendingRepairFromZone = null;
                Diag($"Repair finished with no pending trade; resuming FATE grind at {activeZones[resumeIndex].Name}.");
                ResumeGrindOrHumanize(owningSession, resumeIndex);
                return;
            }
            if (owningSession.PendingHumanize && activeZones.Count > 0)
            {
                var resumeIndex = ResumeIndexFor(owningSession.PendingHumanizeFromZone);
                Diag($"Humanize triggered with no other hand-offs; entering break from {activeZones[resumeIndex].Name}.");
                ResumeGrindOrHumanize(owningSession, resumeIndex);
                return;
            }
            if (owningSession.EndedWithFault && TryAutoResumeAfterFault(owningSession))
                return;
            Diag("FATE grind ended without queueing further hand-offs. Run ends.");
            EndRun(owningSession);
            return;
        }

        owningSession.PendingTradeFromZone = null;

        var itemId = GemstoneCatalog.EnsurePersistedTarget();
        if (itemId == 0)
        {
            Diag("Trade hand-off aborted: EnsurePersistedTarget returned 0 (no purchasable item resolvable). Run ends.");
            EndRun(owningSession);
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
                    EndRun(owningSession);
                    return;
                }
                if (Plugin.Cfg.AfterTrade != AfterTradeAction.Resume)
                {
                    Diag($"AutoTrade finished: AfterTrade = {Plugin.Cfg.AfterTrade}; not resuming.");
                    EndRun(owningSession);
                    return;
                }
                if (activeZones.Count == 0)
                {
                    Diag("AutoTrade finished: no active zones recorded; cannot resume.");
                    EndRun(owningSession);
                    return;
                }

                var resumeIndex = ResumeIndexFor(origin);

                Diag($"AutoTrade finished: resuming FATE grind at {activeZones[resumeIndex].Name}.");
                ResumeGrindOrHumanize(owningSession, resumeIndex);
            });
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
        var resumeIdx = ResumeIndexFor(origin, resumeIndex);

        Phase = AutoPhase.Humanizing;
        Diag($"Humanize phase entering: city {cityId}, duration {minutes}m, resume zone {activeZones[resumeIdx].Name}.");
        var humanize = new AutoHumanize(cityId, durationMs);
        Svc.Automation.Start(
            humanize,
            OnCompleted: () =>
            {
                if (owningSession != session)
                {
                    Diag("Humanize finished: owning session is stale; not resuming.");
                    EndRun(owningSession);
                    return;
                }
                // Only consume the break when it actually happened. A teleport-abort leaves the counter
                // intact so the threshold re-trips on the next completed FATE and the break retries.
                if (humanize.BreakTaken)
                {
                    owningSession.FatesSinceLastBreak = 0;
                    Diag($"Humanize finished: resuming FATE grind at {activeZones[resumeIdx].Name}.");
                }
                else
                {
                    Diag($"Humanize did not take a break (could not reach city); leaving counter at {owningSession.FatesSinceLastBreak} to retry next FATE. Resuming at {activeZones[resumeIdx].Name}.");
                }
                StartFateGrind(resumeIdx, owningSession);
            });
    }
}
