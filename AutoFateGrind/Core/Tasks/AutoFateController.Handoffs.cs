using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Trading;
using clib.Services;
using System;

namespace AutoFateGrind.Core.Tasks;

internal sealed partial class AutoFateController
{
    private AutoCommon? currentTask;
    private AutoFate? grindTask;

    // clib calls OnCompleted from a thread-pool thread, where Dalamud's object table throws, and nothing observes that
    // exception: the trade hand-off's trader pick died there and the run stopped without a word (issue #97).
    private void RunTask(AutoCommon task, Action onCompleted)
    {
        currentTask = task;
        Svc.Automation.Start(task, OnCompleted: () =>
            ECommons.DalamudServices.Svc.Framework.RunOnFrameworkThread(() => CompleteTask(task, onCompleted)));
    }

    private void CompleteTask(AutoCommon task, Action onCompleted)
    {
        if (!ReferenceEquals(currentTask, task))
        {
            Diag($"{task.GetType().Name} finished but is no longer the current task (stopped, paused, or superseded); skipping hand-off.");
            return;
        }

        try
        {
            onCompleted();
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, $"Hand-off after {task.GetType().Name} failed; the run ends.");
            ECommons.DalamudServices.Svc.Chat.PrintError($"[AFG] The run stopped after {task.GetType().Name}: {exception.Message}. /xllog has the details.");
            EndRun(session);
        }
    }

    // clib.Automation buffers only one queued task; multi-step handoffs chain via OnCompleted.
    private void StartFateGrind(int startZoneIndex, AutoFateSession owningSession)
    {
        Phase = AutoPhase.Grinding;
        var startName = startZoneIndex < activeZones.Count ? activeZones[startZoneIndex].Name : "?";
        Diag($"FATE grind phase entering at zone[{startZoneIndex}] {startName}.");
        grindTask = new AutoFate(activeZones, owningSession, startZoneIndex);
        RunTask(grindTask, () => HandlePostFateHandoffs(owningSession));
    }

    private void HandlePostFateHandoffs(AutoFateSession owningSession)
    {
        if (owningSession != session)
        {
            Diag("FATE grind ended: owning session is stale (Stop was called or a new run replaced it). Ignoring hand-offs.");
            EndRun(owningSession);
            return;
        }

        if (owningSession.StopWhenSafe)
        {
            Diag("Soft stop: the grind task ended and no hand-off runs. Run ends.");
            EndRun(owningSession);
            return;
        }

        if (owningSession.PendingYokaiAdvance)
        {
            owningSession.PendingYokaiAdvance = false;
            if (PlanYokaiZones(owningSession))
            {
                StartFateGrind(CurrentTerritoryIndex(), owningSession);
                return;
            }

            Diag("Yo-kai hand-off found nothing left to farm. Run ends.");
            ECommons.DalamudServices.Svc.Chat.Print($"[AFG] Yo-kai goal met: {YokaiProgress.CompletionSummary(Plugin.Cfg)}. The roster is in /xllog.");
            owningSession.CompletedByStopCondition = true;
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
            RunTask(new AutoRepair(), () => HandlePostFateHandoffs(owningSession));
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

        var plan = TradeList.Plan(owningSession.GemstoneCurrent, origin.TerritoryId, origin.Expansion, owningSession.TradeSkippedItemIds);
        if (plan is null)
        {
            Diag("Trade hand-off dropped: nothing on the shopping list can be bought any more; resuming the grind.");
            ResumeGrindOrHumanize(owningSession, ResumeIndexFor(origin));
            return;
        }

        Phase = AutoPhase.Trading;
        Diag($"Trade phase entering: {plan.Trader.Name} for {plan.DescribeItems()}, origin zone {origin.Name} ({origin.TerritoryId}).");
        var trade = new AutoTrade(plan);
        RunTask(
            trade,
            () =>
            {
                if (owningSession != session)
                {
                    Diag("AutoTrade finished: owning session is stale; not resuming.");
                    EndRun(owningSession);
                    return;
                }
                SkipFailedTradeItems(owningSession, plan, trade);
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

    private static void SkipFailedTradeItems(AutoFateSession owningSession, TradePlan plan, AutoTrade trade)
    {
        var failedNames = new List<string>(plan.Orders.Length);
        for (var orderIndex = 0; orderIndex < plan.Orders.Length; orderIndex++)
        {
            var item = plan.Orders[orderIndex].Item;
            if (!trade.Failed(item.ItemId) || !owningSession.TradeSkippedItemIds.Add(item.ItemId))
            {
                continue;
            }

            failedNames.Add(item.ItemName);
        }

        if (failedNames.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", failedNames);
        Diag($"AutoTrade could not buy {names}; skipping them for the rest of the run.");
        ECommons.DalamudServices.Svc.Chat.PrintError($"[AFG] Auto-trade could not buy {names}, so the rest of this run skips them. /xllog has the details.");
    }

    // Runs after every other post-FATE hand-off has cleared. If the humanize threshold tripped while
    // we were repairing/trading, this is where the break actually fires; otherwise we resume the grind
    // directly. Bookkeeping (FatesSinceLastBreak reset, origin zone, break destination) lives here so the
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
        var picked = cfg.HumanizerEnabled ? HumanizeBreaks.Pick(cfg, owningSession.LastIdleSpot, rng) : null;
        if (picked is not { } plan)
        {
            Diag("Humanize hand-off skipped: feature disabled, or no saved idle spot and no selected city in the current catalog.");
            owningSession.ResetBreakCounter();
            StartFateGrind(resumeIndex, owningSession);
            return;
        }
        owningSession.LastIdleSpot = plan.Spot;
        var minMin = Math.Max(1, cfg.HumanizerBreakMinMinutes);
        var maxMin = Math.Max(minMin, cfg.HumanizerBreakMaxMinutes);
        var minutes = rng.Next(minMin, maxMin + 1);
        var durationMs = minutes * 60_000;

        // If the origin zone was dropped from the selection (e.g. user edited zones during the break),
        // fall back to the resume index we already have.
        var resumeIdx = ResumeIndexFor(origin, resumeIndex);

        Phase = AutoPhase.Humanizing;
        Diag($"Humanize phase entering: {plan.Describe()}, duration {minutes}m, resume zone {activeZones[resumeIdx].Name}.");
        var humanize = new AutoHumanize(plan, durationMs);
        RunTask(
            humanize,
            () =>
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
                    owningSession.ResetBreakCounter();
                    Diag($"Humanize finished: resuming FATE grind at {activeZones[resumeIdx].Name}.");
                }
                else
                {
                    Diag($"Humanize did not take a break (could not reach {plan.Place}); leaving counter at {owningSession.FatesSinceLastBreak} to retry next FATE. Resuming at {activeZones[resumeIdx].Name}.");
                }
                StartFateGrind(resumeIdx, owningSession);
            });
    }
}
