using AutoFateGrind.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections;

internal static class Footer
{
    public static void Draw()
    {
        ImGui.Separator();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextUnformatted($"Auto FATE Grind — {AfgConstants.PrimaryCommand} / {AfgConstants.AliasCommand}");
    }
}
