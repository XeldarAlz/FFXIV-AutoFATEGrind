using AutoFateGrind.Core.Zones;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    // Wide enough that landing retries and re-planned paths circling the same spot never read as progress.
    private const float StuckRescueLeashMeters = 15f;
    // Rescues with no FATE finished between them; past this the spot is not the cause, so the run faults instead.
    private const int MaxStuckRescuesWithoutFate = 3;
    // The BetweenFates no-progress fault waits this long past a due rescue, so the teleport always gets the first try.
    private const int StuckRescueFaultGraceMs = 60_000;
    private const string StuckRescueScope = "stuck-rescue";

    private long stuckWatchSinceMs;
    private Vector3 stuckWatchAnchor;
    private uint stuckWatchTerritory;
    private int stuckWatchCompleted = -1;
    // Set when the rescue gives up; GuardForwardProgress raises it outside the loop's catch.
    private string? stuckRescueFault;

    private static int StuckRescueDelayMs(Configuration cfg)
        => Math.Clamp(cfg.StuckRescueMinutes, Configuration.StuckRescueMinMinutes, Configuration.StuckRescueMaxMinutes) * 60_000;

    // The longest no-progress fault delay in force; the fault-resume window is sized from it.
    internal static int NoProgressFaultMsFor(Configuration cfg)
        => cfg.StuckRescueEnabled
            ? Math.Max(NoProgressFaultMs, StuckRescueDelayMs(cfg) + StuckRescueFaultGraceMs)
            : NoProgressFaultMs;

    // BetweenFates and the yo-kai summon wait are where AFG should be acting in its zone. Engaging is left to the
    // engagement watchdog: a quiet wait inside a FATE ring (an NPC-start FATE nobody has triggered) is legitimate.
    private static bool StuckWatchApplies(GrindState state)
        => state is GrindState.BetweenFates or GrindState.WaitingForYokaiMinion;

    // Stuck is read from the outcome, not from any one error: no FATE finished, no zone change, no combat and no real
    // displacement for the whole window. That catches a yo-kai summon parked over water (issue #87) as well as any retry
    // loop that keeps running without getting the character anywhere.
    private async Task<bool> RescueIfStuck(GrindState state)
    {
        var cfg = Plugin.Cfg;
        if (session.StuckRescueCompletedMark != session.CompletedCount)
        {
            session.StuckRescueCompletedMark = session.CompletedCount;
            session.StuckRescuesWithoutFate = 0;
        }

        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        var position = player.Position;
        var territory = Svc.ClientState.TerritoryType;
        var progressed = stuckWatchCompleted != session.CompletedCount
                      || territory != stuckWatchTerritory
                      || Svc.Condition[ConditionFlag.InCombat]
                      || Vector3.Distance(position, stuckWatchAnchor) > StuckRescueLeashMeters;
        if (!cfg.StuckRescueEnabled || !StuckWatchApplies(state) || territory != zone.TerritoryId || progressed)
        {
            RestartStuckWatch(position, territory);
            return false;
        }

        var stuckMs = Environment.TickCount64 - stuckWatchSinceMs;
        if (stuckMs < StuckRescueDelayMs(cfg))
        {
            return false;
        }

        var where = $"({position.X:F0},{position.Y:F0},{position.Z:F0})";
        if (session.StuckRescuesWithoutFate >= MaxStuckRescuesWithoutFate)
        {
            GiveUpStuckRescue($"Still stuck in state {state} at {where} after {session.StuckRescuesWithoutFate} rescue teleports with no FATE finished ({ConditionTag()})");
            return true;
        }

        if (!ZoneAetherytes.TryFindNearest(zone.TerritoryId, position, out var aetheryte))
        {
            GiveUpStuckRescue($"Stuck {stuckMs / 1000}s in state {state} at {where} ({ConditionTag()}), but {zone.Name} has no aetheryte of its own to teleport to");
            return true;
        }

        RefreshPendingCollectReward();
        if (CollectRewardPending)
        {
            Diag($"Stuck rescue deferred: a same-zone teleport reloads the zone while Collect FATE {pendingRewardSpawn.FateId} ({pendingRewardName}) still owes its reward; checking again in {StuckRescueDelayMs(cfg) / 60_000}m");
            RestartStuckWatch(position, territory);
            return false;
        }

        session.StuckRescuesWithoutFate++;
        Status = $"Stuck; teleporting to {aetheryte.Name}";
        Warn($"Stuck rescue {session.StuckRescuesWithoutFate}/{MaxStuckRescuesWithoutFate}: no FATE finished, no zone change, no combat and nothing beyond {StuckRescueLeashMeters:F0}m "
           + $"for {stuckMs / 1000}s in state {state} at {where} ({ConditionTag()}); teleporting to {aetheryte.Name}");
        Svc.Chat.Print($"[AFG] Stuck for {stuckMs / 60_000} min; teleporting to {aetheryte.Name}.");

        if (await TeleportToZoneAetheryte(aetheryte, StuckRescueScope))
        {
            Diag($"Stuck rescue reached {aetheryte.Name}");
        }
        else if (!CancelToken.IsCancellationRequested)
        {
            Warn($"Stuck rescue teleport to {aetheryte.Name} did not go through ({ConditionTag()}); trying again after another window");
        }

        if (Svc.Objects.LocalPlayer is { } landed)
        {
            RestartStuckWatch(landed.Position, Svc.ClientState.TerritoryType);
        }
        return true;
    }

    private void GiveUpStuckRescue(string reason)
    {
        Warn($"{reason}; giving up on the stuck rescue and surfacing a fault for recovery");
        stuckRescueFault = $"{reason}; surfacing fault for recovery.";
    }

    private void RestartStuckWatch(Vector3 position, uint territory)
    {
        stuckWatchSinceMs = Environment.TickCount64;
        stuckWatchAnchor = position;
        stuckWatchTerritory = territory;
        stuckWatchCompleted = session.CompletedCount;
    }

    private string StuckWatchHeartbeat()
    {
        if (!Plugin.Cfg.StuckRescueEnabled || !StuckWatchApplies(lastObservedState))
        {
            return "";
        }

        return $" stuckWatch={(Environment.TickCount64 - stuckWatchSinceMs) / 1000}s rescues={session.StuckRescuesWithoutFate}";
    }
}
