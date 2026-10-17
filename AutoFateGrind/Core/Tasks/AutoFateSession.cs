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
    // Items a trade failed to buy; the wallet stays over the threshold, so retrying would re-fire after every FATE.
    public readonly HashSet<uint> TradeSkippedItemIds = [];
    // Trades that never reached the trader because the character's own state stalled every teleport cast. The first
    // one is retried after the next FATE; from the second on the items are skipped like any other failed buy.
    public int TradeTransportBlocks;
    public bool PendingRepair;
    public ZoneInfo? PendingRepairFromZone;
    public int FatesSinceLastBreak;
    private int fatesBeforeNextBreak;
    public bool PendingHumanize;
    public ZoneInfo? PendingHumanizeFromZone;
    public uint YokaiTargetMinionId;
    public bool PendingYokaiAdvance;
    public bool StopWhenSafe;

    public int FatesBeforeNextBreak(int configured)
    {
        if (fatesBeforeNextBreak == 0)
        {
            fatesBeforeNextBreak = Pacing.FatesBeforeBreak(configured);
        }
        return fatesBeforeNextBreak;
    }

    public void ResetBreakCounter()
    {
        FatesSinceLastBreak = 0;
        fatesBeforeNextBreak = 0;
    }

    // Undoes HandoffTriggers.QueueIfDue. Its triggers are state checks (gear condition, wallet, break counter), so a
    // dropped hand-off queues itself again after the next FATE.
    public void ClearQueuedHandoffs()
    {
        PendingRepair = false;
        PendingRepairFromZone = null;
        PendingTradeFromZone = null;
        PendingHumanize = false;
        PendingHumanizeFromZone = null;
    }

    public IdleSpot? LastIdleSpot;

    public readonly HashSet<uint> UnreachableZoneIds = [];

    private readonly Dictionary<uint, int> deathsByFateId = [];

    public int CountDeath(uint fateId)
    {
        deathsByFateId.TryGetValue(fateId, out var deaths);
        deaths++;
        deathsByFateId[fateId] = deaths;
        return deaths;
    }

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
