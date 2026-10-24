using Dalamud.Bindings.ImGui;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

// Gw.ComboBox and Gw.TextField take the whole content width, so a field that shares a row with its title gets a
// narrower content region for the duration of the scope, the way Glasswork's own sections inset theirs.
internal ref struct ContentWidth
{
    private readonly ImGuiWindowPtr window;
    private readonly float workRight;
    private readonly float contentRight;

    public ContentWidth(float width)
    {
        window = ImGuiP.GetCurrentWindow();
        workRight = window.WorkRect.Max.X;
        contentRight = window.ContentRegionRect.Max.X;
        var right = ImGui.GetCursorScreenPos().X + width * Frame.Scale;
        window.WorkRect.Max.X = right;
        window.ContentRegionRect.Max.X = right;
    }

    public void Dispose()
    {
        window.WorkRect.Max.X = workRight;
        window.ContentRegionRect.Max.X = contentRight;
    }
}
