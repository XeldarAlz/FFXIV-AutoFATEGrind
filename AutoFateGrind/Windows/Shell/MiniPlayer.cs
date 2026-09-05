using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Windows.Components;
using AutoFateGrind.Windows.Sections;
using clib.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;

namespace AutoFateGrind.Windows.Shell;

internal static class MiniPlayer
{
    private const float PadX = 18f;
    private const float ButtonSize = 34f;
    private const float ButtonGap = 8f;
    private const float BarWidth = 160f;
    private const float BarHeight = 8f;

    public static bool Draw(Plugin plugin, Vector2 size, float windowRounding)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();
        var cfg = plugin.Configuration;
        var ctrl = plugin.Controller;
        var fate = PublicEvent.CurrentFate;
        var inFate = fate is not null && fate.State == FateState.Running && !ctrl.Paused;
        var info = ReadyState.Resolve(cfg, ctrl);

        Dock.Background(dl, origin, end, windowRounding);

        var padX = PadX * scale;
        var buttonSize = ButtonSize * scale;
        var buttonsWidth = buttonSize * 2f + ButtonGap * scale;
        ImGui.SetCursorScreenPos(origin);
        var hit = Hit.Area("##afg_mini_open", new Vector2(size.X - padX - buttonsWidth - 8f * scale, size.Y));
        var hover = Motion.Hover(Motion.Key("##afg_mini_open"), hit.Hovered);
        if (hover > 0.01f)
        {
            Paint.Fill(dl, origin, end, Styling.WithAlpha(Styling.Surface2, 0.35f * hover), windowRounding, ImDrawFlags.RoundCornersBottom);
        }

        var midY = origin.Y + size.Y * 0.5f;
        var dotColor = ctrl.Paused ? info.Accent : Styling.PulseColor(info.Accent, info.AccentSoft, Styling.PulseMedium);
        Paint.Dot(dl, new Vector2(origin.X + padX + 4f * scale, midY), 4f * scale, dotColor);

        var barWidth = BarWidth * scale;
        var barRight = end.X - padX - buttonsWidth - 16f * scale;
        var barX = barRight - barWidth;
        var barY = midY - BarHeight * scale * 0.5f;
        if (inFate) Paint.Bar(dl, new Vector2(barX, barY), barWidth, BarHeight * scale, fate!.Progress / 100f, info.Accent);
        else if (ctrl.Paused) Paint.Bar(dl, new Vector2(barX, barY), barWidth, BarHeight * scale, 0f, info.Accent);
        else Paint.IndeterminateBar(dl, new Vector2(barX, barY), barWidth, BarHeight * scale, info.Accent);

        var textX = origin.X + padX + 22f * scale;
        var phase = ctrl.Paused ? Loc.T(L.Run.PhasePaused) : ReadyState.PhaseLabel(ctrl.Phase);
        var phaseSize = TextDraw.SmallCapsSize(phase);
        var lineHeight = ImGui.GetTextLineHeight();
        var gap = 2f * scale;
        var top = midY - (phaseSize.Y + gap + lineHeight) * 0.5f;
        TextDraw.SmallCaps(phase, new Vector2(textX, top), info.AccentSoft);

        var main = inFate ? $"L{fate!.Level}   {fate.Name}" : ctrl.Status;
        TextDraw.At(TextDraw.Truncate(main, barX - 16f * scale - textX), new Vector2(textX, top + phaseSize.Y + gap), Styling.TextStrong);

        var resumeBlocked = ctrl.Paused && ctrl.PauseReason == PauseReason.InContent;
        ImGui.SetCursorScreenPos(new Vector2(end.X - padX - buttonSize * 2f - ButtonGap * scale, midY - buttonSize * 0.5f));
        if (IconButton.Draw(ctrl.Paused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause, "##afg_mini_pause", buttonSize,
                ctrl.Paused ? Styling.AccentMintSoft : Styling.AccentAmberSoft,
                resumeBlocked ? Loc.T(L.Shell.ResumeBlocked) : ctrl.Paused ? Loc.T(L.Common.Resume) : Loc.T(L.Common.Pause),
                enabled: !resumeBlocked))
        {
            ctrl.TogglePause();
        }

        ImGui.SetCursorScreenPos(new Vector2(end.X - padX - buttonSize, midY - buttonSize * 0.5f));
        if (IconButton.Draw(FontAwesomeIcon.Stop, "##afg_mini_stop", buttonSize, Styling.AccentRose, Loc.T(L.Common.StopRun)))
        {
            ctrl.Stop();
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
        return hit.Clicked;
    }
}
