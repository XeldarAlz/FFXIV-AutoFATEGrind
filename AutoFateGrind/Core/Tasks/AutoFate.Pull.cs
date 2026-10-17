using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Ipc;
using clib.Utils;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace AutoFateGrind.Core.Tasks;

// BossMod's AutoTarget ranks every FATE mob alike and takes the nearest, so a tank fights whatever walks up and only
// tags the next mob once the last one is dead. While pulling, AFG picks the targets: BossMod keeps the rotation and
// movement but stops retargeting, and AFG throws the job's ranged attack at each idle mob in reach until the pack is
// on the character, then hands targeting back so the rotation AoEs the pack down.
public sealed partial class AutoFate
{
    private const string AutoTargetRetargetTrack = "Retarget";
    private const string AutoTargetRetargetNeverOption = "Never";
    private const string RotationTargetingTrack = "Targeting";
    private const string RotationTargetingManualOption = "Manual";

    private const int   PullTagTimeoutMs = 8_000;
    private const int   PullSkipMs = 30_000;
    // A pick that times out this often is out of reach (behind a wall, on a ledge); it stays skipped until the FATE ends.
    private const int   PullMaxTimeoutsPerTarget = 2;
    private const int   PullScanIntervalMs = 1_000;
    private const int   PullActionRetryMs = 500;
    private const int   PullSetupRetryMs = 60_000;

    // Ranged GCDs that tag a mob from 20y, keyed by ClassJob row: GLA/PLD Shield Lob, MRD/WAR Tomahawk, DRK Unmend, GNB Lightning Shot.
    private static readonly Dictionary<uint, uint> TankPullActions = new()
    {
        [1] = 24, [19] = 24,
        [3] = 46, [21] = 46,
        [32] = 3624,
        [37] = 16143,
    };

    // xan rotations pick their own best target unless told to hit the player's; WAR runs Veyn's module, which already does.
    private static readonly Dictionary<uint, string> TankRotationModules = new()
    {
        [1] = "BossMod.Autorotation.xan.PLD", [19] = "BossMod.Autorotation.xan.PLD",
        [32] = "BossMod.Autorotation.xan.DRK",
        [37] = "BossMod.Autorotation.xan.GNB",
    };

    private static readonly string[] TankRotationModuleNames =
    [
        "BossMod.Autorotation.xan.PLD",
        "BossMod.Autorotation.xan.DRK",
        "BossMod.Autorotation.xan.GNB",
    ];

    private readonly Dictionary<ulong, long> pullSkippedUntilMs = [];
    private readonly Dictionary<ulong, int> pullTimeouts = [];
    private readonly List<ulong> pullExpiredSkips = [];
    private bool    pullOverridesApplied;
    private bool    pullActive;
    private bool    pullHpLow;
    private string? pullRotationModule;
    private ulong   pullTargetId;
    private long    pullTargetSinceMs;
    private long    pullNextScanMs;
    private long    pullNextActionMs;
    private int     pullTagged;
    private bool    pullSetupFailureLogged;
    private ulong   pullStatusTargetId;
    private int     pullStatusOnPlayer = -1;
    private int     pullStatusCandidates = -1;

    private static bool PullAllowed()
        => Plugin.Cfg.PullAsManyAsPossible
        && Svc.Objects.LocalPlayer is { } player
        && player.ClassJob.Value.Role == RoleTank
        && !Svc.Condition[ConditionFlag.Mounted]
        && BossModIPC.Instance.CanClearTransientStrategy;

    // True while AFG owns targeting; the engage loop leaves the ring chase and the idle watchdog alone meanwhile.
    private bool TickPull(uint fateId, PublicEvent fate, string preset)
    {
        if (!PullAllowed() || Svc.Objects.LocalPlayer is not { } player)
        {
            EndPull(preset, fateId, null);
            return false;
        }

        var cfg = Plugin.Cfg;
        var now = Environment.TickCount64;
        var hpPercent = player.MaxHp == 0 ? 100 : (int)(100L * player.CurrentHp / player.MaxHp);
        if (pullHpLow && hpPercent >= Math.Max(cfg.PullResumeHpPercent, cfg.PullStopHpPercent))
        {
            pullHpLow = false;
        }
        else if (!pullHpLow && hpPercent < cfg.PullStopHpPercent)
        {
            pullHpLow = true;
            if (pullActive)
            {
                EndPull(preset, fateId, $"HP down to {hpPercent}%; fighting the pack before pulling more");
            }
        }
        if (pullHpLow)
        {
            return false;
        }

        if (!pullActive && now < pullNextScanMs)
        {
            return false;
        }

        if (pullActive && pullTargetId != 0 && TickPullTarget(fateId, player, now))
        {
            return true;
        }

        PruneSkippedPullTargets(now);
        var survey = FateMobScanner.SurveyPull(fateId, player.Position, fate.Position, fate.Radius, cfg.PullReachMeters, pullSkippedUntilMs);
        if (survey.OnPlayer >= cfg.PullMaxMobs || !survey.HasCandidate)
        {
            if (pullActive)
            {
                EndPull(preset, fateId, survey.HasCandidate
                    ? $"{survey.OnPlayer} mobs on the character"
                    : $"no idle mob left within {cfg.PullReachMeters}m");
            }
            pullNextScanMs = now + PullScanIntervalMs;
            return false;
        }

        if (!pullActive && !BeginPull(fateId, preset))
        {
            pullNextScanMs = now + PullSetupRetryMs;
            return false;
        }

        pullTargetId = survey.Next.GameObjectId;
        pullTargetSinceMs = now;
        if (FateMobScanner.TryGetPullTarget(fateId, pullTargetId, out var next))
        {
            Svc.Targets.Target = next;
            FirePullAction(player, next, now);
        }
        UpdatePullStatus(fate, survey);
        return true;
    }

    private void UpdatePullStatus(PublicEvent fate, in FatePullSurvey survey)
    {
        if (survey.Next.GameObjectId == pullStatusTargetId
         && survey.OnPlayer == pullStatusOnPlayer
         && survey.Candidates == pullStatusCandidates)
        {
            return;
        }
        pullStatusTargetId = survey.Next.GameObjectId;
        pullStatusOnPlayer = survey.OnPlayer;
        pullStatusCandidates = survey.Candidates;
        Status = $"Pulling {fate.Name} ({survey.OnPlayer} on you, {survey.Candidates} more in reach)";
    }

    // True while the current pick still needs tagging; false once it is on the character or out of the picture.
    private bool TickPullTarget(uint fateId, IPlayerCharacter player, long now)
    {
        switch (FateMobScanner.PullTagState(fateId, pullTargetId))
        {
            case FatePullTagState.OnPlayer:
                pullTagged++;
                pullTargetId = 0;
                return false;
            case FatePullTagState.Gone:
                pullTargetId = 0;
                return false;
        }

        if (now - pullTargetSinceMs > PullTagTimeoutMs)
        {
            var timeouts = pullTimeouts.GetValueOrDefault(pullTargetId) + 1;
            pullTimeouts[pullTargetId] = timeouts;
            if (timeouts >= PullMaxTimeoutsPerTarget)
            {
                Diag($"Pull target {pullTargetId:X} of FATE {fateId} still untagged after {PullTagTimeoutMs / 1000}s again; skipping it for the rest of the FATE");
                pullSkippedUntilMs[pullTargetId] = long.MaxValue;
            }
            else
            {
                Diag($"Pull target {pullTargetId:X} of FATE {fateId} still untagged after {PullTagTimeoutMs / 1000}s; skipping it for {PullSkipMs / 1000}s");
                pullSkippedUntilMs[pullTargetId] = now + PullSkipMs;
            }
            pullTargetId = 0;
            return false;
        }

        if (FateMobScanner.TryGetPullTarget(fateId, pullTargetId, out var target))
        {
            if (Svc.Targets.Target?.GameObjectId != pullTargetId)
            {
                Svc.Targets.Target = target;
            }
            FirePullAction(player, target, now);
        }
        return true;
    }

    private unsafe void FirePullAction(IPlayerCharacter player, IGameObject target, long now)
    {
        if (now < pullNextActionMs || !TankPullActions.TryGetValue(player.ClassJob.RowId, out var actionId))
        {
            return;
        }
        var actions = ActionManager.Instance();
        if (actions is null)
        {
            return;
        }
        if (ActionManager.GetActionInRangeOrLoS(actionId, (CSGameObject*)player.Address, (CSGameObject*)target.Address) != 0)
        {
            return;
        }
        if (actions->GetActionStatus(ActionType.Action, actionId, target.GameObjectId) != 0)
        {
            return;
        }
        pullNextActionMs = now + PullActionRetryMs;
        actions->UseAction(ActionType.Action, actionId, target.GameObjectId);
    }

    private bool BeginPull(uint fateId, string preset)
    {
        var ipc = BossModIPC.Instance;
        if (!ipc.AddTransientStrategy(preset, AutoTargetModule, AutoTargetRetargetTrack, AutoTargetRetargetNeverOption))
        {
            if (!pullSetupFailureLogged)
            {
                pullSetupFailureLogged = true;
                Diag($"Pulling more mobs is on, but BossMod would not take a Retarget override on '{preset}' (no AutoTarget module in the preset?); leaving targeting to BossMod");
            }
            return false;
        }

        pullRotationModule = null;
        if (Svc.Objects.LocalPlayer is { } player
         && TankRotationModules.TryGetValue(player.ClassJob.RowId, out var module)
         && ipc.AddTransientStrategy(preset, module, RotationTargetingTrack, RotationTargetingManualOption))
        {
            pullRotationModule = module;
        }

        pullOverridesApplied = true;
        pullActive = true;
        pullTagged = 0;
        pullTargetId = 0;
        Diag($"Pulling more mobs in FATE {fateId}: AFG picks targets, BossMod keeps the rotation (rotation targeting override: {pullRotationModule ?? "none"})");
        return true;
    }

    // A null reason ends quietly: the FATE is over, or pulling is off or not for this job.
    private void EndPull(string preset, uint fateId, string? reason)
    {
        if (!pullActive)
        {
            return;
        }
        pullActive = false;

        var ipc = BossModIPC.Instance;
        ipc.ClearTransientStrategy(preset, AutoTargetModule, AutoTargetRetargetTrack);
        if (pullRotationModule is not null)
        {
            ipc.ClearTransientStrategy(preset, pullRotationModule, RotationTargetingTrack);
            pullRotationModule = null;
        }

        // A pick nobody has tagged yet would hold BossMod's attention away from the pack.
        if (pullTargetId != 0)
        {
            FateMobScanner.DropUnpulledTarget(fateId);
            pullTargetId = 0;
        }

        pullNextScanMs = Environment.TickCount64 + PullScanIntervalMs;
        pullStatusTargetId = 0;
        pullStatusOnPlayer = -1;
        pullStatusCandidates = -1;
        if (reason is not null)
        {
            Diag($"Pull in FATE {fateId} done after tagging {pullTagged}: {reason}; BossMod retargets and fights the pack");
            if (PublicEvent.GetFateById(fateId) is { } fate)
            {
                Status = $"Engaging {fate.Name}";
            }
        }
    }

    private void PruneSkippedPullTargets(long now)
    {
        if (pullSkippedUntilMs.Count == 0)
        {
            return;
        }
        pullExpiredSkips.Clear();
        foreach (var (id, until) in pullSkippedUntilMs)
        {
            if (until <= now)
            {
                pullExpiredSkips.Add(id);
            }
        }
        for (var index = 0; index < pullExpiredSkips.Count; index++)
        {
            pullSkippedUntilMs.Remove(pullExpiredSkips[index]);
        }
    }

    // Overrides live on BossMod's in-memory preset and outlast AFG, so an engage starts from a clean slate. Only a user
    // with the toggle on, or one who just turned it off after a pull, can have any to clear.
    private void ResetPullOverrides(string preset)
    {
        pullActive = false;
        pullHpLow = false;
        pullRotationModule = null;
        pullTargetId = 0;
        pullNextScanMs = 0;
        pullNextActionMs = 0;
        pullTagged = 0;
        pullStatusTargetId = 0;
        pullStatusOnPlayer = -1;
        pullStatusCandidates = -1;
        pullSkippedUntilMs.Clear();
        pullTimeouts.Clear();

        var ipc = BossModIPC.Instance;
        if (!(Plugin.Cfg.PullAsManyAsPossible || pullOverridesApplied) || !ipc.CanClearTransientStrategy)
        {
            return;
        }
        pullOverridesApplied = false;
        ipc.ClearTransientStrategy(preset, AutoTargetModule, AutoTargetRetargetTrack);
        foreach (var module in TankRotationModuleNames)
        {
            ipc.ClearTransientStrategy(preset, module, RotationTargetingTrack);
        }
    }
}
