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
            MinimumSize = new Vector2(640, 480),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(780, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        var ctrl = plugin.Controller;

        using var style = Styling.PushWindowStyle();

        // Corner-icon row sits at the very top, pushed to the right.
        ImGui.Dummy(new Vector2(0, 0));
        TopToolbar.Draw(plugin);
        ImGui.Spacing();

        DependencyBanner.Draw(plugin);

        if (ctrl.Running) RunningPanel.Draw(cfg, ctrl);
        else              DrawIdle(cfg, ctrl);

        Footer.Draw();
    }

    private static void DrawIdle(Configuration cfg, Core.Tasks.AutoFateController ctrl)
    {
        GoalGrid.Draw(cfg);
        ImGui.Spacing();
        GoalSummary.Draw(cfg, ctrl);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        SelectionOrder.Draw(cfg, ctrl);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ZonePicker.Draw(cfg, ctrl);
    }
}
