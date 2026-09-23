using AutoFateGrind.Core.Zones;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int AethernetShortcutMs = 90_000;

    // Decided once, before the walk: re-deciding on arrival is what sent the Tuliyollal trade to the inn
    // shard and back (issue #75). A failed ride is harmless, the walk that follows starts from wherever it left us.
    internal async Task RideAethernetShortcut(Vector3 destination, string scope)
    {
        if (Svc.Objects.LocalPlayer is not { } player) return;
        if (!CityAethernet.TryPlanHop(Svc.ClientState.TerritoryType, player.Position, destination, out var hop)) return;

        var route = $"{CityAethernet.ShardName(hop.Source.Id)} → {CityAethernet.ShardName(hop.Destination.Id)}";
        if (!hop.SavesDistance)
        {
            Diag($"{scope}: walking; aethernet {route} costs ~{hop.RideMeters:F0}m against a {hop.WalkMeters:F0}m walk");
            return;
        }

        Status = $"Riding the aethernet to {CityAethernet.ShardName(hop.Destination.Id)}";
        Diag($"{scope}: aethernet {route} (~{hop.RideMeters:F0}m against a {hop.WalkMeters:F0}m walk)");
        var op = new MoveOp(o => o.RideAethernet(hop));
        await RunCancellable(op, AethernetShortcutMs, scope);
        if (op.Fault is { } fault) Diag($"{scope}: aethernet ride faulted: {fault.Message}; walking from here");
    }
}
