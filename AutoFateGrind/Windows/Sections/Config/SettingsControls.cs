using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections.Config;

// Shared widgets used across the config tabs. Kept here so each tab section stays focused on its own
// settings rather than re-declaring the same toggle/icon-button plumbing.
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

    public static bool DrawIconButton(FontAwesomeIcon icon, string id, float size)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGui.Button(icon.ToIconString() + id, new Vector2(size, size));
    }
}
