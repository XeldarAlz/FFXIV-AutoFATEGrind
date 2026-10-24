using AutoFateGrind.Core.Localization;
using Dalamud.Bindings.ImGui;
using Glasswork.Backend;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class PostRunSettings
{
    private const int MaxLength = 1000;
    private const int MinLines = 3;
    private const int MaxLines = 8;
    private const int EditorColors = 5;
    private const int EditorStyles = 2;

    public static void Draw(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.PostRunGroup), Loc.T(L.Settings.PostRunCaption)))
        {
            SettingsControls.TitleRow(L.Settings.PostRunLabel, L.Settings.PostRunHelp);
            DrawEditor(cfg);
        }

        Gw.EndSection();
    }

    // Glasswork has no multiline field, so the editor stays an ImGui input dressed in the Look's tokens.
    private static void DrawEditor(Configuration cfg)
    {
        var look = Frame.Look;
        var commands = cfg.PostRunCommands;
        var lines = Math.Clamp(CountLines(commands) + 1, MinLines, MaxLines);
        var height = ImGui.GetTextLineHeight() * lines + ImGui.GetStyle().FramePadding.Y * 2f;
        var width = ImGui.GetContentRegionAvail().X;

        ImGui.PushStyleColor(ImGuiCol.FrameBg, look.TertiaryBackground.ToAbgr());
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, look.TertiaryBackground.ToAbgr());
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, look.TertiaryBackground.ToAbgr());
        ImGui.PushStyleColor(ImGuiCol.Text, look.Label.ToAbgr());
        ImGui.PushStyleColor(ImGuiCol.Border, look.Separator.ToAbgr());
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, look.RadiusMedium * Frame.Scale);
        var changed = ImGui.InputTextMultiline("##afg_post_run", ref commands, MaxLength, new Vector2(width, height));
        ImGui.PopStyleVar(EditorStyles);
        ImGui.PopStyleColor(EditorColors);

        if (!changed)
        {
            return;
        }

        cfg.PostRunCommands = commands;
        cfg.SaveDebounced();
    }

    private static int CountLines(string text)
    {
        var lines = 1;
        for (var charIndex = 0; charIndex < text.Length; charIndex++)
        {
            if (text[charIndex] == '\n')
            {
                lines++;
            }
        }

        return lines;
    }
}
