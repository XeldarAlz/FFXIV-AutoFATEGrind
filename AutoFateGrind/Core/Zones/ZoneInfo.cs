using System.Numerics;

namespace AutoFateGrind.Core.Zones;

public sealed class ZoneInfo
{
    public required uint TerritoryId { get; init; }
    public required string Name { get; init; }
    public required ExpansionKind Expansion { get; init; }
    public required int MinLevel { get; init; }
    public required uint AchievementId { get; init; }
    public Vector3 CentralLanding { get; init; }
    public string? IconFile { get; init; }

    // Live state, refreshed each draw.
    public bool Unlocked;
    public int AchievementCurrent;
    public int AchievementMax;
    public int ActiveFateCount;
    public int CompletedThisRun;

    public bool AchievementDone => AchievementMax > 0 && AchievementCurrent >= AchievementMax;
    public float AchievementProgress => AchievementMax > 0
        ? Math.Clamp((float)AchievementCurrent / AchievementMax, 0f, 1f)
        : 0f;
}
