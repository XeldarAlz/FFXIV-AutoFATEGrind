using System.Runtime.CompilerServices;


namespace AutoFateGrind.Core.Ipc;

// Collapses the uniform "guard HasFunction, invoke, log + safe fallback on throw" shape shared by the IPC
// facades so each wrapped call is a single line and the error-handling policy lives in one place.
internal static class IpcGate
{
    public static T Invoke<T>(bool hasFunction, Func<T> call, T fallback, string label, [CallerFilePath] string callerFile = "")
    {
        if (!hasFunction) return fallback;
        try { return call(); }
        catch (Exception ex) { RunLog.Warning(ex, label, callerFile); return fallback; }
    }

    public static void Run(bool hasFunction, Action call, string label, [CallerFilePath] string callerFile = "")
    {
        if (!hasFunction) return;
        try { call(); }
        catch (Exception ex) { RunLog.Warning(ex, label, callerFile); }
    }
}
