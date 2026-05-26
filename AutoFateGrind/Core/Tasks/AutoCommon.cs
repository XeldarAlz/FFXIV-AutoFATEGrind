using clib.TaskSystem;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Tasks;

public abstract class AutoCommon : TaskBase
{
    protected void Diag(string message)
    {
        Svc.Chat.Print($"[AFG debug] {message}");
        Svc.Log.Info($"[AFG] {message}");
    }
}
