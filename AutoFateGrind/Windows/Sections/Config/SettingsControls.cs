using AutoFateGrind.Windows.Components;

namespace AutoFateGrind.Windows.Sections.Config;

// Shared widgets used across the config tabs, so each tab stays focused on its own settings rather than
// re-declaring the same toggle plumbing.
internal static class SettingsControls
{
    public static void DrawToggle(Configuration cfg, Func<bool> getter, Action<bool> setter, string id)
    {
        var v = getter();
        if (ToggleSwitch.Draw(id, ref v))
        {
            setter(v);
            cfg.SaveDebounced();
        }
    }
}
