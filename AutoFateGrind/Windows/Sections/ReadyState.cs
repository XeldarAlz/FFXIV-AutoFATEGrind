using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class ReadyState
{
    public enum Kind { SetupNeeded, PickZones, Ready, Running, Paused }

    public readonly record struct Info(Kind Kind, Vector4 Accent, Vector4 AccentSoft, FontAwesomeIcon Icon, string Title, string Detail);

    private static int cachedFrame = -1;
    private static Info cached;

    public static Info Resolve(Configuration cfg, AutoFateController ctrl)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == cachedFrame) return cached;

        cached = Compute(cfg, ctrl);
        cachedFrame = frame;
        return cached;
    }

    private static Info Compute(Configuration cfg, AutoFateController ctrl)
    {
        if (ctrl.Running)
        {
            if (ctrl.Paused)
            {
                var detail = ctrl.PauseReason == PauseReason.InContent
                    ? Loc.T(L.Grind.DetailPausedInContent)
                    : Loc.T(L.Grind.DetailPausedManual);
                return new Info(Kind.Paused, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Pause, Loc.T(L.Grind.TitlePaused), detail);
            }

            return new Info(Kind.Running, Styling.AccentBlue, Styling.AccentBlueSoft, FontAwesomeIcon.Bolt, Loc.T(L.Grind.TitleRunning), PhaseLabel(ctrl.Phase));
        }

        if (!ExternalPlugins.AllRequiredInstalled())
        {
            return new Info(Kind.SetupNeeded, Styling.AccentRose, Styling.AccentRoseSoft, FontAwesomeIcon.ExclamationTriangle,
                Loc.T(L.Grind.TitleSetupNeeded), Loc.T(L.Grind.DetailSetupNeeded));
        }

        var zones = ZoneSelection.ResolveStartList(cfg).Count;
        if (zones == 0)
        {
            return new Info(Kind.PickZones, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.MapMarkedAlt,
                Loc.T(L.Grind.TitlePickZones), Loc.T(L.Grind.DetailPickZones));
        }

        return new Info(Kind.Ready, Styling.AccentMint, Styling.AccentMintSoft, FontAwesomeIcon.CheckCircle,
            Loc.T(L.Grind.TitleReady), Loc.T(L.Grind.DetailReady));
    }

    public static string ShortLabel(Kind kind) => kind switch
    {
        Kind.Running     => Loc.T(L.Shell.StatusRunning),
        Kind.Paused      => Loc.T(L.Shell.StatusPaused),
        Kind.Ready       => Loc.T(L.Shell.StatusReady),
        Kind.PickZones   => Loc.T(L.Shell.StatusPickZones),
        Kind.SetupNeeded => Loc.T(L.Shell.StatusSetupNeeded),
        _                => Loc.T(L.Shell.StatusIdle),
    };

    public static string PhaseLabel(AutoPhase phase) => phase switch
    {
        AutoPhase.Trading    => Loc.T(L.Run.PhaseTrading),
        AutoPhase.Repairing  => Loc.T(L.Run.PhaseRepairing),
        AutoPhase.Humanizing => Loc.T(L.Run.PhaseBreak),
        AutoPhase.Finishing  => Loc.T(L.Run.PhaseFinishing),
        AutoPhase.Grinding   => Loc.T(L.Run.PhaseGrinding),
        _                    => Loc.T(L.Run.PhaseStandingBy),
    };

    public static string StopSummary(Configuration cfg) => cfg.ActiveMode.Id switch
    {
        MaxGemstonesMode.ModeId => Loc.T(L.Grind.StopsAtGems, cfg.TargetGemstoneCount),
        RunCountMode.ModeId     => Loc.T(L.Grind.StopsAfterFates, cfg.TargetFateCount),
        TimeBoxedMode.ModeId    => Loc.T(L.Grind.StopsAfterMinutes, cfg.TargetMinutes),
        _                       => Loc.T(L.Grind.StopsWhenYouStop),
    };
}
