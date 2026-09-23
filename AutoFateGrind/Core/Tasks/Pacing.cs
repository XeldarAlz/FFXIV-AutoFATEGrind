namespace AutoFateGrind.Core.Tasks;

// Every client sees a FATE end on the same server frame; each decision rolls its own timing so a zone
// full of AFG users does not move, teleport and swap in lockstep (issue #74).
internal static class Pacing
{
    // Squaring a uniform roll bunches results at the short end with a long tail, like human reaction times.
    private const double ReactionSkewExponent = 2.0;
    private const double DistractionChance = 0.05;
    private const int    DistractionMinMs = 8_000;
    private const int    DistractionMaxMs = 20_000;
    private const int    MaxReactionSec = 30;

    private const float  TeleportShortcutMinSavingMeters = 220f;
    private const float  TeleportShortcutMaxSavingMeters = 450f;
    private const int    IdleSwapMinMs = 20_000;
    private const int    IdleSwapMaxMs = 60_000;
    private const int    FollowUpExtraMaxMs = 10_000;
    private const int    ReviveMinMs = 2_000;
    private const int    ReviveMaxMs = 8_000;
    private const double BreakIntervalSpread = 0.3;

    private static readonly Random random = new();

    public static bool Active => Plugin.Cfg.PacingEnabled;

    public static bool PickVarietyActive => Plugin.Cfg.PacingEnabled && Plugin.Cfg.PacingPickVariety;

    public static int ReactionDelayMs()
    {
        if (!Active)
        {
            return 0;
        }

        var cfg = Plugin.Cfg;
        var minimumMs = Math.Clamp(cfg.PacingReactionMinSec, 0, MaxReactionSec) * 1000;
        var maximumMs = Math.Max(minimumMs, Math.Clamp(cfg.PacingReactionMaxSec, 0, MaxReactionSec) * 1000);
        var delayMs = minimumMs + (int)((maximumMs - minimumMs) * Math.Pow(random.NextDouble(), ReactionSkewExponent));
        if (random.NextDouble() < DistractionChance)
        {
            delayMs += Roll(DistractionMinMs, DistractionMaxMs);
        }
        return delayMs;
    }

    public static float TeleportShortcutSavingMeters(float baselineMeters)
        => Active
            ? TeleportShortcutMinSavingMeters + (float)random.NextDouble() * (TeleportShortcutMaxSavingMeters - TeleportShortcutMinSavingMeters)
            : baselineMeters;

    public static int IdleWaitBeforeSwapMs(int baselineMs)
        => Active ? Roll(IdleSwapMinMs, IdleSwapMaxMs) : baselineMs;

    // Only ever lengthens the watch: cutting it short would miss chain sequels.
    public static int FollowUpWatchMs(int baselineMs)
        => Active ? baselineMs + Roll(0, FollowUpExtraMaxMs) : baselineMs;

    public static int ReviveDelayMs()
        => Active ? Roll(ReviveMinMs, ReviveMaxMs) : 0;

    public static int FatesBeforeBreak(int configured)
    {
        var baseline = Math.Max(1, configured);
        if (!Active)
        {
            return baseline;
        }

        var factor = 1.0 + (random.NextDouble() * 2.0 - 1.0) * BreakIntervalSpread;
        return Math.Max(1, (int)Math.Round(baseline * factor));
    }

    public static int RollIndex(int count) => random.Next(count);

    private static int Roll(int minimum, int maximum) => random.Next(minimum, maximum + 1);
}
