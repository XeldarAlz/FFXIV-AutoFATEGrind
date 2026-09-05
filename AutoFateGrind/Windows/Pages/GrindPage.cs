using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Windows.Sections;
using AutoFateGrind.Windows.Shell;

namespace AutoFateGrind.Windows.Pages;

internal sealed class GrindPage
{
    private const float SwitchRevealMs = 320f;

    private bool scrollToLibrary;

    public void Draw(Plugin plugin, AppWindow window)
    {
        var cfg = plugin.Configuration;
        var ctrl = plugin.Controller;
        var running = ctrl.Running;

        using var reveal = Motion.PushSwitch("##afg_grind_state", running, SwitchRevealMs);
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
