using AutoFateGrind.Core.Zones;

namespace AutoFateGrind.Core.Tasks;

public sealed class AutoFateSession
{
    public int CompletedCount;
    public DateTime StartedAt = DateTime.UtcNow;
    public int GemstoneStart;
    public int GemstoneCurrent;

    // Experience progress for the run. All best-effort: if ExpReader can't read (between zones, login
    // edge), the sample is skipped and accrual simply pauses until the next readable tick.
    public uint JobId;
    public string JobAbbr = "";
    public int StartLevel;
    public int CurrentLevel;
    // Accumulated across the run by summing positive deltas WITHIN each job segment. The class queue (or a
    // manual swap) can change jobs mid-run; a single cross-job cumulative diff would go wrong when the new
    // job has a different cumulative total, so we re-baseline on every job change instead of diffing across.
    public long ExpEarned;
    public int LevelsGained;

    // Current segment's job and the baselines that the next positive delta is measured against. A job
    // change resets the baselines without crediting the cross-job jump.
    private uint trackedJobId;
    private long segmentBaselineExp;
    private int segmentBaselineLevel;
    private bool startCaptured;

    // Set once the run is written to history, so terminal hand-offs and Stop can't double-record it.
    public bool Recorded;

    public void CaptureStartExp()
    {
        if (Core.Game.ExpReader.Read() is not { } s) return;
        JobId = s.JobId;
        JobAbbr = s.JobAbbr;
        StartLevel = s.Level;
        CurrentLevel = s.Level;
        RebaselineTo(s);
        startCaptured = true;
    }

    public void UpdateExp()
    {
        if (Core.Game.ExpReader.Read() is not { } s) return;

        // Start sample was missed (player not loaded when the run began); baseline now without crediting.
        if (!startCaptured)
        {
            StartLevel = s.Level;
            CurrentLevel = s.Level;
            RebaselineTo(s);
            startCaptured = true;
            return;
        }

        // Job changed mid-run: start a fresh segment; never diff one job's total against another's.
        if (s.JobId != trackedJobId)
        {
            RebaselineTo(s);
            CurrentLevel = s.Level;
            return;
        }

        var cur = Core.Game.ExpReader.Cumulative(s);
        if (cur > segmentBaselineExp) ExpEarned += cur - segmentBaselineExp;
        segmentBaselineExp = cur;

        if (s.Level > segmentBaselineLevel) LevelsGained += s.Level - segmentBaselineLevel;
        segmentBaselineLevel = s.Level;

        CurrentLevel = s.Level;
        JobId = s.JobId;
        JobAbbr = s.JobAbbr;
    }

    private void RebaselineTo(Core.Game.ExpReader.Snapshot s)
    {
        trackedJobId = s.JobId;
        segmentBaselineExp = Core.Game.ExpReader.Cumulative(s);
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

    // Fault-resume bookkeeping. The flag is set only when the grind task ends by throwing (and only when
    // AutoResumeOnFault is on); the controller restarts a bounded number of times within a sliding window.
    public bool EndedWithFault;
    public int  FaultResumeZoneIndex;
    public int  FaultResumeCount;
    public long FaultWindowStartedAtMs;

    public TimeSpan Elapsed => DateTime.UtcNow - StartedAt;
    public int GemstonesEarned => Math.Max(0, GemstoneCurrent - GemstoneStart);
    public double FatesPerHour => Elapsed.TotalHours > 0 ? CompletedCount / Elapsed.TotalHours : 0;
}
