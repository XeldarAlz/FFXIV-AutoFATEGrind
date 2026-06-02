using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

// Square icon-font button. The icon glyph plus the supplied ImGui id ("##...") form the button label.
internal static class IconButton
{
    public static bool Draw(FontAwesomeIcon icon, string id, float size)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGui.Button(icon.ToIconString() + id, new Vector2(size, size));
    }
}
