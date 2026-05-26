using AutoFateGrind.Core.External;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections;

internal static class TopToolbar
{
    // Draws the plug/info/cog icons right-aligned on the current line. Used inline
    // from the GOAL row (idle) and the status header (running) so we never have a
    // standalone empty band above the content.
    public static void DrawIconsInline(Plugin plugin)
    {
        var anyMissing = !ExternalPlugins.AllRequiredInstalled();

        var plugLabel = FontAwesomeIcon.Plug.ToIconString();
        var infoLabel = FontAwesomeIcon.InfoCircle.ToIconString();
        var gearLabel = FontAwesomeIcon.Cog.ToIconString();

        float btnW;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            btnW = ImGui.CalcTextSize(gearLabel).X + ImGui.GetStyle().FramePadding.X * 2;

        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - btnW * 3 - spacingX * 2);

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

        if (plugClicked) plugin.ToggleDependenciesUi();
        if (infoClicked) plugin.ToggleAboutUi();
        if (gearClicked) plugin.ToggleConfigUi();
    }

    private static void HoverTip(string text)
    {
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(text);
    }
}
