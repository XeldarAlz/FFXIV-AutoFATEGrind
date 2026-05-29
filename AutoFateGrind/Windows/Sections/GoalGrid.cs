using AutoFateGrind.Core.Modes;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class GoalGrid
{
    // Keyed by mode id so the registry stays UI-agnostic; unknown ids fall back to a neutral visual.
    private static readonly Dictionary<string, (FontAwesomeIcon Icon, Vector4 Accent)> visuals = new()
    {
        ["maxgemstones"] = (FontAwesomeIcon.Gem,       Styling.AccentViolet),
        ["runcount"]     = (FontAwesomeIcon.ListOl,    Styling.AccentVioletSoft),
        ["timeboxed"]    = (FontAwesomeIcon.Stopwatch, Styling.AccentTeal),
        ["endless"]      = (FontAwesomeIcon.Infinity,  Styling.AccentPink),
    };

    public static void Draw(Configuration cfg, Plugin plugin)
    {
        DrawHeaderRow(plugin);

        var modes = FateGrindModes.All;
        var cardCount = modes.Count + 1; // trailing disabled "coming soon" card
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = Layout.GoalCardGap * ImGuiHelpers.GlobalScale;
        var cardWidth = (avail - gap * (cardCount - 1)) / cardCount;
        var cardHeight = Layout.GoalCardHeight * ImGuiHelpers.GlobalScale;
        var size = new Vector2(cardWidth, cardHeight);

        var activeId = cfg.ActiveMode.Id;

        for (var i = 0; i < modes.Count; i++)
        {
            var mode = modes[i];
            if (i > 0) ImGui.SameLine(0, gap);

            var (icon, accent) = visuals.TryGetValue(mode.Id, out var v)
                ? v
                : (FontAwesomeIcon.Flag, Styling.AccentVioletSoft);

            if (GoalCard.Draw($"##goal_{mode.Id}", mode.DisplayName, icon, accent, activeId == mode.Id, size, mode.Description))
            {
                cfg.ModeId = mode.Id;
                cfg.SaveDebounced();
            }
        }

        ImGui.SameLine(0, gap);
        GoalCard.Draw("##goal_coming_soon", "Field Operations", FontAwesomeIcon.Hammer,
            Styling.AccentVioletSoft, false, size,
            tooltip: "Eureka, Bozja and Occult is in progress.\nComing soon, stay tuned!", disabled: true);
    }

    private static void DrawHeaderRow(Plugin plugin)
    {
        ImGui.Dummy(new Vector2(0, ImGui.GetFrameHeight()));
        TopToolbar.DrawIconsInline(plugin);
    }
}
