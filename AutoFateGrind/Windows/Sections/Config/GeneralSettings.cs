using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class GeneralSettings
{
    public static void Draw(Configuration cfg)
    {
        DrawLanguageGroup(cfg);
        DrawWindowGroup(cfg);
        DrawBehaviorGroup(cfg);
        DrawCollectGroup(cfg);
    }

    private static void DrawLanguageGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.Language));

        SettingsRow.Draw(Loc.T(L.Settings.Language),
            Loc.T(L.Settings.LanguageHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.DrawLanguageCombo(cfg));
    }

    private static void DrawWindowGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralWindow));

        SettingsRow.Draw(Loc.T(L.Settings.OpenOnLogin),
            Loc.T(L.Settings.OpenOnLoginHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoShowOnLogin, v => cfg.AutoShowOnLogin = v, "##gen_autoshow"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.LivePopout),
            Loc.T(L.Settings.LivePopoutHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.ShowLivePopout, v =>
            {
                cfg.ShowLivePopout = v;
                Plugin.Instance.LiveFateWindow.IsOpen = v;
            }, "##gen_popout"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawBehaviorGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralBehavior));

        SettingsRow.Draw(Loc.T(L.Settings.SwapZones),
            Loc.T(L.Settings.SwapZonesHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.SwapZonesWhenEmpty, v => cfg.SwapZonesWhenEmpty = v, "##gen_swap"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.MountWhileWaiting),
            Loc.T(L.Settings.MountWhileWaitingHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.MountWhileWaitingForFates, v => cfg.MountWhileWaitingForFates = v, "##gen_mount_waiting"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.AutoPause),
            Loc.T(L.Settings.AutoPauseHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoPauseInContent, v => cfg.AutoPauseInContent = v, "##gen_autopause"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.AutoResume),
            Loc.T(L.Settings.AutoResumeHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoResumeOnFault, v => cfg.AutoResumeOnFault = v, "##gen_autoresume"),
            SettingsRow.ToggleHeight);
    }

    private const int CollectHandInBatchMin = 1;
    // Nine keeps AFG's trip ahead of BossMod's fixed 10-item hand-in, so the two never race for the NPC.
    private const int CollectHandInBatchMax = 9;

    private static void DrawCollectGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralCollect));

        SettingsRow.Draw(Loc.T(L.Settings.CollectHandIn),
            Loc.T(L.Settings.CollectHandInHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.CollectHandInEnabled, v => cfg.CollectHandInEnabled = v, "##gen_collect_handin"),
            SettingsRow.ToggleHeight);

        if (!cfg.CollectHandInEnabled)
        {
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.CollectHandInBatch),
            Loc.T(L.Settings.CollectHandInBatchHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##gen_collect_batch",
                () => cfg.CollectHandInBatch, v => cfg.CollectHandInBatch = v,
                CollectHandInBatchMin, CollectHandInBatchMax, Loc.T(L.Settings.CollectHandInBatchFormat)));
    }
}
