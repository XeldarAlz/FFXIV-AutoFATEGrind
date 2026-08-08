using clib.Services;
using System;

namespace AutoFateGrind.Core.Tasks;

internal enum PauseReason { None, Manual, InContent }

internal sealed partial class AutoFateController
{
    public PauseReason PauseReason { get; private set; } = PauseReason.None;
    public bool Paused => PauseReason != PauseReason.None;

    private int pausedZoneIndex;

    public bool CanPause
        => session is not null
        && Phase is AutoPhase.Grinding or AutoPhase.Trading or AutoPhase.Repairing or AutoPhase.Humanizing;

    public void Pause(PauseReason reason)
    {
        if (reason == PauseReason.None) return;

        if (Paused)
        {
            if (reason == PauseReason.Manual && PauseReason == PauseReason.InContent)
            {
                PauseReason = PauseReason.Manual;
                Diag("Auto-pause promoted to a manual pause; leaving content will no longer resume.");
            }
            return;
        }

        if (!CanPause)
        {
            if (reason == PauseReason.Manual) ECommons.DalamudServices.Svc.Chat.Print("[AFG] Nothing to pause.");
            return;
        }

        pausedZoneIndex = CurrentZoneIndex();
        PauseReason = reason;
        Phase = AutoPhase.Paused;
        session!.BeginPause();
        currentTask = null;
        Svc.Automation.Stop();

        var zoneName = activeZones.Count > 0 ? activeZones[pausedZoneIndex].Name : "?";
        Diag($"Run paused ({reason}); session kept, resume zone {zoneName}.");
        ECommons.DalamudServices.Svc.Chat.Print(reason == PauseReason.InContent
            ? "[AFG] Paused: you are in instanced content. The grind resumes once you are back outside."
            : "[AFG] Paused. Your zones, goal, and session stats are kept until you resume or stop.");
    }

    public void Resume()
    {
        if (!Paused) return;

        var resuming = session;
        if (resuming is null || activeZones.Count == 0)
        {
            Diag("Resume requested with no session or no zones; stopping instead.");
            Stop();
            return;
        }

        PauseReason = PauseReason.None;
        resuming.EndPause();

        var index = Math.Clamp(pausedZoneIndex, 0, activeZones.Count - 1);
        Diag($"Resuming FATE grind at {activeZones[index].Name}.");
        ECommons.DalamudServices.Svc.Chat.Print($"[AFG] Resuming in {activeZones[index].Name}.");
        StartFateGrind(index, resuming);
    }

    public void TogglePause()
    {
        if (Paused) Resume();
        else Pause(PauseReason.Manual);
    }

    private int CurrentZoneIndex()
    {
        if (activeZones.Count == 0) return 0;
        if (grindTask is null) return 0;
        return Math.Clamp(grindTask.ZoneIndex, 0, activeZones.Count - 1);
    }
}
