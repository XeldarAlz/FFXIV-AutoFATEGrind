using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

// One metric in the running dashboard's stat strip: an uppercase dim label across the top, a strong value
// anchored to the bottom-left, and an optional dim sub on the bottom-right. A thin accent tick on the left
// edge ties the tile to its metric's color, matching the sidebar-tab / segment language.
internal static class StatTile
{
    public static void Draw(string label, string value, string? sub, Vector4 accent, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = Layout.StatTileHeight * scale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(width, height);
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBgSoft), 6f);
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.WithAlpha(Styling.BorderDim, 0.6f)), 6f);
        dl.AddRectFilled(origin, new Vector2(origin.X + 3f * scale, end.Y), ImGui.GetColorU32(Styling.WithAlpha(accent, 0.85f)), 2f);

        var padX = 11f * scale;
        var padY = 7f * scale;

        ImGui.SetWindowFontScale(0.80f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, origin.Y + padY));
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(label.ToUpperInvariant());
        ImGui.SetWindowFontScale(1f);

        ImGui.SetWindowFontScale(1.25f);
        var valSize = ImGui.CalcTextSize(value);
        var valY = end.Y - padY - valSize.Y;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, valY));
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            ImGui.TextUnformatted(value);
        ImGui.SetWindowFontScale(1f);

        if (!string.IsNullOrEmpty(sub))
        {
            ImGui.SetWindowFontScale(0.80f);
            var subSize = ImGui.CalcTextSize(sub);
            ImGui.SetCursorScreenPos(new Vector2(end.X - padX - subSize.X, valY + valSize.Y - subSize.Y));
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted(sub);
            ImGui.SetWindowFontScale(1f);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }
}
