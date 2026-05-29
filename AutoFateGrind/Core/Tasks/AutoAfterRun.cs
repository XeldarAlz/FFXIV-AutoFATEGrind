using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed class AutoAfterRun(AfterRunAction action) : AutoCommon
{
    private readonly AfterRunAction action = action;

    private const int ReadyWaitMs = 20_000;
    private const int YesnoWaitMs = 6_000;

    protected override async Task Execute()
    {
        // Don't issue the command mid-combat / mid-transition; wait for a clean grounded state first.
        await WaitUntilTimed(IsSafeToFinish, ReadyWaitMs, "afterrun-ready");
        if (CancelToken.IsCancellationRequested) return;

        switch (action)
        {
            case AfterRunAction.Logout:
                Status = "Logging out";
                Diag("After-run: logging out.");
                await NextFrame(800);
                Chat.ExecuteCommand("/logout");
                if (await WaitUntilTimed(SelectYesnoOpen, YesnoWaitMs, "logout-yesno"))
                    ClickYes();
                break;

            case AfterRunAction.CloseGame:
                Status = "Closing the game";
                Diag("After-run: closing the game (/xlkill).");
                await NextFrame(800);
                Chat.ExecuteCommand("/xlkill");
                break;
        }
    }

    private static bool IsSafeToFinish()
        => Svc.Objects.LocalPlayer is not null
        && !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.Casting];

    private static unsafe bool SelectYesnoOpen()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectYesno", out var a) && GenericHelpers.IsAddonReady(a);

    private static unsafe void ClickYes()
    {
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectYesno", out var a) && GenericHelpers.IsAddonReady(a))
            new AddonMaster.SelectYesno((nint)a).Yes();
    }
}
