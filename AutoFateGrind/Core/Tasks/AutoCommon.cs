using clib.TaskSystem;
using ECommons.DalamudServices;
using System;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public abstract class AutoCommon : TaskBase
{
    protected const int SubTaskUnwindGraceMs = 5_000;

    // Run a single clib operation as its own cancellable AutoTask and bound it with a wall-clock cap.
    // Unlike AwaitWatchdog (which could only NavmeshIPC.Stop and then abandon a still-running clib
    // task), this owns the operation's CancellationTokenSource: on timeout — or when abortIf trips —
    // it calls op.Cancel(), which cancels every await inside the operation and fires its registered
    // cleanups (movement hook off, the MoveTo OnDispose stops vnav). The operation genuinely unwinds,
    // so it can never linger and fight the next move/teleport. Returns true if the op finished on its
    // own, false if it had to be cancelled (timeout, abort, or run cancellation).
    internal async Task<bool> RunCancellable(MoveOp op, int timeoutMs, string label, Func<bool>? abortIf = null)
    {
        var tcs = new TaskCompletionSource();
        op.Run(() => tcs.TrySetResult());
        var work = tcs.Task;

        // The op runs on its own CancellationTokenSource, so cancelling the whole run (Stop) would not
        // reach it. Bind them: if our token fires, cancel the op too, so a Stop tears everything down.
        using var reg = CancelToken.Register(() => TryCancel(op, label));

        var deadline = Environment.TickCount64 + timeoutMs;
        var aborted = false;
        while (!work.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) break;
            if (abortIf is not null)
            {
                bool trip;
                try { trip = abortIf(); }
                catch (Exception ex) { Diag($"RunCancellable '{label}' abortIf threw: {ex.Message}"); trip = false; }
                if (trip) { aborted = true; break; }
            }
            await NextFrame(4);
        }

        if (work.IsCompleted) return true;

        if (CancelToken.IsCancellationRequested)
        {
            TryCancel(op, label);
            return false;
        }

        Diag(aborted
            ? $"RunCancellable '{label}' aborting sub-task (abort condition met)"
            : $"WATCHDOG: '{label}' exceeded {timeoutMs / 1000}s; cancelling sub-task");
        TryCancel(op, label);

        var grace = Environment.TickCount64 + SubTaskUnwindGraceMs;
        while (!work.IsCompleted && Environment.TickCount64 < grace)
            await NextFrame(4);

        if (!work.IsCompleted)
            Diag($"WATCHDOG: '{label}' did not unwind {SubTaskUnwindGraceMs / 1000}s after Cancel (unexpected)");

        return false;
    }

    private void TryCancel(MoveOp op, string label)
    {
        // The op can complete (and dispose its CTS) between our IsCompleted check and here; Cancel on a
        // disposed CTS throws. Swallow it — a completed op needs no cancelling.
        try { op.Cancel(); }
        catch (ObjectDisposedException) { /* already completed */ }
        catch (Exception ex) { Diag($"RunCancellable '{label}' Cancel threw: {ex.Message}"); }
    }

    protected void Diag(string message)
    {
        Svc.Log.Info($"[AFG] {message}");
    }

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
