using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

internal static class ProgressRing
{
    private const float Top = -MathF.PI / 2f;

    private static Vector2 Dir(float a) => new(MathF.Cos(a), MathF.Sin(a));

    private static void Arc(Vector2 c, float r, float thickness, float a0, float a1, uint col)
    {
        var dl = ImGui.GetWindowDrawList();
        var span = MathF.Abs(a1 - a0);
        var segments = Math.Max(2, (int)MathF.Ceiling(span / (MathF.PI / 48f)));
        var prev = c + Dir(a0) * r;
        for (var segment = 1; segment <= segments; segment++)
        {
            var a = a0 + (a1 - a0) * (segment / (float)segments);
            var cur = c + Dir(a) * r;
            dl.AddLine(prev, cur, col, thickness);
            prev = cur;
        }

        var cap = thickness * 0.5f;
        dl.AddCircleFilled(c + Dir(a0) * r, cap, col);
        dl.AddCircleFilled(c + Dir(a1) * r, cap, col);
    }

    public static void Glow(Vector2 c, float radius, Vector4 color, float intensity)
    {
        var dl = ImGui.GetWindowDrawList();
        for (var layer = 4; layer >= 1; layer--)
        {
            var r = radius * (0.72f + layer * 0.17f);
            var a = Math.Clamp(intensity * 0.05f * (5 - layer), 0f, 0.5f);
            dl.AddCircleFilled(c, r, Paint.Col(Styling.WithAlpha(color, a)));
        }
    }

    public static void Disc(Vector2 c, float radius, Vector4 color)
        => ImGui.GetWindowDrawList().AddCircleFilled(c, radius, Paint.Col(color));

    public static void Track(Vector2 c, float r, float thickness, Vector4 col)
        => Arc(c, r, thickness, Top, Top + MathF.PI * 2f, Paint.Col(col));

    public static void Fill(Vector2 c, float r, float thickness, float fraction, Vector4 col)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        if (fraction <= 0.0001f) return;
        Arc(c, r, thickness, Top, Top + fraction * MathF.PI * 2f, Paint.Col(col));
    }

    public static void Sweep(Vector2 c, float r, float thickness, Vector4 col, double periodMs, float arcLen, float headAlpha)
    {
        var dl = ImGui.GetWindowDrawList();
        var head = Top + Styling.Phase(periodMs) * MathF.PI * 2f;
        var tail = head - arcLen;
        var steps = Math.Max(10, (int)MathF.Ceiling(arcLen / (MathF.PI / 36f)));
        var prev = c + Dir(tail) * r;
        for (var step = 1; step <= steps; step++)
        {
            var t = step / (float)steps;
            var a = tail + (head - tail) * t;
            var cur = c + Dir(a) * r;
            dl.AddLine(prev, cur, Paint.Col(Styling.WithAlpha(col, headAlpha * t * t)), thickness);
            prev = cur;
        }

        dl.AddCircleFilled(c + Dir(head) * r, thickness * 0.62f, Paint.Col(Styling.WithAlpha(col, headAlpha)));
    }

    public static void CenterValue(Vector2 c, string big, string? small, Vector4 bigCol, Vector4 smallCol)
    {
        Vector2 bigSize;
        using (Fonts.PushTitle())
            bigSize = TextDraw.Measure(big);

        var hasSmall = !string.IsNullOrEmpty(small);
        var smallSize = Vector2.Zero;
        if (hasSmall)
        {
            using (Fonts.PushCaption())
                smallSize = TextDraw.Measure(small!);
        }

        var gap = hasSmall ? 1f * ImGuiHelpers.GlobalScale : 0f;
        var top = c.Y - (bigSize.Y + gap + smallSize.Y) * 0.5f;

        using (Fonts.PushTitle())
            TextDraw.At(big, new Vector2(c.X - bigSize.X * 0.5f, top), bigCol);

        if (!hasSmall) return;
        using (Fonts.PushCaption())
            TextDraw.At(small!, new Vector2(c.X - smallSize.X * 0.5f, top + bigSize.Y + gap), smallCol);
    }

    public static void CenterIcon(Vector2 c, FontAwesomeIcon icon, Vector4 col, float targetHeight)
    {
        var glyph = icon.ToIconString();
        using var font = Fonts.PushIconFor(targetHeight / ImGuiHelpers.GlobalScale);
        var baseHeight = ImGui.CalcTextSize(glyph).Y;
        var scale = baseHeight > 0f ? targetHeight / baseHeight : 1f;
        ImGui.SetWindowFontScale(scale);
        var size = ImGui.CalcTextSize(glyph);
        ImGui.GetWindowDrawList().AddText(c - size * 0.5f, Paint.Col(col), glyph);
        ImGui.SetWindowFontScale(1f);
    }
}
