using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Trading;

namespace AutoFateGrind.Windows.Sections;

// Resolves the active stop-condition into the goal ring's display: a 0..1 fraction (null = no finish line),
// the big/small center text, and a short remaining phrase shared by the hero card and the ELAPSED stat tile.
internal static class GoalProgress
{
    public readonly record struct Info(float? Fraction, string CenterBig, string CenterSmall, string Remaining, bool Endless);

    public static Info Resolve(Configuration cfg, AutoFateSession? s)
    {
        var completed = s?.CompletedCount ?? 0;

        switch (cfg.ActiveMode.Id)
        {
            case MaxGemstonesMode.ModeId:
            {
                var have = s?.GemstoneCurrent ?? GemstoneCatalog.CurrentWalletCount();
                var target = Math.Max(1, cfg.TargetGemstoneCount);
                var left = Math.Max(0, target - have);
                return new Info(
                    Math.Clamp(have / (float)target, 0f, 1f),
                    have.ToString(), $"/ {target}",
                    left > 0 ? $"{left} gems to go" : "target reached", false);
            }
            case RunCountMode.ModeId:
            {
                var target = Math.Max(1, cfg.TargetFateCount);
                var left = Math.Max(0, target - completed);
                return new Info(
                    Math.Clamp(completed / (float)target, 0f, 1f),
                    completed.ToString(), $"/ {target}",
                    left > 0 ? $"{left} FATEs left" : "target reached", false);
            }
            case TimeBoxedMode.ModeId:
            {
                var targetMin = Math.Max(1, cfg.TargetMinutes);
                var elapsed = s?.Elapsed ?? TimeSpan.Zero;
                var remaining = TimeSpan.FromMinutes(targetMin) - elapsed;
                var rem = remaining > TimeSpan.Zero
                    ? remaining.TotalHours >= 1
                        ? $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m left"
                        : $"{remaining.Minutes}m {remaining.Seconds:D2}s left"
                    : "time reached";
                return new Info(
                    Math.Clamp((float)(elapsed.TotalMinutes / targetMin), 0f, 1f),
                    $"{(int)elapsed.TotalMinutes}m", $"/ {targetMin}m",
                    rem, false);
            }
            default:
                return new Info(null, completed.ToString(), "done", "until you stop", true);
        }
    }
}
