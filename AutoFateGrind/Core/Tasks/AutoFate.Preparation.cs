using AutoFateGrind.Core.Ipc;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private const uint MountRouletteActionId = 9;
    private const int MountRetryMs = 5_000;
    private long nextIdleMountAttemptMs;

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

}
