namespace AutoFateGrind.Windows;

// Shared display formatters so the running panel, live popout, and history window render numbers and
// durations identically. Previously each window carried its own copy and they could drift.
internal static class Formatting
{
    public static string Exp(long exp)
    {
        if (exp >= 1_000_000) return $"{exp / 1_000_000.0:0.0}M";
        if (exp >= 1_000) return $"{exp / 1_000.0:0.0}K";
        return exp.ToString();
    }

    public static string Elapsed(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:D2}m";
        return $"{t.Minutes}m {t.Seconds:D2}s";
    }

    public static string Time(float secs)
    {
        if (secs <= 0) return "--:--";
        var s = (int)secs;
        return $"{s / 60}:{s % 60:D2}";
    }
}
