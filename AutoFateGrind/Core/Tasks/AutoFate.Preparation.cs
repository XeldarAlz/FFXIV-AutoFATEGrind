using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Ipc;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private const uint MountRouletteActionId = 9;
    private const uint GysahlGreensItemId = 4868;
    private const int MountRetryMs = 5_000;
    private const int ChocoboRetryMs = 30_000;
    private long nextIdleMountAttemptMs;
    private long nextChocoboAttemptMs;

    private bool CanPrepareBetweenFates()
        => !CancelToken.IsCancellationRequested
        && Svc.Objects.LocalPlayer is { IsDead: false, IsCasting: false }
        && !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.Mounting]
        && !Svc.Condition[ConditionFlag.Mounting71]
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.BetweenAreas51]
        && !NavmeshIPC.Instance.IsRunning()
        && !NavmeshIPC.Instance.IsBusy();

    // One attempt per cooldown; the state machine keeps scanning even if mounting is unavailable.
    private unsafe void TryMountWhileWaiting()
    {
        if (!Plugin.Cfg.MountWhileWaitingForFates || !CanPrepareBetweenFates()
         || Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.RidingPillion]
         || Environment.TickCount64 < nextIdleMountAttemptMs)
            return;

        nextIdleMountAttemptMs = Environment.TickCount64 + MountRetryMs;
        var am = ActionManager.Instance();
        if (am is not null && am->GetActionStatus(ActionType.GeneralAction, MountRouletteActionId) == 0)
            am->UseAction(ActionType.GeneralAction, MountRouletteActionId);
    }

    // TimeLeft remains valid while mounted, when the companion's world object is hidden.
    private static unsafe bool ChocoboNeeded()
    {
        var ui = UIState.Instance();
        return ui is not null && ui->Buddy.CompanionInfo.TimeLeft <= 0
            && FoodOps.ItemCount(GysahlGreensItemId) > 0;
    }

    private static unsafe bool ChocoboSummoned()
    {
        var ui = UIState.Instance();
        return ui is not null && ui->Buddy.CompanionInfo.TimeLeft > 0;
    }

    private async Task EnsureChocobo()
    {
        if (!Plugin.Cfg.AutoSummonChocobo || !CanPrepareBetweenFates()
         || Svc.ClientState.TerritoryType != zone.TerritoryId
         || Environment.TickCount64 < nextChocoboAttemptMs || !ChocoboNeeded())
            return;

        // Back off before dismounting so a rejected summon cannot cause a mount/dismount loop.
        nextChocoboAttemptMs = Environment.TickCount64 + ChocoboRetryMs;
        if (!await SafeDismount("dismount-chocobo") || !CanPrepareBetweenFates()
         || !Plugin.Cfg.AutoSummonChocobo || !ChocoboNeeded())
            return;

        if (!TryUseGysahlGreens()) return;
        Status = "Summoning chocobo";
        Diag("Auto-summon: using Gysahl Greens");
        // Send only once: repeated uses can consume extra greens before the timer updates.
        await WaitUntilTimed(() => ChocoboSummoned() || Svc.Condition[ConditionFlag.InCombat],
            ConsumeItemWaitMs, "summon-chocobo", checkFrames: 2);
    }

    private static unsafe bool TryUseGysahlGreens()
    {
        var am = ActionManager.Instance();
        return am is not null && am->GetActionStatus(ActionType.Item, GysahlGreensItemId) == 0
            && am->UseAction(ActionType.Item, GysahlGreensItemId, extraParam: 65535);
    }
}
