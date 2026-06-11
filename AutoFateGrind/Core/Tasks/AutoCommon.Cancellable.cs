using clib.TaskSystem;
using System;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public abstract partial class AutoCommon
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
        var abortThrew = false;
        while (!work.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) break;
            if (abortIf is not null)
            {
                bool trip;
                try { trip = abortIf(); }
                catch (Exception ex)
                {
                    if (!abortThrew) { Warn($"RunCancellable '{label}' abortIf threw (abort disabled, watchdog still active): {ex.Message}"); abortThrew = true; }
                    trip = false;
                }
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
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Warn($"RunCancellable '{label}' Cancel threw: {ex.Message}"); }
    }
}
