using AutoFateGrind.Core.External;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections;

internal static class TopToolbar
{
    public static void DrawIconsInline(Plugin plugin)
    {
        var anyMissing = !ExternalPlugins.AllRequiredInstalled();

        var statsLabel = FontAwesomeIcon.ChartLine.ToIconString();
        var plugLabel = FontAwesomeIcon.Plug.ToIconString();
        var infoLabel = FontAwesomeIcon.InfoCircle.ToIconString();
        var gearLabel = FontAwesomeIcon.Cog.ToIconString();

        float btnW;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            btnW = ImGui.CalcTextSize(gearLabel).X + ImGui.GetStyle().FramePadding.X * 2;

        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - btnW * 4 - spacingX * 3);

        bool statsClicked;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            statsClicked = ImGui.Button(statsLabel + "##history");
        HoverTip("Run history");

        ImGui.SameLine();
        bool plugClicked;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, anyMissing ? Styling.AccentRose : Styling.TextSecondary))
            plugClicked = ImGui.Button(plugLabel + "##deps");
        HoverTip(anyMissing ? "Required plugins missing" : "Dependencies");

        ImGui.SameLine();
        bool infoClicked;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            infoClicked = ImGui.Button(infoLabel + "##about");
        HoverTip("About");

        ImGui.SameLine();
        bool gearClicked;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            gearClicked = ImGui.Button(gearLabel + "##gear");
        HoverTip("Settings");

        if (statsClicked) plugin.ToggleHistoryUi();
        if (plugClicked) plugin.ToggleDependenciesUi();
        if (infoClicked) plugin.ToggleAboutUi();
        if (gearClicked) plugin.ToggleConfigUi();
    }

    private static void HoverTip(string text)
    {
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(text);
    }
}
