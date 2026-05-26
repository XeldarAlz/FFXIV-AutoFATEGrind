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

    // Pins Status every frame to override clib's internal coordinate strings during teleport/aethernet.
    protected async Task RunWithStatusPinned(string label, Func<Task> work)
    {
        Status = label;
        void Pin(object _) => Status = label;
        Svc.Framework.Update += Pin;
        try { await work(); }
        finally { Svc.Framework.Update -= Pin; }
    }
}
