using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Game.Player;
using AutoFateGrind.Core.Localization;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class TravelSettings
{
    private const int MountListRefreshMs = 2000;
    private const int SwapWaitMinSec = 0;
    private const int SwapWaitMaxSec = 300;
    private const int CollectHandInBatchMin = 1;
    // Nine keeps AFG's trip ahead of BossMod's fixed 10-item hand-in, so the two never race for the NPC.
    private const int CollectHandInBatchMax = 9;
    // Tank ranged pulls reach 20y; much past that the walk to a far mob drags the pack apart.
    private const int PullReachMin = 10;
    private const int PullReachMax = 40;
    private const int PullMaxMobsMin = 3;
    private const int PullMaxMobsMax = FateMobScanner.EnmityListCapacity;
    private const int PullHpMin = 10;
    private const int PullHpMax = 100;
    private const string PercentFormat = "%d%%";

    private static readonly SearchablePicker mountPicker = new(false);
    private static MountOption[] mountOptions = [];
    private static string[] mountLabels = [];
    private static long mountListBuiltAtMs;

    public static void Draw(Configuration cfg)
    {
        DrawMountGroup(cfg);
        DrawZoneSwapGroup(cfg);
        DrawFatePlayGroup(cfg);
    }

    private static void DrawMountGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.TravelMount)))
        {
            DrawMountRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawMountRows(Configuration cfg)
    {
        DrawMountPicker(cfg);

        var mountWhileWaiting = cfg.MountWhileWaitingForFates;
        if (SettingsControls.ToggleRow(L.Settings.MountWhileWaiting, L.Settings.MountWhileWaitingHelp, ref mountWhileWaiting))
        {
            cfg.MountWhileWaitingForFates = mountWhileWaiting;
            cfg.SaveDebounced();
        }

        var economyTravel = cfg.EconomyTravel;
        if (SettingsControls.ToggleRow(L.Settings.EconomyTravel, L.Settings.EconomyTravelHelp, ref economyTravel))
        {
            cfg.EconomyTravel = economyTravel;
            cfg.SaveDebounced();
        }
    }

    private static void DrawMountPicker(Configuration cfg)
    {
        RefreshMountList();
        var selected = 0;
        for (var index = 0; index < mountOptions.Length; index++)
        {
            if (mountOptions[index].Id == cfg.PreferredMountId)
            {
                selected = index + 1;
                break;
            }
        }

        if (mountPicker.Draw(L.Settings.Mount, L.Settings.MountHelp, mountLabels, ref selected))
        {
            cfg.PreferredMountId = selected == 0 ? MountOps.Roulette : mountOptions[selected - 1].Id;
            cfg.SaveDebounced();
        }

        if (cfg.PreferredMountId != MountOps.Roulette && !MountOps.IsUsable(cfg.PreferredMountId))
        {
            SettingsControls.Note(Loc.T(L.Settings.MountNotOwned, MountOps.NameOf(cfg.PreferredMountId)), Frame.Look.Destructive);
        }
    }

    // Rebuilt on a timer so a mount unlocked or a language switched while the page is open shows up.
    private static void RefreshMountList()
    {
        var now = Environment.TickCount64;
        if (mountLabels.Length > 0 && now - mountListBuiltAtMs < MountListRefreshMs)
        {
            return;
        }

        mountListBuiltAtMs = now;
        mountOptions = MountOps.OwnedMounts();
        var labels = new string[mountOptions.Length + 1];
        labels[0] = Loc.T(L.Settings.MountRandom);
        for (var index = 0; index < mountOptions.Length; index++)
        {
            labels[index + 1] = mountOptions[index].Name;
        }

        mountLabels = labels;
    }

    private static void DrawZoneSwapGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.TravelZoneSwap)))
        {
            DrawZoneSwapRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawZoneSwapRows(Configuration cfg)
    {
        var swapZones = cfg.SwapZonesWhenEmpty;
        if (SettingsControls.ToggleRow(L.Settings.SwapZones, L.Settings.SwapZonesHelp, ref swapZones))
        {
            cfg.SwapZonesWhenEmpty = swapZones;
            cfg.SaveDebounced();
        }

        if (!cfg.SwapZonesWhenEmpty)
        {
            return;
        }

        var waitSeconds = cfg.SwapZoneWaitSec;
        if (SettingsControls.SliderRow(L.Settings.SwapWait, L.Settings.SwapWaitHelp, ref waitSeconds, SwapWaitMinSec, SwapWaitMaxSec, Loc.T(L.Settings.MinTimeFormat)))
        {
            cfg.SwapZoneWaitSec = waitSeconds;
            cfg.SaveDebounced();
        }
    }

    private static void DrawFatePlayGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.TravelFatePlay)))
        {
            DrawFatePlayRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawFatePlayRows(Configuration cfg)
    {
        var keepTwist = cfg.KeepTwistOfFate;
        if (SettingsControls.ToggleRow(L.Settings.KeepTwist, L.Settings.KeepTwistHelp, ref keepTwist))
        {
            cfg.KeepTwistOfFate = keepTwist;
            cfg.SaveDebounced();
        }

        var pullMany = cfg.PullAsManyAsPossible;
        if (SettingsControls.ToggleRow(L.Settings.PullMany, L.Settings.PullManyHelp, ref pullMany))
        {
            cfg.PullAsManyAsPossible = pullMany;
            cfg.SaveDebounced();
        }

        if (cfg.PullAsManyAsPossible)
        {
            DrawPullTuning(cfg);
        }

        var collectHandIn = cfg.CollectHandInEnabled;
        if (SettingsControls.ToggleRow(L.Settings.CollectHandIn, L.Settings.CollectHandInHelp, ref collectHandIn))
        {
            cfg.CollectHandInEnabled = collectHandIn;
            cfg.SaveDebounced();
        }

        if (!cfg.CollectHandInEnabled)
        {
            return;
        }

        var batch = cfg.CollectHandInBatch;
        if (SettingsControls.SliderRow(L.Settings.CollectHandInBatch, L.Settings.CollectHandInBatchHelp, ref batch,
                CollectHandInBatchMin, CollectHandInBatchMax, Loc.T(L.Settings.CollectHandInBatchFormat)))
        {
            cfg.CollectHandInBatch = batch;
            cfg.SaveDebounced();
        }
    }

    private static void DrawPullTuning(Configuration cfg)
    {
        var reach = cfg.PullReachMeters;
        if (SettingsControls.SliderRow(L.Settings.PullReach, L.Settings.PullReachHelp, ref reach, PullReachMin, PullReachMax, Loc.T(L.Settings.MetersFormat)))
        {
            cfg.PullReachMeters = reach;
            cfg.SaveDebounced();
        }

        var maxMobs = cfg.PullMaxMobs;
        if (SettingsControls.SliderRow(L.Settings.PullMaxMobs, L.Settings.PullMaxMobsHelp, ref maxMobs, PullMaxMobsMin, PullMaxMobsMax, Loc.T(L.Settings.PullMaxMobsFormat)))
        {
            cfg.PullMaxMobs = maxMobs;
            cfg.SaveDebounced();
        }

        var stopHp = cfg.PullStopHpPercent;
        if (SettingsControls.SliderRow(L.Settings.PullStopHp, L.Settings.PullStopHpHelp, ref stopHp, PullHpMin, PullHpMax, PercentFormat))
        {
            cfg.PullStopHpPercent = stopHp;
            cfg.PullResumeHpPercent = Math.Max(cfg.PullResumeHpPercent, stopHp);
            cfg.SaveDebounced();
        }

        var resumeHp = cfg.PullResumeHpPercent;
        if (SettingsControls.SliderRow(L.Settings.PullResumeHp, L.Settings.PullResumeHpHelp, ref resumeHp, PullHpMin, PullHpMax, PercentFormat))
        {
            cfg.PullResumeHpPercent = resumeHp;
            cfg.PullStopHpPercent = Math.Min(cfg.PullStopHpPercent, resumeHp);
            cfg.SaveDebounced();
        }
    }
}
