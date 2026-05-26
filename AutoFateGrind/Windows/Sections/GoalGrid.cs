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
        ["maxgemstones"] = (FontAwesomeIcon.Gem,      Styling.AccentViolet),
        ["maxfates"]     = (FontAwesomeIcon.Trophy,   Styling.AccentAmber),
        ["runcount"]     = (FontAwesomeIcon.ListOl,   Styling.AccentVioletSoft),
        ["endless"]      = (FontAwesomeIcon.Infinity, Styling.AccentPink),
    };

    public static void Draw(Configuration cfg, Plugin plugin)
    {
        DrawHeaderRow(plugin);

        var modes = FateGrindModes.All;
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = Layout.GoalCardGap * ImGuiHelpers.GlobalScale;
        var cardWidth = (avail - gap * (modes.Count - 1)) / modes.Count;
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
    }

    private static void DrawHeaderRow(Plugin plugin)
    {
        ImGui.Dummy(new Vector2(0, ImGui.GetFrameHeight()));
        TopToolbar.DrawIconsInline(plugin);
    }
}
