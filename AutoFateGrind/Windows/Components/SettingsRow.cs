using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Components;

internal static class SettingsRow
{
    public static void Draw(string title, string? helper, Action drawControl)
    {
        ImGui.SetWindowFontScale(1.05f);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1.0f);

        if (!string.IsNullOrEmpty(helper))
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped(helper);

        ImGui.Spacing();
        drawControl();
        ImGui.Spacing();
        ImGui.Spacing();
    }
}
