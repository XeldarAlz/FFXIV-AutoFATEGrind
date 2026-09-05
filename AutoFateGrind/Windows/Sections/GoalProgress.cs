using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Trading;

namespace AutoFateGrind.Windows.Sections;

internal static class GoalProgress
{
    public readonly record struct Info(float? Fraction, string CenterBig, string CenterSmall, string Remaining, bool Endless);

    public static Info Resolve(Configuration cfg, AutoFateSession? session)
    {
        var completed = session?.CompletedCount ?? 0;

        switch (cfg.ActiveMode.Id)
        {
            case MaxGemstonesMode.ModeId:
            {
                var have = session?.GemstoneCurrent ?? GemstoneCatalog.CurrentWalletCount();
                var target = Math.Max(1, cfg.TargetGemstoneCount);
                var left = Math.Max(0, target - have);
                return new Info(
                    Math.Clamp(have / (float)target, 0f, 1f),
                    have.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, target),
                    left > 0 ? Loc.T(L.Run.GemsToGo, left) : Loc.T(L.Run.TargetReached), false);
            }
            case RunCountMode.ModeId:
            {
                var target = Math.Max(1, cfg.TargetFateCount);
                var left = Math.Max(0, target - completed);
                return new Info(
                    Math.Clamp(completed / (float)target, 0f, 1f),
                    completed.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, target),
                    left > 0 ? Loc.T(L.Run.FatesLeft, left) : Loc.T(L.Run.TargetReached), false);
            }
            case TimeBoxedMode.ModeId:
            {
                var targetMinutes = Math.Max(1, cfg.TargetMinutes);
                var elapsed = session?.Elapsed ?? TimeSpan.Zero;
                var remaining = TimeSpan.FromMinutes(targetMinutes) - elapsed;
                var remainingText = remaining > TimeSpan.Zero
                    ? remaining.TotalHours >= 1
                        ? Loc.T(L.Run.HoursLeft, (int)remaining.TotalHours, remaining.Minutes)
                        : Loc.T(L.Run.MinutesLeft, remaining.Minutes, remaining.Seconds)
                    : Loc.T(L.Run.TimeReached);
                return new Info(
                    Math.Clamp((float)(elapsed.TotalMinutes / targetMinutes), 0f, 1f),
                    Loc.T(L.Run.GoalMinutes, (int)elapsed.TotalMinutes), Loc.T(L.Run.GoalOfMinutes, targetMinutes),
                    remainingText, false);
            }
            default:
                return new Info(null, completed.ToString(Loc.Culture), Loc.T(L.Run.Done), Loc.T(L.Run.UntilYouStop), true);
        }
    }
}
