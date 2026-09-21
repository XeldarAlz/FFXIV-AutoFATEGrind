using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Zones;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private const int YokaiSummonAttempts = 3;
    private const int YokaiActionWaitMs = 3_000;

    private bool YokaiTargetChanged()
    {
        if (!ZoneSelection.GoalPlansZones(Plugin.Cfg))
        {
            return false;
        }

        var target = YokaiProgress.ResolveTargetMinionId(Plugin.Cfg, session.YokaiTargetMinionId);
        return target != 0 && target != session.YokaiTargetMinionId;
    }

    private async Task HandOffToNextYokai()
    {
        Status = "Switching to the next yo-kai";
        Diag($"Yo-kai minion {session.YokaiTargetMinionId} no longer needs medals; handing off to plan the next one");
        await HoldForCollectReward();
        await ClearBlockingCombat();
        session.PendingYokaiAdvance = true;
    }

    // Legendary medals only drop with the watch on and the target's own minion out.
    private async Task EnsureYokaiCompanion()
    {
        if (!ZoneSelection.GoalPlansZones(Plugin.Cfg))
        {
            return;
        }

        var minionId = session.YokaiTargetMinionId;
        if (minionId == 0 || Svc.Condition[ConditionFlag.InCombat] || IsPlayerKO())
        {
            return;
        }

        if (!YokaiOps.IsWatchEquipped())
        {
            await EquipYokaiWatch();
        }

        if (YokaiOps.SummonedMinionId() != minionId)
        {
            await SummonYokaiMinion(minionId);
        }
    }

    private async Task EquipYokaiWatch()
    {
        Status = "Equipping the Yo-kai Watch";
        if (!YokaiOps.TryEquipWatch())
        {
            Warn("Yo-kai Watch is not equipped and was not found in the armoury chest or inventory; no Legendary Medals will drop");
            return;
        }

        if (!await WaitUntilTimed(YokaiOps.IsWatchEquipped, YokaiActionWaitMs, "yokai-equip-watch"))
        {
            Warn("Yo-kai Watch did not equip; retrying before the next FATE");
        }
    }

    private async Task SummonYokaiMinion(uint minionId)
    {
        Status = "Summoning the yo-kai minion";
        await SafeDismount("dismount-yokai-summon");
        if (Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.InCombat])
        {
            return;
        }

        for (var attempt = 1; attempt <= YokaiSummonAttempts; attempt++)
        {
            // Using the action on a minion that is already out dismisses it, so every use is gated on a fresh read.
            if (CancelToken.IsCancellationRequested || YokaiOps.SummonedMinionId() == minionId)
            {
                return;
            }

            if (YokaiOps.TrySummon(minionId)
             && await WaitUntilTimed(() => YokaiOps.SummonedMinionId() == minionId, YokaiActionWaitMs, "yokai-summon"))
            {
                Diag($"Summoned yo-kai minion {minionId} (attempt {attempt})");
                return;
            }

            await DelayMs(YokaiActionWaitMs / YokaiSummonAttempts);
        }

        if (YokaiOps.SummonedMinionId() == minionId)
        {
            return;
        }

        Warn($"Could not summon yo-kai minion {minionId} in {YokaiSummonAttempts} attempts; retrying before the next FATE");
    }
}
