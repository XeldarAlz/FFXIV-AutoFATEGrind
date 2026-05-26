using AutoFateGrind.Core.Ipc;
using clib.TaskSystem;
using ECommons.DalamudServices;
using System;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public abstract class AutoCommon : TaskBase
{
    protected void Diag(string message)
    {
        Svc.Log.Info($"[AFG] {message}");
    }

    // Verbose breadcrumb — shows up at Debug log level for deep tracing without spamming Info.
    protected void Trace(string message)
    {
        Svc.Log.Debug($"[AFG] {message}");
    }

    // Pins Status every frame to override clib's internal coordinate strings during teleport/aethernet.
    protected async Task RunWithStatusPinned(string label, Func<Task> work)
    {
        Status = label;
        void Pin(object _) => Status = label;
        Svc.Framework.Update += Pin;
        try { await work(); }
        finally { Svc.Framework.Update -= Pin; }
    }

    // Await a clib sub-task but never block longer than timeoutMs. Stays on the framework thread by
    // polling IsCompleted between frames (mixing Task.Delay would risk resuming off-thread). On
    // timeout we stop vnav, allow a short unwind grace, then return false so the caller can recover.
    // Genuine faults propagate (an intentional ErrorIf abort must still stop the run).
    protected async Task<bool> AwaitWatchdog(Task work, int timeoutMs, string label, bool stopNavOnTimeout = true)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!work.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) break;
            await NextFrame(60);
        }

        if (work.IsCompleted)
        {
            await work; // observe result / propagate exceptions
            return true;
        }

        if (CancelToken.IsCancellationRequested) return false;

        Diag($"WATCHDOG: '{label}' exceeded {timeoutMs / 1000}s; forcing recovery");
        if (stopNavOnTimeout)
        {
            try { NavmeshIPC.Instance.Stop(); }
            catch (Exception ex) { Diag($"WATCHDOG: NavmeshIPC.Stop failed: {ex.Message}"); }
        }

        var grace = Environment.TickCount64 + 3_000;
        while (!work.IsCompleted && Environment.TickCount64 < grace)
            await NextFrame(60);

        if (!work.IsCompleted)
            Diag($"WATCHDOG: '{label}' still running after stop; abandoning (task may leak)");

        return false;
    }

    // Poll a condition with a hard deadline. Returns true if met, false on timeout/cancel. Logs the
    // timeout so a stuck shop/teleport leaves a trail in /xllog instead of hanging silently.
    protected async Task<bool> WaitUntilTimed(Func<bool> condition, int timeoutMs, string scope, int checkMs = 30)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return false;
            bool ok;
            try { ok = condition(); }
            catch (Exception ex) { Diag($"WaitUntilTimed '{scope}' condition threw: {ex.Message}"); ok = false; }
            if (ok) return true;
            await NextFrame(checkMs);
        }
        Diag($"WAIT TIMEOUT: '{scope}' not satisfied within {timeoutMs / 1000}s");
        return false;
    }
}
