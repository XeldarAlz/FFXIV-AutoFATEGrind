using AutoFateGrind.Core;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Trading;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

// Body of the "Run until" step: the stop-condition selector, its target, and the compact "Then" action.
// The START hero lives in MainWindow; this section no longer owns it.
internal static class GoalSummary
{
    private static readonly Dictionary<string, (FontAwesomeIcon Icon, string Label)> stopVisuals = new()
    {
        [MaxGemstonesMode.ModeId] = (FontAwesomeIcon.Gem,       "Gemstones"),
        [RunCountMode.ModeId]     = (FontAwesomeIcon.ListOl,    "FATEs"),
        [TimeBoxedMode.ModeId]    = (FontAwesomeIcon.Stopwatch, "Time"),
        [EndlessMode.ModeId]      = (FontAwesomeIcon.Infinity,  "Endless"),
    };

    public static void Draw(Configuration cfg)
    {
        DrawSelector(cfg);
        ImGui.Spacing();
        DrawTargetRow(cfg);

        if (cfg.ActiveMode.Id != EndlessMode.ModeId)
            DrawThenRow(cfg);
    }

    private static readonly AfterRunAction[] afterRunOrder =
        [AfterRunAction.StayLoggedIn, AfterRunAction.ReturnToInn, AfterRunAction.Logout, AfterRunAction.CloseGame];

    private static void DrawThenRow(Configuration cfg)
    {
        ImGui.AlignTextToFramePadding();
        Caption("Then");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        using var combo = ImRaii.Combo("##afterrun", AfterRunLabel(cfg.AfterRun));
        if (!combo) return;
        foreach (var a in afterRunOrder)
        {
            if (ImGui.Selectable(AfterRunLabel(a), a == cfg.AfterRun))
            {
                cfg.AfterRun = a;
                cfg.SaveDebounced();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(AfterRunTooltip(a));
        }
    }

    private static string AfterRunLabel(AfterRunAction a) => a switch
    {
        AfterRunAction.StayLoggedIn => "Stay where you are",
        AfterRunAction.ReturnToInn  => "Return to the inn",
        AfterRunAction.Logout       => "Log out to title",
        AfterRunAction.CloseGame    => "Close the game",
        _                           => a.ToString(),
    };

    private static string AfterRunTooltip(AfterRunAction a) => a switch
    {
        AfterRunAction.StayLoggedIn => "Just stop. You're left standing wherever the last FATE ended.",
        AfterRunAction.ReturnToInn  => "Travel to your Grand Company city and enter the inn room.",
        AfterRunAction.Logout       => "Log out to the title screen.",
        AfterRunAction.CloseGame    => "Close FFXIV entirely (via XIVLauncher's /xlkill).",
        _                           => "",
    };

    private static void DrawSelector(Configuration cfg)
    {
        var modes = FateGrindModes.All;
        var activeId = cfg.ActiveMode.Id;
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = 6f * ImGuiHelpers.GlobalScale;
        var segW = (avail - gap * (modes.Count - 1)) / modes.Count;
        var segH = ImGui.GetFrameHeight() * 1.3f;

        for (var i = 0; i < modes.Count; i++)
        {
            if (i > 0) ImGui.SameLine(0, gap);
            var mode = modes[i];
            var (icon, label) = stopVisuals.TryGetValue(mode.Id, out var v)
                ? v
                : (FontAwesomeIcon.Flag, mode.DisplayName);

            if (DrawSegment(icon, label, mode.Id == activeId, new Vector2(segW, segH)))
            {
                cfg.ModeId = mode.Id;
                cfg.SaveDebounced();
            }
        }
    }

    private static bool DrawSegment(FontAwesomeIcon icon, string label, bool selected, Vector2 size)
    {
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();
        var hovered = ImGui.IsMouseHoveringRect(origin, end);

        var bg = selected ? Vector4.Lerp(Styling.CardBg, Styling.AccentViolet, 0.22f)
            : hovered ? Styling.CardBgHover : Styling.CardBgSoft;
        var border = selected ? Styling.AccentViolet
            : hovered ? Styling.AccentViolet * 0.5f : Styling.BorderDim;
        var textColor = selected ? Styling.TextStrong : Styling.TextSecondary;

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(bg), 6f);
        dl.AddRect(origin, end, ImGui.GetColorU32(border), 6f, ImDrawFlags.None, selected ? 2f : 1f);

        var iconStr = icon.ToIconString();
        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            iconSize = ImGui.CalcTextSize(iconStr);
        var labelSize = ImGui.CalcTextSize(label);
        var innerGap = 6f * ImGuiHelpers.GlobalScale;
        var startX = origin.X + (size.X - (iconSize.X + innerGap + labelSize.X)) * 0.5f;
        var midY = origin.Y + size.Y * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(startX, midY - iconSize.Y * 0.5f));
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, textColor))
            ImGui.TextUnformatted(iconStr);

        ImGui.SetCursorScreenPos(new Vector2(startX + iconSize.X + innerGap, midY - labelSize.Y * 0.5f));
        using (ImRaii.PushColor(ImGuiCol.Text, textColor))
            ImGui.TextUnformatted(label);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) return true;
        }
        return false;
    }

    private static void DrawTargetRow(Configuration cfg)
    {
        ImGui.AlignTextToFramePadding();
        switch (cfg.ActiveMode.Id)
        {
            case MaxGemstonesMode.ModeId:
            {
                Caption("Stop at");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(90);
                var target = cfg.TargetGemstoneCount;
                if (ImGui.InputInt("##gemcnt", ref target, 50, 250))
                {
                    cfg.TargetGemstoneCount = Math.Clamp(target, 1, AfgConstants.BicolorCap);
                    cfg.SaveDebounced();
                }
                ImGui.SameLine();
                Dim($"gems  ·  have {GemstoneCatalog.CurrentWalletCount()}");
                break;
            }
            case RunCountMode.ModeId:
            {
                Caption("Stop after");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                var count = cfg.TargetFateCount;
                if (ImGui.InputInt("##cnt", ref count, 5, 25))
                {
                    cfg.TargetFateCount = Math.Clamp(count, 1, 9999);
                    cfg.SaveDebounced();
                }
                ImGui.SameLine();
                Dim("FATEs");
                break;
            }
            case TimeBoxedMode.ModeId:
            {
                Caption("Stop after");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                var mins = cfg.TargetMinutes;
                if (ImGui.InputInt("##mins", ref mins, 5, 30))
                {
                    cfg.TargetMinutes = Math.Clamp(mins, 1, 1440);
                    cfg.SaveDebounced();
                }
                ImGui.SameLine();
                Dim("minutes");
                break;
            }
            default:
                Dim("Rotates the selected zones until you press Stop.");
                break;
        }
    }

    private static void Caption(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
            ImGui.TextUnformatted(text);
    }

    private static void Dim(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(text);
    }
}
