using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

internal static class PrimaryButton
{
    public static bool Draw(string label, Vector4 accent, bool enabled = true)
    {
        var height = Layout.PrimaryButtonHeight * ImGuiHelpers.GlobalScale;
        using var dis = ImRaii.Disabled(!enabled);
        using var col = ImRaii.PushColor(ImGuiCol.Button, enabled ? accent * 0.65f : Styling.CardBgSoft)
            .Push(ImGuiCol.ButtonHovered, enabled ? accent * 0.85f : Styling.CardBgSoft)
            .Push(ImGuiCol.ButtonActive, enabled ? accent : Styling.CardBgSoft)
            .Push(ImGuiCol.Text, enabled ? Styling.TextStrong : Styling.TextMuted);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 8f);

        return ImGui.Button(label, new Vector2(-1, height));
    }
}
