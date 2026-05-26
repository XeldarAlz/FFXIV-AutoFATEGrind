using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoFateGrind.Core.Ipc;

// Re-subscribes because clib's own wrapper is internal.
internal sealed class NavmeshIPC
{
    private static NavmeshIPC? instance;
    public static NavmeshIPC Instance => instance ??= new NavmeshIPC();

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

    public bool IsAvailable => pathIsRunning.HasFunction;

    // True once the navmesh for the current zone is fully built and queryable. While false, obstacle-map
    // and pathfind IPC calls race against vnavmesh's background build and throw "navmesh creation is in progress".
    public bool IsReady()
    {
        if (!navIsReady.HasFunction) return true; // older vnavmesh without the gate: assume ready, don't block.
        try { return navIsReady.InvokeFunc(); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[NavmeshIPC] IsReady failed"); return true; }
    }

    // 0..1 while a build is in progress; -1 when idle/complete. Used only for a user-facing progress hint.
    public float BuildProgress()
    {
        if (!navBuildProgress.HasFunction) return -1f;
        try { return navBuildProgress.InvokeFunc(); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[NavmeshIPC] BuildProgress failed"); return -1f; }
    }

    public bool IsRunning()
    {
        if (!pathIsRunning.HasFunction) return false;
        try { return pathIsRunning.InvokeFunc(); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[NavmeshIPC] IsRunning failed"); return false; }
    }

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
    {
        if (!nearestPointReachable.HasFunction) return null;
        try { return nearestPointReachable.InvokeFunc(position, halfExtentXZ, halfExtentY); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[NavmeshIPC] NearestPointReachable failed"); return null; }
    }

    public void Stop()
    {
        if (!pathStop.HasFunction) return;
        try { pathStop.InvokeAction(); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[NavmeshIPC] Stop failed"); }
    }
}
