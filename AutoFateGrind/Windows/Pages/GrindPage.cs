using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Windows.Sections;
using AutoFateGrind.Windows.Shell;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Pages;

internal sealed class GrindPage
{
    private const float SwitchRevealMs = 320f;

    private bool wasRunning;
    private bool scrollToLibrary;
    private long switchTick = Environment.TickCount64;

    public void Draw(Plugin plugin, AppWindow window)
    {
        var cfg = plugin.Configuration;
        var ctrl = plugin.Controller;
        var running = ctrl.Running;
        if (running != wasRunning)
        {
            wasRunning = running;
            switchTick = Environment.TickCount64;
        }

        var reveal = Motion.Reveal(switchTick, SwitchRevealMs);
        if (reveal < 1f)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (1f - reveal) * 10f * ImGuiHelpers.GlobalScale);
        }

        using var alpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, MathF.Max(0.001f, reveal * ImGui.GetStyle().Alpha));
        if (running) RunningPanel.Draw(cfg, ctrl);
        else DrawIdle(plugin, window, cfg, ctrl);
    }

    private void DrawIdle(Plugin plugin, AppWindow window, Configuration cfg, AutoFateController ctrl)
    {
        if (Headline.Draw(cfg, ctrl, plugin.History)) window.Show(AppWindow.Page.Plugins);
        Styling.VSpace(20f);

        if (PlanCard.Draw(cfg, ctrl)) scrollToLibrary = true;
        Styling.VSpace(26f);

        ZoneLibrary.Draw(cfg, ctrl, scrollToLibrary);
        scrollToLibrary = false;
        Styling.VSpace(12f);
    }
}
