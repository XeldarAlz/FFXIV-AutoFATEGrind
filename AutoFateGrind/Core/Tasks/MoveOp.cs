using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Game.Player;
using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Zones;
using clib.Extensions;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Network;
using System.Numerics;
using System.Threading.Tasks;
using AutoTask = clib.Services.AutoTask;
using PlayerHelpers = ECommons.GameHelpers.Player;

namespace AutoFateGrind.Core.Tasks;

internal readonly record struct FlightReplanPolicy(int GroundTravelGraceMs, float MinDistanceMeters, int MaxReplans);

// A single movement/teleport operation run as its OWN AutoTask, so it owns its own
// CancellationTokenSource. The parent grind loop can therefore Cancel() exactly one operation
// without tearing down the whole run: Cancel() fires the task's registered cleanups (the Navigate
// OnDispose that stops vnav) and cancels every await, so the operation unwinds instead of leaking.
// Abandoning a move instead (the old ObserveLeak path) left zombie flows that kept re-issuing
// teleports and stopping the next FATE's navigation. It builds on the plain AutoTask rather than the
// library's TaskBase, whose every instance installs two native movement hooks whose memory is never reused.
internal sealed partial class MoveOp(System.Func<MoveOp, Task> body) : AutoTask
{
    // clib's task runner awaits Execute with SuppressThrowing, so a clib ErrorIf (e.g. "Failed to start
    // pathfinding") would otherwise vanish and look like a clean completion. Capture it so the caller can
    // tell a genuine arrival from a faulted move and recover instead of treating the spot as reached.
    public System.Exception? Fault { get; private set; }

    protected override async Task Execute()
    {
        try { await body(this); }
        catch (System.OperationCanceledException) { /* cancelled by watchdog/Stop — expected */ }
        catch (System.Exception ex) { Fault = ex; }
    }

    // clib's territory-aware MoveTo rides the aethernet by itself, twice, from shifted shard positions and ignores
    // allowAethernet on the first ride (issue #75). AFG plans any hop itself (RideAethernet), so the walk never does.
    public async Task Move(uint territoryId, Vector3 dest, MovementConfig config, System.Func<bool>? stopCondition)
    {
        await TeleportToTerritory(territoryId, dest, allowSameZoneTeleport: false);
        await MountPreferred(dest, config);
        await Navigate(dest, config, stopCondition);
    }

    public async Task MoveInZone(Vector3 dest, MovementConfig config, System.Func<bool>? stopCondition)
    {
        await MountPreferred(dest, config);
        await Navigate(dest, config, stopCondition);
    }

    private const int PreferredMountWaitMs = 5_000;
    private const int PreferredMountRetryMs = 500;

    // Navigate only summons Flying Mount Roulette while unmounted, so riding the chosen mount first keeps
    // it; if the summon never lands the roulette still runs. Mirrors Navigate's own skip radius so a move it
    // would not make never mounts.
    private async Task MountPreferred(Vector3 dest, MovementConfig config)
    {
        if ((config.Movement & (MovementOptions.Mount | MovementOptions.Fly)) == 0
         || !MountOps.HasPreferred || !MountOps.TerritoryAllowsMount())
        {
            return;
        }
        if (Svc.Objects.LocalPlayer is not { } player
         || Vector3.Distance(player.Position, dest) < Math.Max(config.Tolerance ?? 0f, NavmeshIPC.Instance.GetTolerance()))
        {
            return;
        }

        var deadline = Environment.TickCount64 + PreferredMountWaitMs;
        var nextAttemptAtMs = 0L;
        while (!Svc.Condition[ConditionFlag.Mounted] && Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested || Svc.Condition[ConditionFlag.InCombat])
            {
                return;
            }
            if (Environment.TickCount64 >= nextAttemptAtMs && MountOps.TrySummonPreferred())
            {
                nextAttemptAtMs = Environment.TickCount64 + PreferredMountRetryMs;
            }
            await NextFrame();
        }
    }

    public async Task MoveInZoneWithFlightRecovery(Vector3 dest, MovementConfig config,
        System.Func<bool>? stopCondition, FlightReplanPolicy policy, System.Action<string> diag)
    {
        if (!config.Movement.HasFlag(MovementOptions.Fly) || config.Pathing == PathingStrategy.Direct)
        {
            await MoveInZone(dest, config, stopCondition);
            return;
        }

        var budgetExhaustionLogged = false;
        for (var replans = 0; ; replans++)
        {
            if (CancelToken.IsCancellationRequested || stopCondition?.Invoke() == true)
            {
                return;
            }

            var groundSinceMs = 0L;
            var replanForFlight = false;
            var callerStopped = false;
            bool? canFlyWhenPlanned = null;
            bool ShouldStop()
            {
                // Navigate has already chosen the route by its first stop callback. If flight was ready
                // then, a grounded take-off is not evidence that it chose a ground route.
                canFlyWhenPlanned ??= Control.CanFly;
                callerStopped |= CancelToken.IsCancellationRequested || stopCondition?.Invoke() == true;
                if (callerStopped || replanForFlight)
                {
                    return true;
                }

                if (canFlyWhenPlanned.Value || !ReadyForFlight()
                 || Svc.Condition[ConditionFlag.InFlight] || !NavmeshIPC.Instance.IsRunning()
                 || Svc.Objects.LocalPlayer is not { } player
                 || Vector3.Distance(player.Position, dest) <= Math.Max(policy.MinDistanceMeters, config.Tolerance ?? 0))
                {
                    groundSinceMs = 0;
                    return false;
                }

                var now = Environment.TickCount64;
                if (groundSinceMs == 0)
                {
                    groundSinceMs = now;
                }
                if (now - groundSinceMs < policy.GroundTravelGraceMs)
                {
                    return false;
                }

                if (replans >= policy.MaxReplans)
                {
                    if (!budgetExhaustionLogged)
                    {
                        diag("Flight replan budget spent; continuing on the ground route");
                        budgetExhaustionLogged = true;
                    }
                    return false;
                }

                replanForFlight = true;
                return true;
            }

            // Navigate decides whether to mount. Replan only when flight became available after it
            // planned a ground route and navigation stayed grounded past the grace period.
            await MoveInZone(dest, config, ShouldStop);
            if (callerStopped || !replanForFlight || CancelToken.IsCancellationRequested)
            {
                return;
            }
            diag($"Flight became available during a ground route; replanning ({replans + 1}/{policy.MaxReplans})");
        }
    }

    private static bool ReadyForFlight()
        => Svc.Condition[ConditionFlag.Mounted]
        && !Svc.Condition[ConditionFlag.Mounting]
        && !Svc.Condition[ConditionFlag.Mounting71]
        && Svc.Objects.LocalPlayer is { IsDead: false, IsCasting: false }
        && Control.CanFly;

    public Task Teleport(uint territoryId, Vector3 dest, bool allowSameZoneTeleport)
        => TeleportToTerritory(territoryId, dest, allowSameZoneTeleport);

    public async Task RideAethernet(AethernetHop hop)
    {
        var shard = CityAethernet.FindShardObject(hop.Source.Id);
        ErrorIf(shard is null, $"Aethernet shard {hop.Source.Id} is not loaded");

        await Navigate(shard!.Position, MovementConfig.Default.WithTolerance(InteractRange.Aetheryte), null);
        await Dismount();

        var menu = hop.Source.IsAetheryte ? AfgConstants.AddonNames.SelectString : AfgConstants.AddonNames.TelepotTown;
        await InteractWith(shard, () => NpcInteraction.AddonOpen(menu), UiSkipOptions.Talk);
        PacketDispatcher.TeleportToAethernet(hop.Source.Id, hop.Destination.Id);

        await WaitUntilThenFalse(() => Svc.Condition[ConditionFlag.BetweenAreas], "AethernetHop");
        await WaitUntil(() => PlayerHelpers.Interactable, "AethernetArrival");
    }

    public Task Interact(IGameObject obj, System.Func<bool>? waitUntil, UiSkipOptions skip)
        => InteractWith(obj, waitUntil, skip);

    public Task DismountNow() => Dismount();
}
