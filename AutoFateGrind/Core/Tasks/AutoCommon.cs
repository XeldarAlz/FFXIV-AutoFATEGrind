using AutoFateGrind.Core.Ipc;
using clib.TaskSystem;
using ECommons.DalamudServices;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public abstract partial class AutoCommon : TaskBase
{
    protected static void Diag(string message, [CallerFilePath] string callerFile = "") => RunLog.Info(message, callerFile);

    protected static void Warn(string message, [CallerFilePath] string callerFile = "") => RunLog.Warning(message, callerFile);

    protected static void Trace(string message, [CallerFilePath] string callerFile = "") => RunLog.Debug(message, callerFile);

    // Pins Status every frame to override clib's internal coordinate strings during teleport/aethernet.
    protected async Task RunWithStatusPinned(string label, Func<Task> work)
    {
        Status = label;
        void Pin(object _) => Status = label;
        Svc.Framework.Update += Pin;
        try { await work(); }
        finally { Svc.Framework.Update -= Pin; }
    }

    private const int DelayPollFrames = 2;

    protected new async Task DelayMs(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            await NextFrame(DelayPollFrames);
        }
    }

    protected async Task<bool> WaitUntilTimed(Func<bool> condition, int timeoutMs, string scope, int checkFrames = 30)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var threw = false;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return false;
            bool ok;
            try { ok = condition(); }
            catch (Exception ex)
            {
                if (!threw) { Warn($"WaitUntilTimed '{scope}' condition threw (treating as unsatisfied; will retry until timeout): {ex.Message}"); threw = true; }
                ok = false;
            }
            if (ok) return true;
            await NextFrame(checkFrames);
        }
        Diag($"WAIT TIMEOUT: '{scope}' not satisfied within {timeoutMs / 1000}s");
        return false;
    }

    // Holds until vnavmesh finishes building the current zone's navmesh, surfacing a loading hint. After a
    // teleport the destination mesh is still building; obstacle-map/pathfind IPC issued now races it and
    // faults. pollFrames lets each caller keep its own cadence.
    protected async Task WaitForNavmeshReady(int timeoutMs, int pollFrames = 120)
    {
        if (NavmeshIPC.Instance.IsReady()) return;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!NavmeshIPC.Instance.IsReady())
        {
            if (CancelToken.IsCancellationRequested) return;
            if (Environment.TickCount64 >= deadline)
            {
                Diag($"WAIT TIMEOUT: navmesh not ready within {timeoutMs / 1000}s; proceeding anyway");
                return;
            }
            var progress = NavmeshIPC.Instance.BuildProgress();
            Status = progress is >= 0f and <= 1f
                ? $"Please wait — navmesh is loading ({progress * 100f:F0}%)"
                : "Please wait — navmesh is loading…";
            await NextFrame(pollFrames);
        }
    }
}
