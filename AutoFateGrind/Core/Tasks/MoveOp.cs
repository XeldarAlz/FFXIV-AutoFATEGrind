using AutoFateGrind.Core.Ipc;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// A single clib movement/teleport operation run as its OWN AutoTask, so it owns its own
// CancellationTokenSource. The parent grind loop can therefore Cancel() exactly one operation
// without tearing down the whole run — clib's Cancel() fires the task's registered cleanups
// (OverrideMovement off, the MoveTo OnDispose(Svc.Navmesh.Stop)) and cancels every await, so the
// operation unwinds instead of leaking. clib's MoveTo/TeleportTo expose no per-call cancellation of
// their own, which is why abandoning them (the old ObserveLeak path) left zombie flows that kept
// re-issuing teleports and stopping the next FATE's navigation.
internal sealed class MoveOp(System.Func<MoveOp, Task> body) : TaskBase
{
    private const int FlightReadyWaitMs = 2_000;
    private const int GroundTravelGraceMs = 3_000;
    private const float FlightRepathMinDistance = 40f;
    private const int MaxFlightRepaths = 2;

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

    public Task Move(uint territoryId, Vector3 dest, MovementConfig config, bool allowTeleportIfFaster,
                     System.Func<bool>? stopCondition, bool allowAethernetWithinTerritory)
        => MoveTo(territoryId, dest, config, allowTeleportIfFaster, stopCondition, null, allowAethernetWithinTerritory);

    public Task MoveInZone(Vector3 dest, MovementConfig config, System.Func<bool>? stopCondition)
        => MoveTo(dest, config, allowTeleportIfFaster: false, stopCondition, null, allowAethernet: false);

    // clib samples CanFly only once, immediately after Mount() sees Mounted. That flag can precede
    // takeoff readiness, leaving the entire trip on a ground route even after flying becomes available.
    public async Task MoveInZoneWithFlightRecovery(Vector3 dest, MovementConfig config, System.Func<bool>? stopCondition)
    {
        if (!config.Movement.HasFlag(MovementOptions.Fly) || config.Pathing == PathingStrategy.Direct)
        {
            await MoveInZone(dest, config, stopCondition);
            return;
        }

        for (var repaths = 0; ; repaths++)
        {
            if (CancelToken.IsCancellationRequested || stopCondition?.Invoke() == true) return;

            await Mount();
            var readyDeadline = Environment.TickCount64 + FlightReadyWaitMs;
            while (Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.InFlight] && !ReadyForFlight()
                && Environment.TickCount64 < readyDeadline)
            {
                if (CancelToken.IsCancellationRequested || stopCondition?.Invoke() == true) return;
                await NextFrame(2);
            }
            if (CancelToken.IsCancellationRequested || stopCondition?.Invoke() == true) return;

            Svc.Log.Info($"{AfgConstants.LogPrefix} Travel route: mounted={Svc.Condition[ConditionFlag.Mounted]} flight={Svc.Condition[ConditionFlag.InFlight]} canFly={Control.CanFly} flightRepaths={repaths}");

            var groundSinceMs = 0L;
            var replanForFlight = false;
            var callerStopped = false;
            bool ShouldStop()
            {
                // Cancellation, a vanished FATE, retargeting and the caller's deadline always win.
                callerStopped |= CancelToken.IsCancellationRequested || stopCondition?.Invoke() == true;
                if (callerStopped) return true;
                if (replanForFlight) return true;

                if (repaths >= MaxFlightRepaths || !ReadyForFlight()
                    || Svc.Condition[ConditionFlag.InFlight]
                    || !NavmeshIPC.Instance.IsRunning()
                    || Svc.Objects.LocalPlayer is not { } player
                    || Vector3.Distance(player.Position, dest) <= Math.Max(FlightRepathMinDistance, config.Tolerance ?? 0))
                {
                    groundSinceMs = 0;
                    return false;
                }

                var now = Environment.TickCount64;
                if (groundSinceMs == 0) groundSinceMs = now;
                replanForFlight = now - groundSinceMs >= GroundTravelGraceMs;
                return replanForFlight;
            }

            // Await clib's path cleanup before starting a replacement; keep the same destination and
            // the caller's original watchdog so retries cannot create competing routes or extend travel forever.
            await MoveInZone(dest, config, ShouldStop);
            if (callerStopped || !replanForFlight || CancelToken.IsCancellationRequested) return;
            Svc.Log.Info($"{AfgConstants.LogPrefix} Still travelling on the ground with flight available; replanning for flight ({repaths + 1}/{MaxFlightRepaths})");
        }
    }

    private static bool ReadyForFlight()
        => Svc.Condition[ConditionFlag.Mounted]
        && !Svc.Condition[ConditionFlag.Mounting]
        && !Svc.Condition[ConditionFlag.Mounting71]
        && Svc.Objects.LocalPlayer is { IsDead: false, IsCasting: false }
        && Control.CanFly;

    public Task Teleport(uint territoryId, Vector3 dest, bool allowSameZoneTeleport)
        => TeleportTo(territoryId, dest, allowSameZoneTeleport);

    // Rides the local aethernet from the hub we are standing in to the shard nearest dest in territoryId.
    public Task Aethernet(uint territoryId, Vector3 dest)
        => UseAethernet(territoryId, dest);

    public Task Interact(IGameObject obj, System.Func<bool>? waitUntil, UiSkipOptions skip)
        => InteractWith(obj, waitUntil, null, skip);

    public Task DismountNow() => Dismount();
}
