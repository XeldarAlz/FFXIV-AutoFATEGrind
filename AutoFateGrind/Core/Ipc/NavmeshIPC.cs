using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Ipc;

// Re-subscribes because clib's own wrapper is internal.
internal sealed class NavmeshIPC
{
    private static NavmeshIPC? instance;
    public static NavmeshIPC Instance => instance ??= new NavmeshIPC();

    // BuildProgress idle sentinel: -1 = no build running.
    private const float BuildIdle = -1f;

    private readonly ICallGateSubscriber<bool> pathIsRunning;
    private readonly ICallGateSubscriber<bool> simpleMovePathfindInProgress;
    private readonly ICallGateSubscriber<bool> navPathfindInProgress;
    private readonly ICallGateSubscriber<bool> navIsReady;
    private readonly ICallGateSubscriber<float> navBuildProgress;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>> navPathfind;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>> navPathfindCancelable;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPointReachable;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor;
    private readonly ICallGateSubscriber<object> pathStop;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> pathMoveTo;
    private readonly ICallGateSubscriber<Vector3, bool, object> simpleMovePathfindAndMoveTo;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> simpleMovePathfindAndMoveCloseTo;
    private readonly ICallGateSubscriber<int> pathNumWaypoints;
    private readonly ICallGateSubscriber<List<Vector3>> pathListWaypoints;
    private readonly ICallGateSubscriber<float> pathGetTolerance;

    // Cached once so the per-frame move and stall checks do not allocate a delegate on every call.
    private readonly Func<bool> isRunningCall;
    private readonly Func<bool> isMovePendingCall;
    private readonly Func<bool> isPathfindingCall;

    public const int WaypointsUnavailable = -1;

    private NavmeshIPC()
    {
        pathIsRunning               = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        simpleMovePathfindInProgress = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        navPathfindInProgress       = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        navIsReady                  = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        navBuildProgress            = Svc.PluginInterface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        navPathfind                 = Svc.PluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>("vnavmesh.Nav.Pathfind");
        navPathfindCancelable       = Svc.PluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable");
        nearestPointReachable       = Svc.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
        pointOnFloor                = Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        pathStop                    = Svc.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        pathMoveTo                  = Svc.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        simpleMovePathfindAndMoveTo = Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, object>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        simpleMovePathfindAndMoveCloseTo = Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        pathNumWaypoints            = Svc.PluginInterface.GetIpcSubscriber<int>("vnavmesh.Path.NumWaypoints");
        pathListWaypoints           = Svc.PluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
        pathGetTolerance            = Svc.PluginInterface.GetIpcSubscriber<float>("vnavmesh.Path.GetTolerance");

        isRunningCall = pathIsRunning.InvokeFunc;
        isMovePendingCall = simpleMovePathfindInProgress.InvokeFunc;
        isPathfindingCall = navPathfindInProgress.InvokeFunc;
    }

    // Arrival radius vnav applies on top of a move's own tolerance; a MoveOp skips a move inside the larger of the two.
    public float GetTolerance()
        => IpcGate.Invoke(pathGetTolerance.HasFunction, pathGetTolerance.InvokeFunc, 0f, "GetTolerance failed");

    // True once the current zone's navmesh is fully built and queryable; obstacle-map/pathfind IPC throw
    // "navmesh creation is in progress" while false. Older vnavmesh lacks the gate → assume ready, don't block.
    public bool IsReady()
        => IpcGate.Invoke(navIsReady.HasFunction, navIsReady.InvokeFunc, true, "IsReady failed");

    // 0..1 while a build is in progress; -1 when idle/complete. User-facing progress hint only.
    public float BuildProgress()
        => IpcGate.Invoke(navBuildProgress.HasFunction, navBuildProgress.InvokeFunc, BuildIdle, "BuildProgress failed");

    public bool IsRunning()
        => IpcGate.Invoke(pathIsRunning.HasFunction, isRunningCall, false, "IsRunning failed");

    public bool IsBusy() => IsRunning() || IsMovePending() || IsPathfinding();

    // A SimpleMove request still searching for its route, before Path takes it over.
    public bool IsMovePending()
        => IpcGate.Invoke(simpleMovePathfindInProgress.HasFunction, isMovePendingCall, false, "PathfindInProgress(SimpleMove) failed");

    public bool IsPathfinding()
        => IpcGate.Invoke(navPathfindInProgress.HasFunction, isPathfindingCall, false, "PathfindInProgress(Nav) failed");

    // Ends arriveWithin metres short of the destination, which is how a MoveOp parks beside a point.
    public bool PathfindAndMoveCloseTo(Vector3 destination, bool fly, float arriveWithin)
        => IpcGate.Invoke(simpleMovePathfindAndMoveCloseTo.HasFunction,
            () => simpleMovePathfindAndMoveCloseTo.InvokeFunc(destination, fly, arriveWithin),
            false, "SimpleMove.PathfindAndMoveCloseTo failed");

    // Queues a pathfind on vnavmesh's own worker without moving. The task faults when the mesh is not
    // loaded; null when this vnavmesh lacks the IPC or the call itself threw. vnav runs pathfinds one at a
    // time, so an abandoned query has to be cancelled or the grind's next move waits behind it.
    public Task<List<Vector3>>? Pathfind(Vector3 from, Vector3 to, bool fly, CancellationToken cancel)
    {
        if (navPathfindCancelable.HasFunction)
        {
            return IpcGate.Invoke<Task<List<Vector3>>?>(true,
                () => navPathfindCancelable.InvokeFunc(from, to, fly, cancel),
                null, "PathfindCancelable failed");
        }
        return IpcGate.Invoke<Task<List<Vector3>>?>(navPathfind.HasFunction,
            () => navPathfind.InvokeFunc(from, to, fly),
            null, "Pathfind failed");
    }

    public Vector3? NearestPointReachable(Vector3 position, float halfExtentXZ = 5f, float halfExtentY = 5f)
        => IpcGate.Invoke(nearestPointReachable.HasFunction,
            () => nearestPointReachable.InvokeFunc(position, halfExtentXZ, halfExtentY),
            (Vector3?)null, "NearestPointReachable failed");

    // The highest mesh point below position within ±halfExtentXZ. vnavmesh names the flag allowUnlandable but
    // applies it as its flood-fill reachability filter.
    public Vector3? PointOnFloor(Vector3 position, bool allowUnreachable, float halfExtentXZ)
        => IpcGate.Invoke(pointOnFloor.HasFunction,
            () => pointOnFloor.InvokeFunc(position, allowUnreachable, halfExtentXZ),
            (Vector3?)null, "PointOnFloor failed");

    // Waypoints left on the path vnav is following; WaypointsUnavailable when this vnavmesh has no such IPC.
    public int NumWaypoints()
        => IpcGate.Invoke(pathNumWaypoints.HasFunction, pathNumWaypoints.InvokeFunc, WaypointsUnavailable, "NumWaypoints failed");

    // The waypoint vnav is steering toward right now. The IPC copies the whole list, so callers cache it.
    public Vector3? CurrentWaypoint()
    {
        var waypoints = IpcGate.Invoke<List<Vector3>?>(pathListWaypoints.HasFunction, pathListWaypoints.InvokeFunc, null, "ListWaypoints failed");
        return waypoints is { Count: > 0 } ? waypoints[0] : null;
    }

    // vnavmesh registers Path.Stop and Path.MoveTo as actions, so HasFunction is always false for them.
    public void Stop()
        => IpcGate.Run(pathStop.HasAction, pathStop.InvokeAction, "Stop failed");

    // Follows the waypoints as given, with no path search, so the caller has to know the way is clear.
    public void MoveAlong(List<Vector3> waypoints, bool fly)
        => IpcGate.Run(pathMoveTo.HasAction, () => pathMoveTo.InvokeAction(waypoints, fly), "Path.MoveTo failed");

    // Plans and walks on vnavmesh's own loop; used only for the user's manual click-to-walk, never by the grind.
    public void PathfindAndMoveTo(Vector3 destination, bool fly)
        => IpcGate.Run(simpleMovePathfindAndMoveTo.HasAction, () => simpleMovePathfindAndMoveTo.InvokeAction(destination, fly), "SimpleMove.PathfindAndMoveTo failed");
}
