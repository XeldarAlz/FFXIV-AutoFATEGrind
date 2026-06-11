using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

// Full-width hero CTA shared by the idle START and running STOP buttons: a glyph + bold title on the left
// and a right-aligned sublabel. When disabled it dims and swaps in a lock glyph + the blocking reason.
internal static class HeroButton
{
    public static bool Draw(FontAwesomeIcon icon, string title, string? sublabel, Vector4 accent, bool enabled, string? disabledReason = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = Layout.HeroButtonHeight * scale;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(width, height);
        var dl = ImGui.GetWindowDrawList();
        var hovered = enabled && ImGui.IsMouseHoveringRect(origin, end);

        var accentSoft = Vector4.Lerp(accent, Vector4.One, 0.35f);
        var bg = enabled
            ? Vector4.Lerp(Styling.CardBg, accent, hovered ? 0.80f : 0.58f)
            : Styling.CardBgSoft;
        var border = enabled ? (hovered ? accentSoft : accent) : Styling.BorderDim;

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(bg), 8f);
        dl.AddRect(origin, end, ImGui.GetColorU32(border), 8f, ImDrawFlags.None, enabled ? 1.6f : 1f);

        var padX = 18f * scale;
        var midY = origin.Y + height * 0.5f;
        var glyph = (enabled ? icon : FontAwesomeIcon.Lock).ToIconString();

        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            iconSize = ImGui.CalcTextSize(glyph);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, midY - iconSize.Y * 0.5f));
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, enabled ? accentSoft : Styling.TextMuted))
            ImGui.TextUnformatted(glyph);

        ImGui.SetWindowFontScale(1.2f);
        var titleSize = ImGui.CalcTextSize(title);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX + iconSize.X + 12f * scale, midY - titleSize.Y * 0.5f));
        using (ImRaii.PushColor(ImGuiCol.Text, enabled ? Styling.TextStrong : Styling.TextMuted))
            ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1f);

        var sub = enabled ? sublabel : (disabledReason ?? sublabel);
        if (!string.IsNullOrEmpty(sub))
        {
            var subSize = ImGui.CalcTextSize(sub);
            ImGui.SetCursorScreenPos(new Vector2(end.X - padX - subSize.X, midY - subSize.Y * 0.5f));
            using (ImRaii.PushColor(ImGuiCol.Text, enabled ? Styling.WithAlpha(Styling.TextStrong, 0.7f) : Styling.TextDim))
                ImGui.TextUnformatted(sub);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));

        if (!enabled) return false;
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) return true;
        }
        return false;
    }
}
