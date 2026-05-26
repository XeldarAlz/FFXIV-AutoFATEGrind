using clib.TaskSystem;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// Tail sentinel. Lets us splice a trade pass between queued zones without complicating AutoFate.
internal sealed class AutoFateBridge(AutoFateController controller, AutoFateSession session) : TaskBase
{
    private readonly AutoFateController controller = controller;
    private readonly AutoFateSession session = session;

    protected override Task Execute()
    {
        Status = "Bridge";
        controller.HandleTradeIfPending();
        return Task.CompletedTask;
    }
}
