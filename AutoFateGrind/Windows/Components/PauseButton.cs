using AutoFateGrind.Core.Tasks;
using Dalamud.Interface;

namespace AutoFateGrind.Windows.Components;

internal static class PauseButton
{
    public static bool Draw(PauseReason reason, float width = 0f) => reason switch
    {
        PauseReason.InContent => HeroButton.Draw(FontAwesomeIcon.Play, "RESUME", null, Styling.AccentMint, false, "in content", width),
        PauseReason.Manual    => HeroButton.Draw(FontAwesomeIcon.Play, "RESUME", null, Styling.AccentMint, true, null, width),
        _                     => HeroButton.Draw(FontAwesomeIcon.Pause, "PAUSE", null, Styling.AccentAmber, true, null, width),
    };
}
