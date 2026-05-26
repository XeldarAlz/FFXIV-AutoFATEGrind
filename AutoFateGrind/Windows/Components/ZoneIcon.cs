using AutoFateGrind.Core.Zones;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using System.IO;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

internal static class ZoneIcon
{
    private static readonly string ImagesRoot = Path.Combine(
        Svc.PluginInterface.AssemblyLocation.DirectoryName ?? "",
        "Images",
        "Zones");

    public static void Draw(ZoneInfo zone, float size = 0)
    {
        if (size <= 0) size = ImGui.GetTextLineHeight() * 1.6f;

        if (zone.IconFile is { Length: > 0 } file)
        {
            var path = Path.Combine(ImagesRoot, file);
            if (File.Exists(path))
            {
                var tex = Svc.Texture.GetFromFile(path).GetWrapOrEmpty();
                if (tex != null)
                {
                    ImGui.Image(tex.Handle, new Vector2(size, size));
                    return;
                }
            }
        }

        // Fallback: FontAwesome map-marker keyed by expansion.
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, ExpansionColor(zone.Expansion)))
            ImGui.TextUnformatted(FontAwesomeIcon.MapMarkedAlt.ToIconString());
    }

    private static Vector4 ExpansionColor(ExpansionKind exp) => exp switch
    {
        ExpansionKind.ShB => new(0.78f, 0.62f, 0.96f, 1f),
        ExpansionKind.EW  => new(0.92f, 0.62f, 0.78f, 1f),
        ExpansionKind.DT  => new(0.96f, 0.74f, 0.52f, 1f),
        _ => Styling.TextDim,
    };
}
