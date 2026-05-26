using AutoFateGrind.Core.Tasks;
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
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(760, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        var ctrl = plugin.Controller;

        using var style = Styling.PushWindowStyle();

        TopToolbar.Draw(plugin, ctrl);
        DependencyBanner.Draw(plugin);
        Header.Draw(ctrl, cfg);
        ZoneList.Draw(ctrl, cfg);
        LiveFateTracker.Draw(cfg);
        Footer.Draw();
    }
}
