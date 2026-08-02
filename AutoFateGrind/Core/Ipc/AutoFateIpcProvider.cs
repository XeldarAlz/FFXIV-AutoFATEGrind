using ECommons.DalamudServices;
using ECommons.EzIpcManager;

namespace AutoFateGrind.Core.Ipc;

// Outward IPC control surface, registered by EzIPC under the plugin's internal name and torn down by
// ECommonsMain.Dispose. Endpoints swallow exceptions so none crosses the IPC boundary.
internal static class AutoFateIpcProvider
{
    public static void Register() => EzIPC.Init(typeof(AutoFateIpcProvider));

    [EzIPC("Control.APIVersion")]
    public static int ApiVersion() => AfgConstants.IpcApiVersion;

    [EzIPC("Control.IsRunning")]
    public static bool IsRunning()
    {
        try
        {
            return Plugin.Instance.Controller.Running;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, $"{AfgConstants.LogPrefix} IPC IsRunning failed");
            return false;
        }
    }

    [EzIPC("Control.Start")]
    public static bool Start()
        => OnFramework(() => Plugin.Instance.Controller.IpcStart(), false, "Start");

    [EzIPC("Control.StartWith")]
    public static bool StartWith(List<uint>? zones, string? modeId, int? stopValue, int? gearsetIndex, List<uint>? avoidedFates)
        => OnFramework(() => Plugin.Instance.Controller.IpcStartWith(zones, modeId, stopValue, gearsetIndex, avoidedFates), false, "StartWith");

    [EzIPC("Control.Stop")]
    public static void Stop()
        => OnFramework(() => Plugin.Instance.Controller.IpcStop(), "Stop");

    [EzIPC("Control.Toggle")]
    public static bool Toggle()
        => OnFramework(() => Plugin.Instance.Controller.IpcToggle(), false, "Toggle");

    // Blocks for the result on the framework thread. Runs inline when already on it, so no deadlock.
    private static T OnFramework<T>(Func<T> work, T fallback, string label)
    {
        try
        {
            return Svc.Framework.RunOnFrameworkThread(work).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, $"{AfgConstants.LogPrefix} IPC {label} failed");
            return fallback;
        }
    }

    private static void OnFramework(Action work, string label)
    {
        try
        {
            Svc.Framework.RunOnFrameworkThread(work).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, $"{AfgConstants.LogPrefix} IPC {label} failed");
        }
    }
}
