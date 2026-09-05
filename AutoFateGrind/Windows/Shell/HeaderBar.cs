using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using AutoFateGrind.Windows.Sections;
using clib.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;

namespace AutoFateGrind.Windows.Shell;

internal static class HeaderBar
{
    private const string Title = "Auto FATE Grind";
    private const float PadX = 16f;
    private const float IconBox = 26f;
    private const float ButtonSize = 30f;
    private const float ButtonGap = 6f;
    private const int ButtonCount = 3;
    private const float CompactBarWidth = 90f;

    public const float MinimumWidth = PadX * 2f + IconBox + 12f + ButtonSize * ButtonCount + ButtonGap * (ButtonCount - 1);

    public static float ButtonsWidth() => (ButtonSize * ButtonCount + ButtonGap * (ButtonCount - 1)) * ImGuiHelpers.GlobalScale;

    public static bool HandleDrag(Vector2 windowPos, float width, float height)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dragWidth = width - PadX * scale - ButtonsWidth() - 8f * scale;
        ImGui.SetCursorScreenPos(windowPos);
        ImGui.InvisibleButton("##afg_drag", new Vector2(MathF.Max(1f, dragWidth), height));
        var doubleClicked = ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        if (!ImGui.IsItemActive()) return doubleClicked;

        var delta = ImGui.GetIO().MouseDelta;
        if (delta != Vector2.Zero) ImGui.SetWindowPos(ImGui.GetWindowPos() + delta, ImGuiCond.Always);
        return doubleClicked;
    }

    public static void Draw(AppWindow window, Plugin plugin, Vector2 origin, float width, float height, float windowRounding, bool compact)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var end = origin + new Vector2(width, height);
        var padX = PadX * scale;
        var midY = origin.Y + height * 0.5f;

        Paint.Fill(dl, origin, end, Styling.WithAlpha(Styling.Surface1, 0.40f), windowRounding,
            compact ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersTop);
        if (!compact) Paint.Hairline(dl, new Vector2(origin.X, end.Y - 0.5f), new Vector2(end.X, end.Y - 0.5f));

        var iconBox = IconBox * scale;
        var iconMin = new Vector2(origin.X + padX, midY - iconBox * 0.5f);
        AppIcon.Draw(dl, iconMin, iconMin + new Vector2(iconBox, iconBox), 7f * scale);

        var buttonsLeft = end.X - padX - ButtonsWidth();
        var x = iconMin.X + iconBox + 12f * scale;
        using (Fonts.PushHeadline())
        {
            var titleSize = TextDraw.Measure(Title);
            if (x + titleSize.X <= buttonsLeft)
            {
                TextDraw.At(Title, new Vector2(x, midY - titleSize.Y * 0.5f), Styling.TextStrong);
                x += titleSize.X + 14f * scale;
            }
        }

        var info = ReadyState.Resolve(plugin.Configuration, plugin.Controller);
        var pillEnd = DrawStatusPill(dl, info, x, buttonsLeft, midY);
        if (pillEnd > x) x = pillEnd + 14f * scale;

        if (compact) DrawCompactInfo(plugin, info, x, buttonsLeft - 14f * scale, midY);

        DrawButtons(window, plugin, end, midY, compact);
    }

    private static void DrawButtons(AppWindow window, Plugin plugin, Vector2 end, float midY, bool compact)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padX = PadX * scale;
        var buttonSize = ButtonSize * scale;
        var stride = buttonSize + ButtonGap * scale;
        var top = midY - buttonSize * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(end.X - padX - buttonSize, top));
        if (IconButton.Draw(FontAwesomeIcon.Times, "##afg_close", buttonSize, tooltip: Loc.T(L.Common.Close)))
        {
            window.IsOpen = false;
        }

        ImGui.SetCursorScreenPos(new Vector2(end.X - padX - buttonSize - stride, top));
        if (IconButton.Draw(compact ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown, "##afg_minimize", buttonSize,
                tooltip: compact ? Loc.T(L.Shell.Restore) : Loc.T(L.Shell.Minimize)))
        {
            window.ToggleCompact();
        }

        var cfg = plugin.Configuration;
        var popout = cfg.ShowLivePopout;
        ImGui.SetCursorScreenPos(new Vector2(end.X - padX - buttonSize - stride * 2f, top));
        if (IconButton.Draw(FontAwesomeIcon.ExternalLinkAlt, "##afg_popout", buttonSize,
                popout ? Styling.AccentVioletSoft : null, popout ? Loc.T(L.Shell.HideLiveTracker) : Loc.T(L.Shell.ShowLiveTracker)))
        {
            cfg.ShowLivePopout = !popout;
            plugin.LiveFateWindow.IsOpen = cfg.ShowLivePopout;
            cfg.Save();
        }
    }

    private static float DrawStatusPill(ImDrawListPtr dl, ReadyState.Info info, float x, float rightLimit, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var label = ReadyState.ShortLabel(info.Kind);
        var padX = 10f * scale;
        var dotRadius = 3.5f * scale;

        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.Measure(label);
            var pillHeight = labelSize.Y + 8f * scale;
            var pillMin = new Vector2(x, midY - pillHeight * 0.5f);
            var pillMax = pillMin + new Vector2(padX * 2f + dotRadius * 2f + 6f * scale + labelSize.X, pillHeight);
            if (pillMax.X > rightLimit) return x;

            Paint.Pill(dl, pillMin, pillMax, Styling.WithAlpha(info.Accent, 0.16f), Styling.WithAlpha(info.Accent, 0.45f));

            var animated = info.Kind is ReadyState.Kind.Running;
            var dotColor = animated ? Styling.PulseColor(info.Accent, info.AccentSoft, Styling.PulseMedium) : info.Accent;
            dl.AddCircleFilled(new Vector2(pillMin.X + padX + dotRadius, midY), dotRadius, Paint.Col(dotColor));
            TextDraw.At(label, new Vector2(pillMin.X + padX + dotRadius * 2f + 6f * scale, midY - labelSize.Y * 0.5f), info.AccentSoft);
            return pillMax.X;
        }
    }

    private static void DrawCompactInfo(Plugin plugin, ReadyState.Info info, float x, float rightX, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var ctrl = plugin.Controller;
        var cfg = plugin.Configuration;
        if (rightX - x < CompactBarWidth * scale) return;

        if (!ctrl.Running)
        {
            var zones = ZoneSelection.ResolveStartList(cfg).Count;
            if (zones == 0) return;
            var plan = Loc.T(L.Grind.StartSub, Loc.Plural(L.Grind.ZonesCount, zones), ReadyState.StopSummary(cfg));
            using (Fonts.PushCaption())
            {
                var planSize = TextDraw.Measure(plan);
                TextDraw.At(TextDraw.Truncate(plan, rightX - x), new Vector2(x, midY - planSize.Y * 0.5f), Styling.TextDim);
            }

            return;
        }

        var fate = PublicEvent.CurrentFate;
        var inFate = fate is not null && fate.State == FateState.Running && !ctrl.Paused;
        var barWidth = CompactBarWidth * scale;
        var barX = rightX - barWidth;
        var barHeight = 6f * scale;
        var barOrigin = new Vector2(barX, midY - barHeight * 0.5f);
        if (inFate) Paint.Bar(dl, barOrigin, barWidth, barHeight, fate!.Progress / 100f, info.Accent);
        else if (ctrl.Paused) Paint.Bar(dl, barOrigin, barWidth, barHeight, 0f, info.Accent);
        else Paint.IndeterminateBar(dl, barOrigin, barWidth, barHeight, info.Accent);

        var textWidth = barX - 12f * scale - x;
        if (textWidth <= 0f) return;

        var phase = ctrl.Paused ? Loc.T(L.Run.PhasePaused) : ReadyState.PhaseLabel(ctrl.Phase);
        var text = inFate ? $"{phase}  ·  L{fate!.Level} {fate.Name}" : $"{phase}  ·  {ctrl.Status}";
        using (Fonts.PushCaption())
        {
            var textSize = TextDraw.Measure(text);
            TextDraw.At(TextDraw.Truncate(text, textWidth), new Vector2(x, midY - textSize.Y * 0.5f), Styling.TextSecondary);
        }
    }
}
