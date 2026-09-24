using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Numerics;
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
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPointReachable;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor;
    private readonly ICallGateSubscriber<object> pathStop;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> pathMoveTo;
    private readonly ICallGateSubscriber<int> pathNumWaypoints;
    private readonly ICallGateSubscriber<List<Vector3>> pathListWaypoints;

    public const int WaypointsUnavailable = -1;

    private NavmeshIPC()
    {
        pathIsRunning               = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        simpleMovePathfindInProgress = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        navPathfindInProgress       = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        navIsReady                  = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        navBuildProgress            = Svc.PluginInterface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        navPathfind                 = Svc.PluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>("vnavmesh.Nav.Pathfind");
        nearestPointReachable       = Svc.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
        pointOnFloor                = Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        pathStop                    = Svc.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        pathMoveTo                  = Svc.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        pathNumWaypoints            = Svc.PluginInterface.GetIpcSubscriber<int>("vnavmesh.Path.NumWaypoints");
        pathListWaypoints           = Svc.PluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
    }

    // True once the current zone's navmesh is fully built and queryable; obstacle-map/pathfind IPC throw
    // "navmesh creation is in progress" while false. Older vnavmesh lacks the gate → assume ready, don't block.
    public bool IsReady()
        => IpcGate.Invoke(navIsReady.HasFunction, navIsReady.InvokeFunc, true, "IsReady failed");

    // 0..1 while a build is in progress; -1 when idle/complete. User-facing progress hint only.
    public float BuildProgress()
        => IpcGate.Invoke(navBuildProgress.HasFunction, navBuildProgress.InvokeFunc, BuildIdle, "BuildProgress failed");

    public bool IsRunning()
        => IpcGate.Invoke(pathIsRunning.HasFunction, pathIsRunning.InvokeFunc, false, "IsRunning failed");

    public bool IsBusy()
    {
        if (IsRunning()) return true;
        if (simpleMovePathfindInProgress.HasFunction)
        {
            try { if (simpleMovePathfindInProgress.InvokeFunc()) return true; }
            catch (Exception ex) { RunLog.Warning(ex, "PathfindInProgress(SimpleMove) failed"); }
        }
        if (navPathfindInProgress.HasFunction)
        {
            try { if (navPathfindInProgress.InvokeFunc()) return true; }
            catch (Exception ex) { RunLog.Warning(ex, "PathfindInProgress(Nav) failed"); }
        }
        return false;
    }

    // Queues a pathfind on vnavmesh's own worker without moving. The task faults when the mesh is not
    // loaded; null when this vnavmesh lacks the IPC or the call itself threw.
    public Task<List<Vector3>>? Pathfind(Vector3 from, Vector3 to, bool fly)
        => IpcGate.Invoke<Task<List<Vector3>>?>(navPathfind.HasFunction,
            () => navPathfind.InvokeFunc(from, to, fly),
            null, "Pathfind failed");

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
}
