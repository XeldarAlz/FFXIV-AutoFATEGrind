using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class GoalGrid
{
    private record GoalDef(string Label, FontAwesomeIcon Icon, Vector4 Accent, GrindMode Mode);

    private static readonly GoalDef[] goals =
    [
        new("Farm Gemstones",       FontAwesomeIcon.Gem,      Styling.AccentViolet,     GrindMode.MaxGemstones),
        new("Complete Achievement", FontAwesomeIcon.Trophy,   Styling.AccentAmber,      GrindMode.MaxFates),
        new("Run N FATEs",          FontAwesomeIcon.ListOl,   Styling.AccentVioletSoft, GrindMode.RunCount),
        new("Endless Grind",        FontAwesomeIcon.Infinity, Styling.AccentPink,       GrindMode.Endless),
    ];

    public static void Draw(Configuration cfg)
    {
        Styling.SectionLabel("What do you want to do?");
        ImGui.Spacing();

        var avail = ImGui.GetContentRegionAvail().X;
        var gap = Layout.GoalCardGap * ImGuiHelpers.GlobalScale;
        var cardWidth = (avail - gap * (goals.Length - 1)) / goals.Length;
        var cardHeight = Layout.GoalCardHeight * ImGuiHelpers.GlobalScale;
        var size = new Vector2(cardWidth, cardHeight);

        for (var i = 0; i < goals.Length; i++)
        {
            var g = goals[i];
            if (i > 0) ImGui.SameLine(0, gap);

            if (GoalCard.Draw($"##goal_{g.Mode}", g.Label, g.Icon, g.Accent, cfg.Mode == g.Mode, size))
            {
                cfg.Mode = g.Mode;
                cfg.SaveDebounced();
            }
        }
    }
}
