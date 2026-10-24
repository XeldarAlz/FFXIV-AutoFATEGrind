using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Localization;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class GeneralSettings
{
    public static void Draw(Configuration cfg)
    {
        DrawLanguageGroup(cfg);
        DrawWindowGroup(cfg);
        DrawRunGroup(cfg);
        PostRunSettings.Draw(cfg);
    }

    private static void DrawLanguageGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.Language)))
        {
            SettingsControls.LanguageRow(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawWindowGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.GeneralWindow)))
        {
            DrawWindowRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawWindowRows(Configuration cfg)
    {
        var openOnLogin = cfg.AutoShowOnLogin;
        if (SettingsControls.ToggleRow(L.Settings.OpenOnLogin, L.Settings.OpenOnLoginHelp, ref openOnLogin))
        {
            cfg.AutoShowOnLogin = openOnLogin;
            cfg.SaveDebounced();
        }

        var dragAnywhere = cfg.DragWindowFromAnywhere;
        if (SettingsControls.ToggleRow(L.Settings.DragAnywhere, L.Settings.DragAnywhereHelp, ref dragAnywhere))
        {
            cfg.DragWindowFromAnywhere = dragAnywhere;
            cfg.SaveDebounced();
        }

        var livePopout = cfg.ShowLivePopout;
        if (SettingsControls.ToggleRow(L.Settings.LivePopout, L.Settings.LivePopoutHelp, ref livePopout))
        {
            cfg.ShowLivePopout = livePopout;
            Plugin.Instance.LiveFateWindow.IsOpen = livePopout;
            cfg.SaveDebounced();
        }

        var nameFormat = cfg.FateNameFormat;
        if (SettingsControls.TextFieldRow(L.Settings.NameFormat, L.Settings.NameFormatHelp, ref nameFormat, FateNameFormatter.MaxFormatLength))
        {
            cfg.FateNameFormat = nameFormat;
            cfg.SaveDebounced();
        }
    }

    private static void DrawRunGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.GeneralRun)))
        {
            DrawRunRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawRunRows(Configuration cfg)
    {
        var autoPause = cfg.AutoPauseInContent;
        if (SettingsControls.ToggleRow(L.Settings.AutoPause, L.Settings.AutoPauseHelp, ref autoPause))
        {
            cfg.AutoPauseInContent = autoPause;
            cfg.SaveDebounced();
        }

        var autoResume = cfg.AutoResumeOnFault;
        if (SettingsControls.ToggleRow(L.Settings.AutoResume, L.Settings.AutoResumeHelp, ref autoResume))
        {
            cfg.AutoResumeOnFault = autoResume;
            cfg.SaveDebounced();
        }

        var stuckRescue = cfg.StuckRescueEnabled;
        if (SettingsControls.ToggleRow(L.Settings.StuckRescue, L.Settings.StuckRescueHelp, ref stuckRescue))
        {
            cfg.StuckRescueEnabled = stuckRescue;
            cfg.SaveDebounced();
        }

        if (!cfg.StuckRescueEnabled)
        {
            return;
        }

        var minutes = cfg.StuckRescueMinutes;
        if (SettingsControls.SliderRow(L.Settings.StuckRescueAfter, L.Settings.StuckRescueAfterHelp, ref minutes,
                Configuration.StuckRescueMinMinutes, Configuration.StuckRescueMaxMinutes, Loc.T(L.Settings.MinutesFormat)))
        {
            cfg.StuckRescueMinutes = minutes;
            cfg.SaveDebounced();
        }
    }
}
