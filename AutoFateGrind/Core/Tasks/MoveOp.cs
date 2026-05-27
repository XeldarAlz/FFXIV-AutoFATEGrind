using clib.TaskSystem;
using Dalamud.Game.ClientState.Objects.Types;
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

    public Task Teleport(uint territoryId, Vector3 dest, bool allowSameZoneTeleport)
        => TeleportTo(territoryId, dest, allowSameZoneTeleport);

    public Task Aethernet(uint territoryId, Vector3 dest) => UseAethernet(territoryId, dest);

    public Task Interact(IGameObject obj, System.Func<bool>? waitUntil, UiSkipOptions skip)
        => InteractWith(obj, waitUntil, null, skip);

    public Task DismountNow() => Dismount();
}
