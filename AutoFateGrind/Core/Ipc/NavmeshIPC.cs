using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Numerics;

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
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPointReachable;
    private readonly ICallGateSubscriber<object> pathStop;

    private NavmeshIPC()
    {
        pathIsRunning               = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        simpleMovePathfindInProgress = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        navPathfindInProgress       = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        navIsReady                  = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        navBuildProgress            = Svc.PluginInterface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        nearestPointReachable       = Svc.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
        pathStop                    = Svc.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    // True once the current zone's navmesh is fully built and queryable; obstacle-map/pathfind IPC throw
    // "navmesh creation is in progress" while false. Older vnavmesh lacks the gate → assume ready, don't block.
    public bool IsReady()
        => IpcGate.Invoke(navIsReady.HasFunction, navIsReady.InvokeFunc, true, "[NavmeshIPC] IsReady failed");

    // 0..1 while a build is in progress; -1 when idle/complete. User-facing progress hint only.
    public float BuildProgress()
        => IpcGate.Invoke(navBuildProgress.HasFunction, navBuildProgress.InvokeFunc, BuildIdle, "[NavmeshIPC] BuildProgress failed");

    public bool IsRunning()
        => IpcGate.Invoke(pathIsRunning.HasFunction, pathIsRunning.InvokeFunc, false, "[NavmeshIPC] IsRunning failed");

    public bool IsBusy()
    {
        if (IsRunning()) return true;
        if (simpleMovePathfindInProgress.HasFunction)
        {
            try { if (simpleMovePathfindInProgress.InvokeFunc()) return true; }
            catch (Exception ex) { Svc.Log.Warning(ex, "[NavmeshIPC] PathfindInProgress(SimpleMove) failed"); }
        }
        if (navPathfindInProgress.HasFunction)
        {
            try { if (navPathfindInProgress.InvokeFunc()) return true; }
            catch (Exception ex) { Svc.Log.Warning(ex, "[NavmeshIPC] PathfindInProgress(Nav) failed"); }
        }
        return false;
    }

    public Vector3? NearestPointReachable(Vector3 position, float halfExtentXZ = 5f, float halfExtentY = 5f)
        => IpcGate.Invoke(nearestPointReachable.HasFunction,
            () => nearestPointReachable.InvokeFunc(position, halfExtentXZ, halfExtentY),
            (Vector3?)null, "[NavmeshIPC] NearestPointReachable failed");

    public void Stop()
        => IpcGate.Run(pathStop.HasFunction, pathStop.InvokeAction, "[NavmeshIPC] Stop failed");
}
