using AutoFateGrind.Windows.Components;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class GeneralSettings
{
    public static void Draw(Configuration cfg)
    {
        DrawWindowGroup(cfg);
        DrawBehaviorGroup(cfg);
    }

    private static void DrawWindowGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Window");

        SettingsRow.Draw("Open on login",
            "Pop the main window automatically the next time you log in.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoShowOnLogin, v => cfg.AutoShowOnLogin = v, "##gen_autoshow"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw("Live FATE tracker popout",
            "Show the live FATE tracker as a small overlay window so you can keep it visible while the main window is closed.",
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
        using var group = SettingsGroup.Begin("Behavior");

        SettingsRow.Draw("Swap zones when empty",
            "When the current zone runs out of eligible FATEs, jump to the next zone in your priority order.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.SwapZonesWhenEmpty, v => cfg.SwapZonesWhenEmpty = v, "##gen_swap"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw("Auto-resume on fault",
            "If the grind hits an unrecoverable error and stops, automatically restart it (up to 3 times in 5 minutes) instead of ending the run. Leave off if you want faults to surface.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoResumeOnFault, v => cfg.AutoResumeOnFault = v, "##gen_autoresume"),
            SettingsRow.ToggleHeight);
    }
}
