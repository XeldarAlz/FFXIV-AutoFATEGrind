namespace AutoFateGrind.Core.Stats;

// One completed (or stopped) grind run, persisted to history. Plain serializable scalars only.
[Serializable]
public sealed class RunRecord
{
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public double DurationSeconds { get; set; }

    public int FatesCompleted { get; set; }
    public int GemstonesEarned { get; set; }

    public long ExpEarned { get; set; }
    public int LevelsGained { get; set; }
    public int StartLevel { get; set; }
    public int EndLevel { get; set; }
    public uint JobId { get; set; }
    public string JobAbbr { get; set; } = "";

    public string ModeName { get; set; } = "";
    public List<string> ZoneNames { get; set; } = [];

    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
    public double FatesPerHour => DurationSeconds > 0 ? FatesCompleted / (DurationSeconds / 3600.0) : 0;
    public double ExpPerHour => DurationSeconds > 0 ? ExpEarned / (DurationSeconds / 3600.0) : 0;
}
