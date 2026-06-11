using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using AutoFateGrind.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoFateGrind.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("Auto FATE Grind###AutoFateGrindMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(100, 100),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(780, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
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
    }

    private void DrawIdle(Configuration cfg, AutoFateController ctrl)
    {
        IdleHeader.Draw(cfg, plugin);
        ImGui.Spacing();

        var zoneCount = ZoneSelection.ResolveStartList(cfg).Count;
        StepHeader.Draw(1, "Zones", zoneCount > 0 ? $"{zoneCount} selected" : null);
        ZonePicker.Draw(cfg, ctrl);

        ImGui.Spacing();
        StepHeader.Draw(2, "Run until");
        GoalSummary.Draw(cfg);

        ImGui.Spacing();
        ImGui.Spacing();
        DrawStart(cfg, ctrl);
    }

    private static void DrawStart(Configuration cfg, AutoFateController ctrl)
    {
        var startList = ZoneSelection.ResolveStartList(cfg);
        var depsOk = ExternalPlugins.AllRequiredInstalled();
        var canStart = startList.Count > 0 && !ctrl.Running && depsOk;
        var reason = !depsOk ? "install required plugins"
            : startList.Count == 0 ? "pick at least one zone below"
            : "";
        var sub = $"{startList.Count} zone{(startList.Count == 1 ? "" : "s")}";

        if (StartButton.Draw(sub, canStart, reason))
            ctrl.RunAll(startList);
    }
}
