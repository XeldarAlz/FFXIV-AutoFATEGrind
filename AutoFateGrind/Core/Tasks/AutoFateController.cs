using AutoFateGrind.Core.Zones;

namespace AutoFateGrind.Core.Tasks;

// v1 stub. Holds the visible Running/Status flags so the UI compiles and renders;
// the actual automation engine (Svc.Automation.Start(new AutoFate(zone))) will land
// in the next pass once IPCs and FateScanner are in.
internal sealed class AutoFateController
{
    public bool Running { get; private set; }
    public string Status { get; private set; } = "Idle";

    public void Run(ZoneInfo zone)
    {
        // TODO: Svc.Automation.Start(new AutoFate(zone));
        Running = true;
        Status = $"(stub) Would start {zone.Name}";
    }

    public void RunAll(IEnumerable<ZoneInfo> zones)
    {
        foreach (var z in zones)
        {
            // TODO: Svc.Automation.Start(new AutoFate(z), queue: true after first iteration);
            Status = $"(stub) Queued {z.Name}";
            _ = z;
        }
        Running = true;
    }

    public void Stop()
    {
        // TODO: Svc.Automation.Stop();
        Running = false;
        Status = "Idle";
    }
}
