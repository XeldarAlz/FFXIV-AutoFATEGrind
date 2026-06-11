using AutoFateGrind.Core.Ipc;
using clib.TaskSystem;
using ECommons.DalamudServices;
using System;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public abstract partial class AutoCommon : TaskBase
{
    protected void Diag(string message) => Svc.Log.Info($"{AfgConstants.LogPrefix} {message}");

    protected void Warn(string message) => Svc.Log.Warning($"{AfgConstants.LogPrefix} {message}");

    protected void Trace(string message) => Svc.Log.Debug($"{AfgConstants.LogPrefix} {message}");

    // Pins Status every frame to override clib's internal coordinate strings during teleport/aethernet.
    protected async Task RunWithStatusPinned(string label, Func<Task> work)
    {
        Status = label;
        void Pin(object _) => Status = label;
        Svc.Framework.Update += Pin;
        try { await work(); }
        finally { Svc.Framework.Update -= Pin; }
    }

    protected async Task<bool> WaitUntilTimed(Func<bool> condition, int timeoutMs, string scope, int checkMs = 30)
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
            await NextFrame(checkMs);
        }
        Diag($"WAIT TIMEOUT: '{scope}' not satisfied within {timeoutMs / 1000}s");
        return false;
    }

    // Holds until vnavmesh finishes building the current zone's navmesh, surfacing a loading hint. After a
    // teleport the destination mesh is still building; obstacle-map/pathfind IPC issued now races it and
    // faults. pollMs lets each caller keep its own cadence.
    protected async Task WaitForNavmeshReady(int timeoutMs, int pollMs = 120)
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
            await NextFrame(pollMs);
        }
    }
}
