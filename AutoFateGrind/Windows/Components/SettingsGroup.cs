using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

internal sealed class SettingsGroup : IDisposable
{
    private const float PaddingX = 12f;
    private const float PaddingY = 6f;
    private const float GroupGap = 14f;
    private const float FootnoteFontScale = 0.92f;
    private const float FootnotePullUp = 6f;
    private const float FootnoteIndent = 4f;

    internal static float ContentRightEdge { get; private set; }
    internal static bool RowDrawnInGroup;

    private readonly Vector2 cardOrigin;
    private readonly float cardWidth;

    public static SettingsGroup Begin(string title)
    {
        if (title.Length > 0)
        {
            Styling.SectionLabel(title);
            Styling.VSpace(2f);
        }

        return new SettingsGroup();
    }

    private SettingsGroup()
    {
        var scale = ImGuiHelpers.GlobalScale;
        cardOrigin = ImGui.GetCursorScreenPos();
        cardWidth = ImGui.GetContentRegionAvail().X;
        ContentRightEdge = cardOrigin.X + cardWidth - PaddingX * scale;
        RowDrawnInGroup = false;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(cardOrigin + new Vector2(PaddingX, PaddingY) * scale);
        ImGui.BeginGroup();
    }

    public void Dispose()
    {
        ImGui.EndGroup();
        var scale = ImGuiHelpers.GlobalScale;
        var cardEnd = new Vector2(cardOrigin.X + cardWidth, ImGui.GetItemRectMax().Y + PaddingY * scale);

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        var rounding = Styling.CardRounding * scale;
        drawList.AddRectFilled(cardOrigin, cardEnd, ImGui.GetColorU32(Styling.CardBgSoft), rounding);
        drawList.AddRect(cardOrigin, cardEnd, ImGui.GetColorU32(Styling.WithAlpha(Styling.BorderDim, 0.55f)), rounding);
        drawList.ChannelsMerge();

        ImGui.SetCursorScreenPos(new Vector2(cardOrigin.X, cardEnd.Y));
        ImGui.Dummy(new Vector2(cardWidth, 0f));
        Styling.VSpace(GroupGap);
    }

    public static void Footnote(string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - FootnotePullUp * scale);
        ImGui.Indent(FootnoteIndent * scale);
        ImGui.SetWindowFontScale(FootnoteFontScale);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
        {
            ImGui.TextWrapped(text);
        }

        ImGui.SetWindowFontScale(1f);
        ImGui.Unindent(FootnoteIndent * scale);
        Styling.VSpace(GroupGap);
    }

    // Local-coordinate X of the card's inner right edge, so block content (reorder buttons, etc.)
    // can right-align to the card border instead of the wider window content region.
    public static float InnerRightLocalX()
        => ImGui.GetCursorPosX() + (ContentRightEdge - ImGui.GetCursorScreenPos().X);
}
