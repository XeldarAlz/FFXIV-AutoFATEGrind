using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Zones;
using clib.Extensions;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Network;
using System.Numerics;
using System.Threading.Tasks;
using PlayerHelpers = ECommons.GameHelpers.Player;

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

    // clib's territory-aware MoveTo rides the aethernet by itself, twice, from shifted shard positions and ignores
    // allowAethernet on the first ride (issue #75). AFG plans any hop itself (RideAethernet), so the walk never does.
    public async Task Move(uint territoryId, Vector3 dest, MovementConfig config, System.Func<bool>? stopCondition)
    {
        await TeleportTo(territoryId, dest);
        await MoveTo(dest, config, allowTeleportIfFaster: false, stopCondition, null, allowAethernet: false);
    }

    public Task MoveInZone(Vector3 dest, MovementConfig config, System.Func<bool>? stopCondition)
        => MoveTo(dest, config, allowTeleportIfFaster: false, stopCondition, null, allowAethernet: false);

    public Task Teleport(uint territoryId, Vector3 dest, bool allowSameZoneTeleport)
        => TeleportTo(territoryId, dest, allowSameZoneTeleport);

    // Rides the local aethernet from the hub we are standing in to the shard nearest dest in territoryId.
    public Task Aethernet(uint territoryId, Vector3 dest)
        => UseAethernet(territoryId, dest);

    public async Task RideAethernet(AethernetHop hop)
    {
        var shard = CityAethernet.FindShardObject(hop.Source.Id);
        ErrorIf(shard is null, $"Aethernet shard {hop.Source.Id} is not loaded");

        await MoveTo(shard!.Position, MovementConfig.Default.WithTolerance(InteractRange.Aetheryte),
            allowTeleportIfFaster: false, null, null, allowAethernet: false);
        await Dismount();

        var menu = hop.Source.IsAetheryte ? AfgConstants.AddonNames.SelectString : AfgConstants.AddonNames.TelepotTown;
        await InteractWith(shard, () => NpcInteraction.AddonOpen(menu), skip: UiSkipOptions.Talk);
        PacketDispatcher.TeleportToAethernet(hop.Source.Id, hop.Destination.Id);

        await WaitUntilThenFalse(() => Svc.Condition[ConditionFlag.BetweenAreas], "AethernetHop");
        await WaitUntil(() => PlayerHelpers.Interactable, "AethernetArrival");
    }

    public Task Interact(IGameObject obj, System.Func<bool>? waitUntil, UiSkipOptions skip)
        => InteractWith(obj, waitUntil, null, skip);

    public Task DismountNow() => Dismount();
}
