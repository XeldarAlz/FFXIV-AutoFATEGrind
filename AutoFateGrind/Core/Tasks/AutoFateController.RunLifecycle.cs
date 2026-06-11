using AutoFateGrind.Core.Stats;
using clib.Services;
using System;

namespace AutoFateGrind.Core.Tasks;

internal sealed partial class AutoFateController
{
    // Terminal choke point so every end-run path records and returns to Idle.
    private void EndRun(AutoFateSession? s)
    {
        FinalizeRun(s);
        Phase = AutoPhase.Idle;
        MaybeRunAfterAction(s);
    }

    // After-action gates: s must be the live session and have ended via stop condition (not manual Stop or fault).
    private void MaybeRunAfterAction(AutoFateSession? s)
    {
        if (s is null || s != session || !s.CompletedByStopCondition || s.AfterActionDispatched) return;
        s.AfterActionDispatched = true;

        var action = Plugin.Cfg.AfterRun;
        if (action == AfterRunAction.StayLoggedIn)
        {
            Diag("Run completed by stop condition; after-run action = StayLoggedIn (no-op).");
            return;
        }

        Diag($"Run completed by stop condition; starting after-run action {action}.");
        Phase = AutoPhase.Finishing;
        AutoCommon task = action == AfterRunAction.ReturnToInn ? new AutoReturnToInn() : new AutoAfterRun(action);
        Svc.Automation.Start(task, OnCompleted: () =>
        {
            Diag($"After-run action {action} finished.");
            Phase = AutoPhase.Idle;
        });
    }

    // Writes a finished run to history exactly once. Idempotent (guarded by session.Recorded) so the many
    // terminal hand-off paths and an explicit Stop can all call it without double-counting. Purely additive
    // — never touches grind control flow, so a history failure can't wedge automation.
    private void FinalizeRun(AutoFateSession? s)
    {
        if (s is null || s.Recorded) return;
        if (s.CompletedCount == 0 && s.ExpEarned == 0 && s.GemstonesEarned == 0) { s.Recorded = true; return; }
        s.Recorded = true;
        try
        {
            s.UpdateExp();
            var record = new RunRecord
            {
                StartedAtUtc = s.StartedAt,
                EndedAtUtc = DateTime.UtcNow,
                DurationSeconds = s.Elapsed.TotalSeconds,
                FatesCompleted = s.CompletedCount,
                GemstonesEarned = s.GemstonesEarned,
                ExpEarned = s.ExpEarned,
                LevelsGained = s.LevelsGained,
                StartLevel = s.StartLevel,
                EndLevel = s.CurrentLevel,
                JobId = s.JobId,
                JobAbbr = s.JobAbbr,
                ModeName = Plugin.Cfg.ActiveMode.DisplayName,
                ZoneNames = activeZones.Select(z => z.Name).ToList(),
            };
            Plugin.Instance.History.Append(record);
            Diag($"Run recorded to history: {record.FatesCompleted} FATEs, {record.GemstonesEarned}g, {record.ExpEarned} exp over {record.Duration}.");
        }
        catch (Exception ex)
        {
            Diag($"FinalizeRun failed to record history: {ex.Message}");
        }
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
}
