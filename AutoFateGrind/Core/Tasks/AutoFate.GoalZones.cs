using AutoFateGrind.Core.Game.Items;
using AutoFateGrind.Core.Game.Player;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Zones;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private bool GoalZoneDone() => ZoneDone(zone);

    private bool GoalZoneDoneAt(int candidateIndex) => ZoneDone(zones[candidateIndex]);

    private static bool ZoneDone(ZoneInfo candidate)
        => RunLimits.ZoneShareDone(Plugin.Cfg, candidate) || Plugin.Cfg.ActiveMode.IsZoneDone(candidate.TerritoryId);

    private static string ZoneDoneReason(ZoneInfo done)
        => RunLimits.ZoneShareDone(Plugin.Cfg, done)
            ? $"{done.CompletedThisRun} of {RunLimits.FatesPerZoneCap(Plugin.Cfg)} FATEs done here"
            : Plugin.Cfg.ActiveMode.ZoneDoneReason(done.TerritoryId);

    private async Task<ExitReason> LeaveDoneZone()
    {
        var done = zone;
        var reason = ZoneDoneReason(done);
        Status = $"{done.Name} is done; moving on";
        await HoldForCollectReward();
        await WaitOutSettle();
        await ClearBlockingCombat();
        if (CancelToken.IsCancellationRequested)
        {
            return ExitReason.Quit;
        }

        if (AdvanceZone())
        {
            Diag($"{done.Name}: {reason}; moving on to {zone.Name}");
            Svc.Chat.Print($"[AFG] {done.Name}: {reason}. Moving on to {zone.Name}.");
            return ExitReason.Continue;
        }

        Diag($"{done.Name}: {reason}, and no other reachable zone is left; ending the run");
        Svc.Chat.Print($"[AFG] {done.Name}: {reason}, and no other reachable zone is left in the plan.");
        Status = "Every reachable zone is done";
        session.CompletedByStopCondition = true;
        return ExitReason.Quit;
    }

    private void ReportGoalMet()
    {
        ReportYokaiGoalMet();
        ReportItemGoalMet();
        ReportLevelLimitCutShort();
    }

    private void ReportLevelLimitCutShort()
    {
        var cfg = Plugin.Cfg;
        var levelCap = RunLimits.LevelCap(cfg);
        if (!cfg.StopAfterLevelsEnabled || session.LevelsGained >= levelCap || !ClassSwitcher.LevelingJobAtMax(cfg))
        {
            return;
        }

        Diag($"Level limit: the job is at max level after {session.LevelsGained} of {levelCap} levels; ending the run");
        Svc.Chat.Print($"[AFG] Your job reached the level cap after {session.LevelsGained} of {levelCap} levels, so the run ends here.");
    }

    private void ReportItemGoalMet()
    {
        if (ItemGoalCatalog.Find(Plugin.Cfg.ActiveMode.Id) is not { } goal)
        {
            return;
        }

        var summary = ItemGoalProgress.Describe(goal, Plugin.Cfg);
        Diag($"Item goal {goal.DisplayName} at completion: {summary}");
        Svc.Chat.Print(ItemGoalProgress.IsAvailable(goal)
            ? $"[AFG] {goal.DisplayName}: every item is collected ({summary})."
            : $"[AFG] {goal.DisplayName}: the items no longer drop (quest turned in or relic unequipped); stopping with {summary}.");
    }
}
