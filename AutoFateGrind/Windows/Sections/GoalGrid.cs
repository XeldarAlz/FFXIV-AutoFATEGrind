using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class GoalGrid
{
    private record GoalDef(string Label, FontAwesomeIcon Icon, Vector4 Accent, GrindMode Mode, string Tooltip);

    private static readonly GoalDef[] goals =
    [
        new("Farm Gemstones",     FontAwesomeIcon.Gem,      Styling.AccentViolet,     GrindMode.MaxGemstones,
            "Stops when Bicolor Gemstones hit your trade threshold. Auto-trade resumes the grind."),
        new("Max Shared FATEs",   FontAwesomeIcon.Trophy,   Styling.AccentAmber,      GrindMode.MaxFates,
            "Stops when every selected zone's 'Free Market Friend' achievement (60 Shared FATEs) is complete. ShB / EW / DT only — no equivalent exists in earlier expansions."),
        new("Run N FATEs",        FontAwesomeIcon.ListOl,   Styling.AccentVioletSoft, GrindMode.RunCount,
            "Stops after a fixed number of FATE completions across all selected zones."),
        new("Endless Grind",      FontAwesomeIcon.Infinity, Styling.AccentPink,       GrindMode.Endless,
            "Runs forever, rotating between selected zones, until you press Stop."),
    ];

    public static void Draw(Configuration cfg, Plugin plugin)
    {
        DrawHeaderRow(plugin);

        var avail = ImGui.GetContentRegionAvail().X;
        var gap = Layout.GoalCardGap * ImGuiHelpers.GlobalScale;
        var cardWidth = (avail - gap * (goals.Length - 1)) / goals.Length;
        var cardHeight = Layout.GoalCardHeight * ImGuiHelpers.GlobalScale;
        var size = new Vector2(cardWidth, cardHeight);

        for (var i = 0; i < goals.Length; i++)
        {
            var g = goals[i];
            if (i > 0) ImGui.SameLine(0, gap);

            if (GoalCard.Draw($"##goal_{g.Mode}", g.Label, g.Icon, g.Accent, cfg.Mode == g.Mode, size, g.Tooltip))
            {
                cfg.Mode = g.Mode;
                cfg.SaveDebounced();
            }
        }
    }

    private static void DrawHeaderRow(Plugin plugin)
    {
        // Placeholder so the inline icon strip has a line to attach to.
        ImGui.Dummy(new Vector2(0, ImGui.GetFrameHeight()));
        TopToolbar.DrawIconsInline(plugin);
    }
}
