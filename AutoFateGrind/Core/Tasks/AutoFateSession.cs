using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;

namespace AutoFateGrind.Core.Tasks;

public sealed class AutoFateSession
{
    public int CompletedCount;
    public DateTime StartedAt = DateTime.UtcNow;
    public int GemstoneCurrent;

    // Accumulates positive wallet deltas; end-minus-start would undercount spent/capped gems.
    public int GemstonesEarned;
    private int gemWalletLastSeen;
    private bool gemBaselineCaptured;

    public uint JobId;
    public string JobAbbr = "";
    public int StartLevel;
    public int CurrentLevel;
    // Accumulated per job segment; reset baseline on job change to avoid cross-job cumulative diff errors.
    public long ExpEarned;
    public int LevelsGained;

    private uint trackedJobId;
    private long segmentBaselineExp;
    private int segmentBaselineLevel;
    private bool startCaptured;

    public bool Recorded;
    public bool CompletedByStopCondition;
    public bool AfterActionDispatched;

    public bool DidNothing => CompletedCount == 0 && ExpEarned == 0 && GemstonesEarned == 0;

    public void CaptureStartExp()
    {
        if (Core.Game.Player.ExpReader.Read() is not { } s) return;
        JobId = s.JobId;
        JobAbbr = s.JobAbbr;
        StartLevel = s.Level;
        CurrentLevel = s.Level;
        RebaselineTo(s);
        startCaptured = true;
    }

    public void UpdateExp()
    {
        if (Core.Game.Player.ExpReader.Read() is not { } s) return;

        if (!startCaptured)
        {
            StartLevel = s.Level;
            CurrentLevel = s.Level;
            RebaselineTo(s);
            startCaptured = true;
            return;
        }

        if (s.JobId != trackedJobId)
        {
            RebaselineTo(s);
            CurrentLevel = s.Level;
            return;
        }

        var cur = Core.Game.Player.ExpReader.Cumulative(s);
        if (cur > segmentBaselineExp) ExpEarned += cur - segmentBaselineExp;
        segmentBaselineExp = cur;

        if (s.Level > segmentBaselineLevel) LevelsGained += s.Level - segmentBaselineLevel;
        segmentBaselineLevel = s.Level;

        CurrentLevel = s.Level;
        JobId = s.JobId;
        JobAbbr = s.JobAbbr;
    }

    private void RebaselineTo(Core.Game.Player.ExpReader.Snapshot s)
    {
        trackedJobId = s.JobId;
        segmentBaselineExp = Core.Game.Player.ExpReader.Cumulative(s);
        segmentBaselineLevel = s.Level;
        JobId = s.JobId;
        JobAbbr = s.JobAbbr;
    }

    public double ExpPerHour => Elapsed.TotalHours > 0 ? ExpEarned / Elapsed.TotalHours : 0;

    public ZoneInfo? PendingTradeFromZone;
    public bool PendingRepair;
    public ZoneInfo? PendingRepairFromZone;
    public int FatesSinceLastBreak;
    public bool PendingHumanize;
    public ZoneInfo? PendingHumanizeFromZone;

    public readonly HashSet<uint> UnreachableZoneIds = [];

    public bool EndedWithFault;
    public int  FaultResumeZoneIndex;
    public int  FaultResumeCount;
    public long FaultWindowStartedAtMs;

    public void UpdateGemstones()
    {
        if (!GemstoneCatalog.TryCurrentWalletCount(out var wallet)) return;
        GemstoneCurrent = wallet;

        if (!gemBaselineCaptured) { gemWalletLastSeen = wallet; gemBaselineCaptured = true; return; }
        if (wallet > gemWalletLastSeen) GemstonesEarned += wallet - gemWalletLastSeen;
        gemWalletLastSeen = wallet;
    }

    private long pausedMs;
    private long pauseStartedAtMs;

    public void BeginPause()
    {
        if (pauseStartedAtMs != 0) return;
        pauseStartedAtMs = Environment.TickCount64;
    }

    public void EndPause()
    {
        if (pauseStartedAtMs == 0) return;
        pausedMs += Environment.TickCount64 - pauseStartedAtMs;
        pauseStartedAtMs = 0;
    }

    private long PausedTotalMs
        => pausedMs + (pauseStartedAtMs == 0 ? 0 : Environment.TickCount64 - pauseStartedAtMs);

    public TimeSpan Elapsed => DateTime.UtcNow - StartedAt - TimeSpan.FromMilliseconds(PausedTotalMs);
    public double FatesPerHour => Elapsed.TotalHours > 0 ? CompletedCount / Elapsed.TotalHours : 0;
}
