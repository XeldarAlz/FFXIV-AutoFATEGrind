using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class YokaiEventCard
{
    private const float PadX = 18f;
    private const float PadY = 16f;
    private const float IconGap = 10f;
    private const float RowGap = 10f;
    private const float TargetGap = 10f;
    private const int MaxYokaiMedals = 99;
    private const string ToggleId = "##afg_yokai_event_toggle";
    private const string Separator = "  \u00b7  ";

    public static void Draw(Configuration cfg, AutoFateController ctrl)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = PadX * scale;
        var padY = PadY * scale;
        var innerWidth = width - padX * 2f;
        var enabled = ZoneSelection.GoalPlansZones(cfg);
        var editable = !ctrl.Running;
        var dl = ImGui.GetWindowDrawList();

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        var x = origin.X + padX;
        var y = origin.Y + padY;
        y = DrawHeader(cfg, x, y, innerWidth, enabled, editable);
        y += RowGap * scale;
        y = DrawCaption(Loc.T(L.Grind.NoteYokai), x, y, innerWidth, Styling.TextMuted);

        if (enabled)
        {
            y += RowGap * scale;
            y = DrawStatus(cfg, x, y, innerWidth);
            y += RowGap * scale;
            y = DrawTarget(cfg, x, y);
        }

        var end = new Vector2(origin.X + width, y + padY);
        var active = Motion.Approach(Motion.Key(ToggleId, 2), enabled ? 1f : 0f);

        dl.ChannelsSetCurrent(0);
        Paint.Glass(dl, origin, end, Styling.PanelRounding * scale, Styling.AccentViolet, 0.03f + 0.09f * active, 0f, elevated: true);
        dl.ChannelsMerge();

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, end.Y - origin.Y));
    }

    private static float DrawHeader(Configuration cfg, float x, float y, float width, bool enabled, bool editable)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var title = Loc.T(L.Grind.YokaiEventTitle);
        var titleSize = TextDraw.SectionTitleSize(title);
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.Ghost);
        var toggleSize = new Vector2(ToggleSwitch.Width, ToggleSwitch.Height) * scale;
        var rowHeight = MathF.Max(titleSize.Y, toggleSize.Y);
        var midY = y + rowHeight * 0.5f;

        TextDraw.Icon(FontAwesomeIcon.Ghost, new Vector2(x, midY - iconSize.Y * 0.5f), enabled ? Styling.AccentVioletSoft : Styling.TextDim);
        TextDraw.SectionTitle(title, new Vector2(x + iconSize.X + IconGap * scale, midY - titleSize.Y * 0.5f), Styling.TextStrong);

        var toggleOrigin = new Vector2(x + width - toggleSize.X, midY - toggleSize.Y * 0.5f);
        ImGui.SetCursorScreenPos(toggleOrigin);
        var value = enabled;
        if (ToggleSwitch.Draw(ToggleId, ref value, editable))
        {
            Apply(cfg, value);
        }

        if (!editable && Hit.HoveringRect(toggleOrigin, toggleOrigin + toggleSize))
        {
            Tooltip.Show(Loc.T(L.Grind.PlanLocked));
        }

        return y + rowHeight;
    }

    private static float DrawCaption(string text, float x, float y, float width, Vector4 color)
    {
        using (Fonts.PushCaption())
        {
            TextDraw.Wrapped(text, new Vector2(x, y), width, color);
            return y + TextDraw.MeasureWrapped(text, width).Y;
        }
    }

    private static float DrawStatus(Configuration cfg, float x, float y, float width)
    {
        var (owned, weaponsLeft) = YokaiProgress.Ownership();
        var watchMissing = !YokaiOps.OwnsWatch();
        var watch = YokaiOps.IsWatchEquipped() ? Loc.T(L.Grind.YokaiEventWatchEquipped)
            : watchMissing ? Loc.T(L.Grind.YokaiEventWatchMissing)
            : Loc.T(L.Grind.YokaiEventWatchStored);
        var text = string.Concat(
            Loc.T(L.Grind.YokaiEventOwned, owned, YokaiCatalog.Entries.Length), Separator,
            Loc.Plural(L.Grind.YokaiEventWeaponsLeft, weaponsLeft), Separator,
            watch);
        return DrawCaption(text, x, y, width, watchMissing ? Styling.AccentAmber : Styling.TextDim);
    }

    private static float DrawTarget(Configuration cfg, float x, float y)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = TargetGap * scale;
        var rowHeight = ImGui.GetFrameHeight();
        var label = Loc.T(L.Grind.StopAt);
        var labelSize = TextDraw.Measure(label);
        TextDraw.At(label, new Vector2(x, y + (rowHeight - labelSize.Y) * 0.5f), Styling.TextSecondary);

        var stepperX = x + labelSize.X + gap;
        ImGui.SetCursorScreenPos(new Vector2(stepperX, y));
        var value = cfg.TargetYokaiMedals;
        if (Stepper.Draw("##afg_yokai_target", ref value, 1, 1, MaxYokaiMedals, "%d"))
        {
            cfg.TargetYokaiMedals = Math.Clamp(value, 1, MaxYokaiMedals);
            cfg.SaveDebounced();
        }

        var unit = Loc.T(L.Grind.UnitYokaiMedals);
        var unitSize = TextDraw.Measure(unit);
        TextDraw.At(unit, new Vector2(stepperX + Stepper.DefaultWidth * scale + gap, y + (rowHeight - unitSize.Y) * 0.5f), Styling.TextDim);
        return y + rowHeight;
    }

    private static void Apply(Configuration cfg, bool on)
    {
        if (on)
        {
            if (cfg.ActiveMode.Id != YokaiMedalsMode.ModeId)
            {
                cfg.YokaiPreviousModeId = cfg.ActiveMode.Id;
            }

            cfg.ModeId = YokaiMedalsMode.ModeId;
        }
        else
        {
            var previous = FateGrindModes.GetById(cfg.YokaiPreviousModeId);
            cfg.ModeId = previous is null || previous.Id == YokaiMedalsMode.ModeId ? FateGrindModes.Default.Id : previous.Id;
        }

        cfg.SaveDebounced();
    }
}
