using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using AutoFateGrind.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoFateGrind.Windows.Shell;

internal static class ActionDock
{
    private const float PadX = 18f;
    private const float ButtonGap = 8f;

    public static void Draw(Plugin plugin, Vector2 size, float windowRounding)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();
        Dock.Background(dl, origin, end, windowRounding);

        var padX = PadX * scale;
        var buttonHeight = Layout.HeroButtonHeight * scale;
        var innerWidth = size.X - padX * 2f;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, origin.Y + (size.Y - buttonHeight) * 0.5f));

        var ctrl = plugin.Controller;
        if (ctrl.Running) DrawRunControls(plugin, innerWidth);
        else DrawStart(plugin, innerWidth);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    private static void DrawRunControls(Plugin plugin, float innerWidth)
    {
        var ctrl = plugin.Controller;
        var gap = ButtonGap * ImGuiHelpers.GlobalScale;
        var half = (innerWidth - gap) * 0.5f;

        if (PauseButton.Draw(ctrl.PauseReason, half)) ctrl.TogglePause();

        ImGui.SameLine(0f, gap);

        var session = ctrl.SessionSnapshot;
        var state = ctrl.Paused ? Loc.T(L.Grind.StatePaused) : Loc.T(L.Grind.StateRunning);
        var stopSub = session is null ? state : Loc.T(L.Grind.StopSub, state, Formatting.Elapsed(session.Elapsed));
        if (StopButton.Draw(stopSub, half)) ctrl.Stop();
    }

    private static void DrawStart(Plugin plugin, float innerWidth)
    {
        var cfg = plugin.Configuration;
        var ctrl = plugin.Controller;
        var startList = ZoneSelection.ResolveStartList(cfg);
        var depsOk = ExternalPlugins.AllRequiredInstalled();
        var canStart = startList.Count > 0 && depsOk;
        var reason = !depsOk ? Loc.T(L.Grind.ReasonInstall)
            : startList.Count == 0 ? Loc.T(L.Grind.ReasonPickZone)
            : string.Empty;
        var sub = Loc.T(L.Grind.StartSub, Loc.Plural(L.Grind.ZonesCount, startList.Count), ReadyState.StopSummary(cfg));

        if (StartButton.Draw(sub, canStart, reason, innerWidth)) ctrl.RunAll(startList);
    }
}
