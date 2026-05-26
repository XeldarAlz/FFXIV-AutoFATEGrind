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
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPointReachable;

    private NavmeshIPC()
    {
        pathIsRunning               = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        simpleMovePathfindInProgress = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        navPathfindInProgress       = Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        nearestPointReachable       = Svc.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
    }

    public bool IsAvailable => pathIsRunning.HasFunction;

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
}
