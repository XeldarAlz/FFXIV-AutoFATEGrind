using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoFateGrind.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base("Auto Fate Grind — Settings###AutoFateGrindConfig")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(480, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(680, 900),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        using var style = Styling.PushWindowStyle();

        DrawBehaviorSection(cfg);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawFiltersSection(cfg);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawCombatSection(cfg);
    }

    private static void DrawBehaviorSection(Configuration cfg)
    {
        Styling.SectionLabel("Behavior");

        var b1 = cfg.AutoShowOnLogin;
        if (ImGui.Checkbox("Open this window on login", ref b1))
        {
            cfg.AutoShowOnLogin = b1;
            cfg.SaveDebounced();
        }

        var b2 = cfg.SwapZonesWhenEmpty;
        if (ImGui.Checkbox("Swap to next selected zone when current zone has no eligible FATEs", ref b2))
        {
            cfg.SwapZonesWhenEmpty = b2;
            cfg.SaveDebounced();
        }

        var b3 = cfg.ShowLivePopout;
        if (ImGui.Checkbox("Show live FATE tracker as a separate window (HUD-style overlay)", ref b3))
        {
            cfg.ShowLivePopout = b3;
            cfg.SaveDebounced();
        }
    }

    private static void DrawFiltersSection(Configuration cfg)
    {
        Styling.SectionLabel("FATE filters");
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextWrapped("Keeps the plugin off dying / late FATEs other players are finishing.");
        ImGui.Spacing();

        var minTime = cfg.MinTimeRemainingSec;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderInt("Minimum time remaining (seconds)", ref minTime, 30, 600))
        {
            cfg.MinTimeRemainingSec = minTime;
            cfg.SaveDebounced();
        }

        var maxProgress = cfg.MaxProgressPct;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderInt("Maximum progress (percent)", ref maxProgress, 50, 99))
        {
            cfg.MaxProgressPct = maxProgress;
            cfg.SaveDebounced();
        }
    }

    private static void DrawCombatSection(Configuration cfg)
    {
        Styling.SectionLabel("Combat");
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextWrapped("Name of the auto-rotation preset to activate when engaging a FATE. The default preset is bundled with the plugin and reinstalled on demand.");
        ImGui.Spacing();

        var preset = cfg.CombatPresetName;
        ImGui.SetNextItemWidth(280);
        if (ImGui.InputText("Preset name", ref preset, 64))
        {
            cfg.CombatPresetName = preset;
            cfg.SaveDebounced();
        }

        using (ImRaii.Disabled(true))
            ImGui.Button("Install bundled preset (TODO)");
    }
}
