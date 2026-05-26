using AutoFateGrind.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoFateGrind.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("Auto Fate Grind###AutoFateGrindMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(780, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        var ctrl = plugin.Controller;

        using var style = Styling.PushWindowStyle();

        DependencyBanner.Draw(plugin);

        if (ctrl.Running) RunningPanel.Draw(cfg, ctrl);
        else              DrawIdle(cfg, ctrl);

        Footer.Draw();
    }

    private void DrawIdle(Configuration cfg, Core.Tasks.AutoFateController ctrl)
    {
        GoalGrid.Draw(cfg, plugin);
        GoalSummary.Draw(cfg, ctrl);

        ImGui.Spacing();
        ZonePicker.Draw(cfg, ctrl);
    }
}
