using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class GoalSummary
{
    private static readonly Dictionary<string, (FontAwesomeIcon Icon, string Label)> stopVisuals = new()
    {
        ["maxgemstones"] = (FontAwesomeIcon.Gem,       "Gemstones"),
        ["runcount"]     = (FontAwesomeIcon.ListOl,    "FATEs"),
        ["timeboxed"]    = (FontAwesomeIcon.Stopwatch, "Time"),
        ["endless"]      = (FontAwesomeIcon.Infinity,  "Endless"),
    };

    public static void Draw(Configuration cfg, AutoFateController controller)
    {
        ImGui.Spacing();
        Styling.SectionLabel("Run until");
        ImGui.Spacing();

        DrawSelector(cfg);
        ImGui.Spacing();
        DrawTargetRow(cfg);

        DrawAfterRunRow(cfg);

        ImGui.Spacing();
        DrawStartButton(cfg, controller);
    }

    private static readonly AfterRunAction[] afterRunOrder =
        [AfterRunAction.StayLoggedIn, AfterRunAction.ReturnToInn, AfterRunAction.Logout, AfterRunAction.CloseGame];

    private static void DrawAfterRunRow(Configuration cfg)
    {
        // Endless never auto-completes, so there is no "after" — hide the whole Then section rather than
        // show a disabled control implying one exists. The plan line below still explains Endless.
        if (cfg.ActiveMode.Id != "endless")
        {
            ImGui.Spacing();
            Styling.SectionLabel("Then");
            ImGui.Spacing();

            ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
            using (var combo = ImRaii.Combo("##afterrun", AfterRunLabel(cfg.AfterRun)))
                if (combo)
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

        ImGui.Spacing();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
            ImGui.TextWrapped(PlanSentence(cfg));
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

    private static string PlanSentence(Configuration cfg)
    {
        if (cfg.ActiveMode.Id == "endless")
            return "Grind the selected zones forever — until you press Stop.";

        var until = cfg.ActiveMode.Id switch
        {
            "maxgemstones" => $"until you reach {cfg.TargetGemstoneCount} Bicolor Gemstones",
            "runcount"     => $"for {cfg.TargetFateCount} FATEs",
            "timeboxed"    => $"for {cfg.TargetMinutes} minutes",
            _              => "until done",
        };
        var then = cfg.AfterRun switch
        {
            AfterRunAction.ReturnToInn => "head to the inn",
            AfterRunAction.Logout      => "log out to the title screen",
            AfterRunAction.CloseGame   => "close the game",
            _                          => "stop and stay where you are",
        };
        return $"Grind {until}, then {then}.";
    }

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
            case "maxgemstones":
            {
                Caption("Stop at");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(90);
                var target = cfg.TargetGemstoneCount;
                if (ImGui.InputInt("##gemcnt", ref target, 50, 250))
                {
                    cfg.TargetGemstoneCount = Math.Clamp(target, 1, 1500);
                    cfg.SaveDebounced();
                }
                ImGui.SameLine();
                Dim($"gems  ·  have {GemstoneCount()}");
                break;
            }
            case "runcount":
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
            case "timeboxed":
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

    private static void DrawStartButton(Configuration cfg, AutoFateController controller)
    {
        var startList = ZoneSelection.ResolveStartList(cfg);
        var runnable = startList.Count;
        var depsOk = ExternalPlugins.AllRequiredInstalled();
        var canStart = runnable > 0 && !controller.Running && depsOk;
        var label = canStart ? $"START   ({runnable} zone{(runnable == 1 ? "" : "s")})" : "START";

        if (PrimaryButton.Draw(label, Styling.AccentViolet, canStart))
            controller.RunAll(startList);

        if (!canStart && !depsOk)
            Tooltip.For("Install all required plugins first (see the plug icon).");
        else if (!canStart && runnable == 0)
            Tooltip.For("Pick at least one zone below.");
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

    private static unsafe int GemstoneCount()
    {
        const uint bicolorItemId = 26807;
        var im = InventoryManager.Instance();
        return im is null ? 0 : im->GetInventoryItemCount(bicolorItemId);
    }
}
