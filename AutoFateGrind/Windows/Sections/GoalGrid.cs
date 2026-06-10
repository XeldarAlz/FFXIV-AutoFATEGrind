using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class GoalGrid
{
    public static void Draw(Configuration cfg, Plugin plugin)
    {
        DrawHeaderRow(plugin);

        Styling.SectionLabel("Activity");
        ImGui.Spacing();

        var avail = ImGui.GetContentRegionAvail().X;
        var gap = Layout.GoalCardGap * ImGuiHelpers.GlobalScale;
        var cardWidth = (avail - gap) / 2f;
        var cardHeight = Layout.GoalCardHeight * ImGuiHelpers.GlobalScale;
        var size = new Vector2(cardWidth, cardHeight);

        GoalCard.Draw("##activity_fates", "Open-World FATEs", FontAwesomeIcon.Bolt,
            Styling.AccentViolet, selected: true, size,
            tooltip: "Auto-grind FATEs across the zones you pick below.");

        ImGui.SameLine(0, gap);
        GoalCard.Draw("##activity_fieldops", "Field Operations", FontAwesomeIcon.Hammer,
            Styling.AccentVioletSoft, selected: false, size,
            tooltip: "Eureka, Bozja and Occult is in progress.\nComing soon, stay tuned!", disabled: true);
    }

    private static void DrawHeaderRow(Plugin plugin)
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            ImGui.TextUnformatted(Greeting());
        TopToolbar.DrawIconsInline(plugin);
    }

    private static string Greeting()
    {
        return DateTime.Now.Hour switch
        {
            >= 5 and < 12 => "Good morning, ready to grind?",
            >= 12 and < 17 => "Good afternoon, ready to grind?",
            >= 17 and < 22 => "Good evening, ready to grind?",
            _ => "Late night grind session?",
        };
    }
}
