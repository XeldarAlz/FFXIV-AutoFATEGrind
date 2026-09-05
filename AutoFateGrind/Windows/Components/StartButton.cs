using AutoFateGrind.Core.Localization;
using Dalamud.Interface;

namespace AutoFateGrind.Windows.Components;

internal static class StartButton
{
    public static bool Draw(string sublabel, bool enabled, string? disabledReason = null, float width = 0f)
        => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Grind.Start), sublabel, Styling.AccentViolet, enabled, disabledReason, width);
}
