using AutoFateGrind.Core;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class PlanCard
{
    private const float PadX = 18f;
    private const float PadY = 16f;
    private const float TokenHeight = 30f;
    private const float TokenPadX = 11f;
    private const float ChevronGap = 6f;
    private const float WordGap = 8f;
    private const float LineGap = 10f;
    private const float PopoverWidth = 420f;
    private const float PopoverSegmentHeight = 40f;
    private const float PopoverGap = 6f;

    private const string GoalPopup = "##afg_goal_popover";
    private const string AfterPopup = "##afg_after_popover";

    private static readonly AfterRunAction[] afterRunOrder =
        [AfterRunAction.StayLoggedIn, AfterRunAction.ReturnToInn, AfterRunAction.Logout, AfterRunAction.CloseGame];

    private static readonly (LocString Token, LocString Name, LocString Detail)[] afterRunChoices =
    [
        (L.Grind.AfterStayToken, L.Grind.AfterStayName, L.Grind.AfterStayDetail),
        (L.Grind.AfterInnToken, L.Grind.AfterInnName, L.Grind.AfterInnDetail),
        (L.Grind.AfterLogoutToken, L.Grind.AfterLogoutName, L.Grind.AfterLogoutDetail),
        (L.Grind.AfterCloseToken, L.Grind.AfterCloseName, L.Grind.AfterCloseDetail),
    ];

    private static readonly Piece[] pieces = new Piece[7];
    private static readonly Vector2 PopoverPadding = new(16f, 16f);
    private static readonly Segmented.Item[] modeItems = new Segmented.Item[4];
    private static Vector2 goalAnchor;
    private static Vector2 afterAnchor;

    private enum PieceKind { Word, Zones, Goal, After }

    private readonly record struct Piece(PieceKind Kind, string Text);

    public static bool Draw(Configuration cfg, AutoFateController ctrl)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = PadX * scale;
        var padY = PadY * scale;
        var dl = ImGui.GetWindowDrawList();

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        var y = origin.Y + padY;
        var planLabel = Loc.T(L.Grind.Plan);
        var labelSize = TextDraw.SectionTitleSize(planLabel);
        TextDraw.SectionTitle(planLabel, new Vector2(origin.X + padX, y), Styling.TextStrong);
        y += labelSize.Y + 10f * scale;

        var focusZones = DrawSentence(cfg, ctrl, new Vector2(origin.X + padX, y), width - padX * 2f, out var sentenceBottom);
        y = sentenceBottom + 14f * scale;

        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, y));
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 6f) * scale))
        {
            ImGui.PushID("##afg_plan_chips");
            ImGui.BeginGroup();
            QueueStrip.Draw(cfg, ctrl);
            ImGui.EndGroup();
            ImGui.PopID();
        }

        var end = new Vector2(origin.X + width, ImGui.GetItemRectMax().Y + padY);

        dl.ChannelsSetCurrent(0);
        Paint.Glass(dl, origin, end, Styling.PanelRounding * scale, Styling.AccentViolet, 0.07f, 0f, elevated: true);
        dl.ChannelsMerge();

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, end.Y - origin.Y));

        DrawGoalPopover(cfg);
        DrawAfterPopover(cfg);
        return focusZones;
    }

    private static bool DrawSentence(Configuration cfg, AutoFateController ctrl, Vector2 start, float maxWidth, out float bottom)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var tokenHeight = TokenHeight * scale;
        var wordGap = WordGap * scale;
        var endless = cfg.ActiveMode.Id == EndlessMode.ModeId;
        var zoneCount = ZoneSelection.ResolveStartList(cfg).Count;
        var end = Loc.T(L.Grind.SentenceEnd);

        var count = 0;
        pieces[count++] = new Piece(PieceKind.Word, Loc.T(L.Grind.SentenceGrind));
        pieces[count++] = new Piece(PieceKind.Zones, ZoneLabel(zoneCount));
        pieces[count++] = new Piece(PieceKind.Word, Loc.T(L.Grind.SentenceUntil));
        pieces[count++] = new Piece(PieceKind.Goal, GoalLabel(cfg));
        if (!endless)
        {
            pieces[count++] = new Piece(PieceKind.Word, Loc.T(L.Grind.SentenceThen));
            pieces[count++] = new Piece(PieceKind.After, Loc.T(afterRunChoices[AfterIndex(cfg)].Token));
        }

        if (end.Length > 0) pieces[count++] = new Piece(PieceKind.Word, end);

        var x = start.X;
        var y = start.Y;
        var focusZones = false;
        var editable = !ctrl.Running;

        for (var index = 0; index < count; index++)
        {
            var piece = pieces[index];
            var isPunctuation = ReferenceEquals(piece.Text, end);
            var pieceWidth = piece.Kind == PieceKind.Word ? TextDraw.Measure(piece.Text).X : TokenWidth(piece.Text);
            var gap = isPunctuation ? 0f : wordGap;
            if (x > start.X && x + pieceWidth > start.X + maxWidth)
            {
                x = start.X;
                y += tokenHeight + LineGap * scale;
            }

            if (piece.Kind == PieceKind.Word)
            {
                var textSize = TextDraw.Measure(piece.Text);
                TextDraw.At(piece.Text, new Vector2(x, y + (tokenHeight - textSize.Y) * 0.5f), Styling.TextSecondary);
            }
            else
            {
                var accent = piece.Kind == PieceKind.Zones && zoneCount == 0 ? Styling.AccentAmber : Styling.AccentViolet;
                ImGui.SetCursorScreenPos(new Vector2(x, y));
                var clicked = DrawToken(TokenId(piece.Kind), piece.Text, accent, editable);
                var anchor = new Vector2(x, y + tokenHeight + PopoverGap * scale);
                switch (piece.Kind)
                {
                    case PieceKind.Zones:
                        if (clicked) focusZones = true;
                        break;
                    case PieceKind.Goal:
                        goalAnchor = anchor;
                        if (clicked) ImGui.OpenPopup(GoalPopup);
                        break;
                    case PieceKind.After:
                        afterAnchor = anchor;
                        if (clicked) ImGui.OpenPopup(AfterPopup);
                        break;
                }
            }

            x += pieceWidth + gap;
        }

        bottom = y + tokenHeight;
        return focusZones;
    }

    private static string TokenId(PieceKind kind) => kind switch
    {
        PieceKind.Zones => "##afg_token_zones",
        PieceKind.Goal  => "##afg_token_goal",
        _               => "##afg_token_after",
    };

    private static float TokenWidth(string label)
    {
        var scale = ImGuiHelpers.GlobalScale;
        return TokenPadX * 2f * scale + TextDraw.Measure(label).X + ChevronGap * scale + TextDraw.IconSize(FontAwesomeIcon.ChevronDown).X;
    }

    private static bool DrawToken(string id, string label, Vector4 accent, bool enabled)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(TokenWidth(label), TokenHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var hit = Hit.Area(id, size, enabled);
        var hover = Motion.Hover(Motion.Key(id), hit.Hovered);
        var dl = ImGui.GetWindowDrawList();

        var fill = enabled ? Styling.WithAlpha(accent, 0.16f + 0.12f * hover) : Styling.WithAlpha(Styling.Surface2, 0.8f);
        var border = enabled ? Styling.WithAlpha(accent, 0.45f + 0.30f * hover) : Styling.WithAlpha(Styling.BorderDim, 0.6f);
        var text = enabled ? Vector4.Lerp(Styling.Lighten(accent, 0.3f), Styling.TextStrong, hover * 0.5f) : Styling.TextDim;
        if (hit.Held) fill = Styling.Darken(fill, 0.12f);
        Paint.Pill(dl, origin, end, fill, border);

        var midY = origin.Y + size.Y * 0.5f;
        var labelSize = TextDraw.Measure(label);
        TextDraw.At(label, new Vector2(origin.X + TokenPadX * scale, midY - labelSize.Y * 0.5f), text);

        var chevronSize = TextDraw.IconSize(FontAwesomeIcon.ChevronDown);
        TextDraw.Icon(FontAwesomeIcon.ChevronDown, new Vector2(end.X - TokenPadX * scale - chevronSize.X, midY - chevronSize.Y * 0.5f + 1f * scale),
            Styling.WithAlpha(text, 0.75f));

        if (!enabled && Hit.HoveringRect(origin, end)) ImGui.SetTooltip(Loc.T(L.Grind.PlanLocked));
        return hit.Clicked;
    }

    private static string ZoneLabel(int count) => count == 0 ? Loc.T(L.Grind.ZonesNone) : Loc.Plural(L.Grind.ZonesCount, count);

    private static string GoalLabel(Configuration cfg) => cfg.ActiveMode.Id switch
    {
        MaxGemstonesMode.ModeId => Loc.T(L.Grind.GoalGemstones, cfg.TargetGemstoneCount.ToString("N0", Loc.Culture)),
        RunCountMode.ModeId     => Loc.T(L.Grind.GoalFates, cfg.TargetFateCount),
        TimeBoxedMode.ModeId    => Loc.T(L.Grind.GoalMinutes, cfg.TargetMinutes),
        _                       => Loc.T(L.Grind.GoalEndless),
    };

    private static int AfterIndex(Configuration cfg) => Math.Max(0, Array.IndexOf(afterRunOrder, cfg.AfterRun));

    private static void RefreshModeItems(IReadOnlyList<IFateGrindMode> modes)
    {
        for (var index = 0; index < modes.Count && index < modeItems.Length; index++)
        {
            modeItems[index] = modes[index].Id switch
            {
                MaxGemstonesMode.ModeId => new Segmented.Item(FontAwesomeIcon.Gem, Loc.T(L.Grind.ModeGemstones)),
                RunCountMode.ModeId     => new Segmented.Item(FontAwesomeIcon.ListOl, Loc.T(L.Grind.ModeFates)),
                TimeBoxedMode.ModeId    => new Segmented.Item(FontAwesomeIcon.Stopwatch, Loc.T(L.Grind.ModeTime)),
                EndlessMode.ModeId      => new Segmented.Item(FontAwesomeIcon.Infinity, Loc.T(L.Grind.ModeEndless)),
                _                       => new Segmented.Item(FontAwesomeIcon.Flag, modes[index].DisplayName),
            };
        }
    }

    private static Vector2 PopoverPosition(Vector2 anchor, float contentWidth)
    {
        var viewport = ImGui.GetMainViewport();
        var popupWidth = contentWidth + PopoverPadding.X * 2f * ImGuiHelpers.GlobalScale;
        var maxX = viewport.WorkPos.X + viewport.WorkSize.X - popupWidth;
        return anchor with { X = MathF.Max(viewport.WorkPos.X, MathF.Min(anchor.X, maxX)) };
    }

    private static IDisposable PushPopoverStyle()
        => ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, PopoverPadding * ImGuiHelpers.GlobalScale);

    private static void DrawGoalPopover(Configuration cfg)
    {
        if (!ImGui.IsPopupOpen(GoalPopup)) return;

        var scale = ImGuiHelpers.GlobalScale;
        var modes = FateGrindModes.All;
        var visible = Math.Min(modes.Count, modeItems.Length);
        RefreshModeItems(modes);
        var width = MathF.Max(PopoverWidth * scale, Segmented.PreferredWidth(modeItems.AsSpan(0, visible)));

        ImGui.SetNextWindowPos(PopoverPosition(goalAnchor, width), ImGuiCond.Appearing);
        using var style = PushPopoverStyle();
        using var popup = ImRaii.Popup(GoalPopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove);
        if (!popup) return;

        var selected = 0;
        for (var index = 0; index < modes.Count; index++)
        {
            if (modes[index].Id == cfg.ActiveMode.Id) selected = index;
        }

        if (Segmented.Draw("##afg_modes", modeItems.AsSpan(0, visible), ref selected, height: PopoverSegmentHeight, width: width))
        {
            cfg.ModeId = modes[selected].Id;
            cfg.SaveDebounced();
        }

        Styling.VSpace(14f);
        if (cfg.ActiveMode.Id == EndlessMode.ModeId)
        {
            Caption(Loc.T(L.Grind.EndlessNote), width);
            return;
        }

        var (label, unit, step, min, max, value, note) = cfg.ActiveMode.Id switch
        {
            MaxGemstonesMode.ModeId => (Loc.T(L.Grind.StopAt), Loc.T(L.Grind.UnitGemstones), 50, 1, AfgConstants.BicolorCap, cfg.TargetGemstoneCount,
                Loc.T(L.Grind.NoteGemstones, GemstoneCatalog.CurrentWalletCount().ToString("N0", Loc.Culture))),
            RunCountMode.ModeId     => (Loc.T(L.Grind.StopAfter), Loc.T(L.Grind.UnitFates), 5, 1, 9999, cfg.TargetFateCount, Loc.T(L.Grind.NoteFates)),
            _                       => (Loc.T(L.Grind.StopAfter), Loc.T(L.Grind.UnitMinutes), 5, 1, 1440, cfg.TargetMinutes, Loc.T(L.Grind.NoteMinutes)),
        };

        var labelOrigin = ImGui.GetCursorScreenPos();
        var labelSize = TextDraw.Measure(label);
        TextDraw.At(label, labelOrigin, Styling.TextSecondary);
        ImGui.Dummy(new Vector2(width, labelSize.Y + 8f * scale));

        if (Stepper.Draw("##afg_target", ref value, step, min, max, "%d"))
        {
            ApplyTarget(cfg, value);
        }

        ImGui.SameLine(0f, 10f * scale);
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(unit);

        Styling.VSpace(8f);
        Caption(note, width);
    }

    private static void Caption(string text, float width)
    {
        using (Fonts.PushCaption())
        {
            var origin = ImGui.GetCursorScreenPos();
            TextDraw.Wrapped(text, origin, width, Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, TextDraw.MeasureWrapped(text, width).Y));
        }
    }

    private static void ApplyTarget(Configuration cfg, int value)
    {
        switch (cfg.ActiveMode.Id)
        {
            case MaxGemstonesMode.ModeId: cfg.TargetGemstoneCount = Math.Clamp(value, 1, AfgConstants.BicolorCap); break;
            case RunCountMode.ModeId:     cfg.TargetFateCount = Math.Clamp(value, 1, 9999); break;
            default:                      cfg.TargetMinutes = Math.Clamp(value, 1, 1440); break;
        }

        cfg.SaveDebounced();
    }

    private static void DrawAfterPopover(Configuration cfg)
    {
        if (!ImGui.IsPopupOpen(AfterPopup)) return;

        var scale = ImGuiHelpers.GlobalScale;
        var width = PopoverWidth * scale;
        ImGui.SetNextWindowPos(PopoverPosition(afterAnchor, width), ImGuiCond.Appearing);
        using var style = PushPopoverStyle();
        using var popup = ImRaii.Popup(AfterPopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove);
        if (!popup) return;

        var current = AfterIndex(cfg);
        var heading = Loc.T(L.Grind.WhenGoalReached);
        var labelOrigin = ImGui.GetCursorScreenPos();
        var labelSize = TextDraw.SectionTitleSize(heading);
        TextDraw.SectionTitle(heading, labelOrigin, Styling.TextStrong);
        ImGui.Dummy(new Vector2(width, labelSize.Y + 8f * scale));

        for (var index = 0; index < afterRunChoices.Length; index++)
        {
            if (DrawChoiceRow(index, Loc.T(afterRunChoices[index].Name), Loc.T(afterRunChoices[index].Detail), index == current, width))
            {
                cfg.AfterRun = afterRunOrder[index];
                cfg.SaveDebounced();
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private static bool DrawChoiceRow(int index, string name, string detail, bool selected, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padX = 10f * scale;
        var padY = 8f * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        float detailHeight;
        using (Fonts.PushCaption())
            detailHeight = TextDraw.Measure(detail).Y;
        var size = new Vector2(width, padY * 2f + lineHeight + 2f * scale + detailHeight);
        var origin = ImGui.GetCursorScreenPos();

        ImGui.PushID((nint)(index + 1));
        var hit = Hit.Area("##choice", size);
        var hover = Motion.Hover(Motion.Key("##choice"), hit.Hovered);
        ImGui.PopID();

        var dl = ImGui.GetWindowDrawList();
        var fill = selected ? Styling.WithAlpha(Styling.AccentViolet, 0.18f + 0.08f * hover) : Styling.WithAlpha(Styling.Surface2, 0.8f * hover);
        if (fill.W > 0.01f) Paint.Fill(dl, origin, origin + size, fill, 8f * scale);

        var nameColor = selected ? Styling.AccentVioletSoft : Vector4.Lerp(Styling.TextSecondary, Styling.TextStrong, hover);
        TextDraw.At(name, new Vector2(origin.X + padX, origin.Y + padY), nameColor);
        using (Fonts.PushCaption())
            TextDraw.At(detail, new Vector2(origin.X + padX, origin.Y + padY + lineHeight + 2f * scale), Styling.TextMuted);

        if (selected)
        {
            var checkSize = TextDraw.IconSize(FontAwesomeIcon.Check);
            TextDraw.Icon(FontAwesomeIcon.Check, new Vector2(origin.X + size.X - padX - checkSize.X, origin.Y + padY + (lineHeight - checkSize.Y) * 0.5f), Styling.AccentVioletSoft);
        }

        return hit.Clicked;
    }
}
