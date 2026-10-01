using AutoFateGrind.Core.Zones;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    internal const int StuckRescueMinMinutes = 2;
    internal const int StuckRescueMaxMinutes = 30;
    // Wide enough that landing retries and re-planned paths circling the same spot never read as progress.
    private const float StuckRescueLeashMeters = 15f;
    // Rescues in a row with no FATE finished between them; past this the spot is not the cause, so the other guards take over.
    private const int MaxStuckRescuesWithoutFate = 3;
    // The no-progress fault waits this long past a due rescue, so the teleport always gets the first try.
    private const int StuckRescueFaultGraceMs = 60_000;
    private const string StuckRescueScope = "stuck-rescue";

    private long stuckWatchSinceMs;
    private Vector3 stuckWatchAnchor;
    private uint stuckWatchTerritory;
    private int stuckWatchCompleted = -1;
    private int stuckRescueCompleted = -1;
    private int stuckRescues;

    private static int StuckRescueDelayMs(Configuration cfg)
        => Math.Clamp(cfg.StuckRescueMinutes, StuckRescueMinMinutes, StuckRescueMaxMinutes) * 60_000;

    private static int NoProgressFaultMsFor(Configuration cfg)
        => cfg.StuckRescueEnabled
            ? Math.Max(NoProgressFaultMs, StuckRescueDelayMs(cfg) + StuckRescueFaultGraceMs)
            : NoProgressFaultMs;

    // Only the states where AFG should be acting in its zone. The waits that are still on purpose (no FATE up, a Collect
    // reward, a follow-up, a settle, a KO) and the states that change zone each end on their own.
    private static bool StuckWatchApplies(GrindState state)
        => state is GrindState.BetweenFates or GrindState.Engaging or GrindState.WaitingForYokaiMinion;

    // Stuck is read from the outcome, not from any one error: no FATE finished, no zone change, no combat and no real
    // displacement for the whole window. That catches a yo-kai summon parked over water (issue #87) as well as any retry
    // loop that keeps running without getting the character anywhere.
    private async Task<bool> RescueIfStuck(GrindState state)
    {
        var cfg = Plugin.Cfg;
        if (stuckRescueCompleted != session.CompletedCount)
        {
            stuckRescueCompleted = session.CompletedCount;
            stuckRescues = 0;
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
        if (!cfg.StuckRescueEnabled || !StuckWatchApplies(state) || progressed)
        {
            RestartStuckWatch(position, territory);
            return false;
        }

        var stuckMs = Environment.TickCount64 - stuckWatchSinceMs;
        if (stuckMs < StuckRescueDelayMs(cfg))
        {
            return false;
        }

        if (stuckRescues >= MaxStuckRescuesWithoutFate)
        {
            Warn($"Still stuck in state {state} after {stuckRescues} rescue teleports with no FATE finished; not teleporting again until one does ({ConditionTag()})");
            RestartStuckWatch(position, territory);
            return false;
        }

        await TeleportOutOfStuck(state, stuckMs, position, territory);
        if (Svc.Objects.LocalPlayer is { } landed)
        {
            RestartStuckWatch(landed.Position, Svc.ClientState.TerritoryType);
        }
        return true;
    }

    private async Task TeleportOutOfStuck(GrindState state, long stuckMs, Vector3 from, uint territory)
    {
        var where = $"({from.X:F0},{from.Y:F0},{from.Z:F0})";
        if (!ZoneAetherytes.TryFindNearest(territory, from, out var aetheryte))
        {
            stuckRescues = MaxStuckRescuesWithoutFate;
            Warn($"Stuck {stuckMs / 1000}s in state {state} at {where} ({ConditionTag()}), but territory {territory} has no aetheryte of its own to teleport to");
            return;
        }

        stuckRescues++;
        Status = $"Stuck; teleporting to {aetheryte.Name}";
        Warn($"Stuck rescue {stuckRescues}/{MaxStuckRescuesWithoutFate}: no FATE finished, no zone change, no combat and nothing beyond {StuckRescueLeashMeters:F0}m "
           + $"for {stuckMs / 1000}s in state {state} at {where} ({ConditionTag()}); teleporting to {aetheryte.Name}");
        Svc.Chat.Print($"[AFG] Stuck for {stuckMs / 60_000} min; teleporting to {aetheryte.Name}.");

        await PrepareForTeleport(StuckRescueScope);
        if (CancelToken.IsCancellationRequested)
        {
            return;
        }

        var outcome = await RunTeleport(territory, aetheryte.Position, allowSameZoneTeleport: true, TeleportWatchdogMs, StuckRescueScope);
        var after = Svc.Objects.LocalPlayer?.Position;
        var moved = after is { } landed ? Vector3.Distance(from, landed) : 0f;
        if (outcome.Succeeded && moved >= TeleportRetryProgressMeters)
        {
            Diag($"Stuck rescue reached {aetheryte.Name}, {moved:F0}m from where the character was held");
            return;
        }

        var reason = outcome.Fault?.Message ?? (outcome.Completed ? $"moved only {moved:F1}m" : "timed out");
        Warn($"Stuck rescue teleport to {aetheryte.Name} did not go through ({reason}, {ConditionTag()})");
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

        return $" stuckWatch={(Environment.TickCount64 - stuckWatchSinceMs) / 1000}s rescues={stuckRescues}";
    }
}
