using AutoFateGrind.Core.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Game.Watchers;

internal sealed class DutyWatcher : IDisposable
{
    private const int ResumeSettleMs = 5_000;

    private long leftContentAtMs;

    public DutyWatcher()
    {
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework _)
    {
        var controller = Plugin.Instance.Controller;

        if (InContent())
        {
            leftContentAtMs = 0;
            if (Plugin.Cfg.AutoPauseInContent) controller.Pause(PauseReason.InContent);
            return;
        }

        if (controller.PauseReason != PauseReason.InContent) return;

        if (Svc.Objects.LocalPlayer is null
         || Svc.Condition[ConditionFlag.BetweenAreas]
         || Svc.Condition[ConditionFlag.BetweenAreas51])
        {
            leftContentAtMs = 0;
            return;
        }

        if (leftContentAtMs == 0)
        {
            leftContentAtMs = Environment.TickCount64;
            return;
        }
        if (Environment.TickCount64 - leftContentAtMs < ResumeSettleMs) return;

        leftContentAtMs = 0;
        controller.Resume();
    }

    private static bool InContent()
        => Svc.Condition[ConditionFlag.BoundByDuty]
        || Svc.Condition[ConditionFlag.BoundByDuty56]
        || Svc.Condition[ConditionFlag.BoundByDuty95];
}
