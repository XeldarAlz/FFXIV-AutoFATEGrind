using AutoFateGrind.Core.Trading;

namespace AutoFateGrind.Core.Modes;

public sealed class EndlessMode : IFateGrindMode
{
    public string Id => "endless";
    public string DisplayName => "Endless Grind";
    public string Description => "Runs forever, rotating between selected zones, until you press Stop.";
    public bool IsComplete(ModeContext ctx) => false;
    public string? GetRemainingDisplay(ModeContext ctx) => null;
}

public sealed class MaxFatesMode : IFateGrindMode
{
    public string Id => "maxfates";
    public string DisplayName => "Max Shared FATEs";
    public string Description =>
        "Stops when every selected zone's 'Free Market Friend' achievement (60 Shared FATEs) is complete. " +
        "ShB / EW / DT only — no equivalent exists in earlier expansions.";
    public bool RotatesSharedFateZones => true;
    public bool IsComplete(ModeContext ctx) => ctx.Zones.All(z => z.AchievementDone);

    public string? GetRemainingDisplay(ModeContext ctx)
    {
        var remaining = ctx.Zones.Count(z => !z.AchievementDone);
        return remaining > 0 ? $"{remaining} zone{(remaining == 1 ? "" : "s")} left" : null;
    }
}

public sealed class MaxGemstonesMode : IFateGrindMode
{
    public string Id => "maxgemstones";
    public string DisplayName => "Farm Gemstones";
    public string Description => "Stops when Bicolor Gemstones hit your target. Auto-trade resumes the grind.";
    public bool IsComplete(ModeContext ctx) => GemstoneCatalog.CurrentWalletCount() >= Plugin.Cfg.TargetGemstoneCount;

    public string? GetRemainingDisplay(ModeContext ctx)
    {
        var have = GemstoneCatalog.CurrentWalletCount();
        var target = Plugin.Cfg.TargetGemstoneCount;
        return have < target ? $"{have} / {target} gems" : null;
    }
}

public sealed class RunCountMode : IFateGrindMode
{
    public string Id => "runcount";
    public string DisplayName => "Run N FATEs";
    public string Description => "Stops after a fixed number of FATE completions across all selected zones.";
    public bool IsComplete(ModeContext ctx) => ctx.CompletedCount >= Plugin.Cfg.TargetFateCount;

    public string? GetRemainingDisplay(ModeContext ctx)
    {
        var remaining = Math.Max(0, Plugin.Cfg.TargetFateCount - ctx.CompletedCount);
        return remaining > 0 ? $"{remaining} FATEs left" : null;
    }
}
