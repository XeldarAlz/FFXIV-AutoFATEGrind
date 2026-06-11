using Dalamud.Interface;

namespace AutoFateGrind.Windows.Components;

// Running-flow hero: stop glyph + STOP + a right-aligned sublabel ("running · 18:42"). Mirrors StartButton
// so the idle and running views read as one family — same shape, rose accent, always enabled.
internal static class StopButton
{
    public static bool Draw(string? sublabel)
        => HeroButton.Draw(FontAwesomeIcon.Stop, "STOP", sublabel, Styling.AccentRose, true);
}
