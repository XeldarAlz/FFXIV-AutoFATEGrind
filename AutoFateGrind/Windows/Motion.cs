using Dalamud.Bindings.ImGui;

namespace AutoFateGrind.Windows;

internal static class Motion
{
    private static readonly Dictionary<int, float> values = new();

    public static bool Reduced => Plugin.PluginInterface.UiBuilder.ShouldUseReducedMotion;

    public static float DeltaTime => MathF.Min(ImGui.GetIO().DeltaTime, 0.05f);

    public static int Key(string id) => unchecked((int)ImGui.GetID(id));

    public static int Key(string id, int salt) => HashCode.Combine(ImGui.GetID(id), salt);

    public static int Key(string id, uint salt) => HashCode.Combine(ImGui.GetID(id), salt);

    public static float Approach(int key, float target, float speed = 14f)
    {
        if (Reduced || !values.TryGetValue(key, out var current))
        {
            values[key] = target;
            return target;
        }

        var next = current + (target - current) * (1f - MathF.Exp(-speed * DeltaTime));
        if (MathF.Abs(next - target) < 0.0005f)
        {
            next = target;
        }

        values[key] = next;
        return next;
    }

    public static float Hover(int key, bool hovered) => Approach(key, hovered ? 1f : 0f, 18f);

    public static float Reveal(long startedTick, float durationMs, float delayMs = 0f)
    {
        if (Reduced) return 1f;
        var elapsed = Environment.TickCount64 - startedTick - delayMs;
        return EaseOutCubic(Math.Clamp(elapsed / durationMs, 0f, 1f));
    }

    public static float EaseOutCubic(float t)
    {
        var u = 1f - t;
        return 1f - u * u * u;
    }

    public static float EaseInOutCubic(float t)
        => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) * 0.5f;

    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    public static float Smoothstep(float t) => t * t * (3f - 2f * t);

    public static float Wave(double periodMs) => MathF.Sin(Styling.Phase(periodMs) * MathF.PI * 2f);
}
