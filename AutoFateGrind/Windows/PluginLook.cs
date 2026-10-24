using Glasswork;

namespace AutoFateGrind.Windows;

internal static class PluginLook
{
    public static Look Create()
    {
        var look = Look.NeutralDark();
        look.Accent = Color.FromVector4(Styling.AccentViolet);
        look.Link = look.Accent;
        look.FocusRing = look.Accent.WithAlpha(look.FocusRing.A);
        return look;
    }
}
