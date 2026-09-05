using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Tasks;
using Dalamud.Interface;

namespace AutoFateGrind.Windows.Components;

internal static class PauseButton
{
    public static bool Draw(PauseReason reason, float width = 0f) => reason switch
    {
        PauseReason.InContent => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Grind.ResumeCaps), null, Styling.AccentMint, false, Loc.T(L.Grind.InContent), width),
        PauseReason.Manual    => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Grind.ResumeCaps), null, Styling.AccentMint, true, null, width),
        _                     => HeroButton.Draw(FontAwesomeIcon.Pause, Loc.T(L.Grind.PauseCaps), null, Styling.AccentAmber, true, null, width),
    };
}
