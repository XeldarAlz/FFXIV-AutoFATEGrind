using Dalamud.Interface;

namespace AutoFateGrind.Windows.Components;

// Idle-flow hero: play glyph + START + a right-aligned sublabel ("3 zones" when ready, the blocking reason
// when not). A thin violet wrapper over HeroButton so it stays visually paired with the running STOP hero.
internal static class StartButton
{
    public static bool Draw(string sublabel, bool enabled, string? disabledReason = null)
        => HeroButton.Draw(FontAwesomeIcon.Play, "START", sublabel, Styling.AccentViolet, enabled, disabledReason);
}
