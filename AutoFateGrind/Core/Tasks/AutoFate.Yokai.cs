using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Zones;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private const int YokaiActionWaitMs = 3_000;
    private const int YokaiSummonWindowMs = 12_000;
    private const int YokaiSummonIdlePollFrames = 10;
    private const int YokaiSummonRetryFrames = 30;
    private const int YokaiParkRetryMs = 3_000;
    private const int YokaiParkWarnRepeatMs = 60_000;

    private long yokaiParkWarnedAtMs;

    private bool YokaiTargetChanged()
    {
        if (!ZoneSelection.GoalPlansZones(Plugin.Cfg))
        {
            return false;
        }

        var target = YokaiProgress.ResolveTargetMinionId(Plugin.Cfg, session.YokaiTargetMinionId);
        return target != 0 && target != session.YokaiTargetMinionId;
    }

    private void ReportYokaiGoalMet()
    {
        if (!ZoneSelection.GoalPlansZones(Plugin.Cfg))
        {
            return;
        }

        Diag($"Yo-kai roster at goal completion: {YokaiProgress.DescribeRoster(Plugin.Cfg)}");
        Svc.Chat.Print($"[AFG] Yo-kai goal met: {YokaiProgress.CompletionSummary(Plugin.Cfg)}. The roster is in /xllog.");
    }

    private async Task HandOffToNextYokai()
    {
        Status = "Switching to the next yo-kai";
        var nextIndex = YokaiProgress.ResolveTargetIndex(Plugin.Cfg, session.YokaiTargetMinionId);
        var nextName = nextIndex < 0 ? "none" : YokaiProgress.MinionName(nextIndex);
        Diag($"Yo-kai minion {session.YokaiTargetMinionId} no longer needs medals; handing off to plan the next one ({nextName})");
        await HoldForCollectReward();
        await ClearBlockingCombat();
        session.PendingYokaiAdvance = true;
    }

    // A FATE finished without the target's own minion out pays no Legendary Medal, so the grind parks instead of fighting blind.
    private bool YokaiMinionMissing()
    {
        if (!ZoneSelection.GoalPlansZones(Plugin.Cfg))
        {
            return false;
        }

        var minionId = session.YokaiTargetMinionId;
        if (minionId == 0 || Svc.Condition[ConditionFlag.InCombat] || IsPlayerKO())
        {
            return false;
        }

        return YokaiOps.SummonedMinionId() != minionId;
    }

    private async Task TickYokaiMinionWait()
    {
        var minionName = YokaiTargetName();
        Status = $"Waiting to summon {minionName}";
        await EnsureYokaiCompanion();
        if (CancelToken.IsCancellationRequested || !YokaiMinionMissing())
        {
            yokaiParkWarnedAtMs = 0;
            return;
        }

        WarnYokaiParked(minionName);
        await DelayMs(YokaiParkRetryMs);
    }

    private void WarnYokaiParked(string minionName)
    {
        var now = Environment.TickCount64;
        if (yokaiParkWarnedAtMs != 0 && now - yokaiParkWarnedAtMs < YokaiParkWarnRepeatMs)
        {
            return;
        }

        yokaiParkWarnedAtMs = now;
        Warn($"Parked in {zone.Name}: {minionName} is not summoned and its Legendary Medals need it out ({ConditionTag()})");
        Svc.Chat.PrintError($"[AFG] Waiting in {zone.Name}: cannot summon {minionName}, and its Legendary Medals only drop while it is out. If it is raining, put your umbrella away or turn off auto-umbrella under Character Configuration > Fashion Accessories.");
    }

    private string YokaiTargetName() => YokaiMinionName(session.YokaiTargetMinionId);

    private static string YokaiMinionName(uint minionId)
    {
        var index = YokaiCatalog.IndexOfMinion(minionId);
        return index < 0 ? minionId.ToString() : YokaiProgress.MinionName(index);
    }

    // Legendary medals only drop with the target's own minion out; the watch is equipped as well so plain medals keep coming.
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
            Warn("Yo-kai Watch is not equipped and was not found in the armoury chest or inventory; plain Yo-kai Medals will not drop");
            return;
        }

        if (!await WaitUntilTimed(YokaiOps.IsWatchEquipped, YokaiActionWaitMs, "yokai-equip-watch"))
        {
            Warn("Yo-kai Watch did not equip; retrying before the next FATE");
        }
    }

    private async Task SummonYokaiMinion(uint minionId)
    {
        var minionName = YokaiMinionName(minionId);
        Status = $"Summoning {minionName}";
        await SafeDismount("dismount-yokai-summon");
        if (Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.InCombat])
        {
            return;
        }

        await StowFashionAccessory();

        var deadline = Environment.TickCount64 + YokaiSummonWindowMs;
        var attempts = 0;
        while (Environment.TickCount64 < deadline)
        {
            // Using the action on a minion that is already out dismisses it, so every use is gated on a fresh read.
            if (CancelToken.IsCancellationRequested || YokaiOps.SummonedMinionId() == minionId)
            {
                return;
            }

            // Firing into the character's own cast or animation lock cancels the summon that is being waited on.
            if (!YokaiOps.CanIssueSummon())
            {
                await NextFrame(YokaiSummonIdlePollFrames);
                continue;
            }

            attempts++;
            if (YokaiOps.TrySummon(minionId)
             && await WaitUntilTimed(() => YokaiOps.SummonedMinionId() == minionId, YokaiActionWaitMs, "yokai-summon"))
            {
                Diag($"Summoned {minionName} (attempt {attempts})");
                return;
            }

            await NextFrame(YokaiSummonRetryFrames);
        }

        if (YokaiOps.SummonedMinionId() == minionId)
        {
            return;
        }

        Warn($"Could not summon {minionName} within {YokaiSummonWindowMs / 1000}s ({attempts} attempts, {ConditionTag()})");
    }

    // A deployed fashion accessory blocks minion summoning, so it is put away first rather than reported.
    private async Task StowFashionAccessory()
    {
        if (!YokaiOps.IsFashionAccessoryDeployed())
        {
            return;
        }

        Status = "Putting the fashion accessory away";
        Diag("A fashion accessory is deployed, which blocks minion summoning; withdrawing it");
        if (!YokaiOps.TryWithdrawFashionAccessory())
        {
            return;
        }

        if (!await WaitUntilTimed(() => !YokaiOps.IsFashionAccessoryDeployed(), YokaiActionWaitMs, "yokai-stow-accessory"))
        {
            Warn("The fashion accessory did not go away; the summon is attempted anyway");
        }
    }
}
