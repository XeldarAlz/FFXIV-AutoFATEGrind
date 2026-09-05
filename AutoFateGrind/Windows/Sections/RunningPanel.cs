using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using clib.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class RunningPanel
{
    private const float PadX = 18f;
    private const int QueueLength = 5;

    public static void Draw(Configuration cfg, AutoFateController controller)
    {
        var paused = controller.Paused;
        var fate = PublicEvent.CurrentFate;
        var inFate = fate is not null && fate.State == FateState.Running && !paused;
        var (accent, accentSoft, label) = PhasePalette(controller, inFate);

        DrawHeaderStrip(cfg, accent, accentSoft, paused);
        Styling.VSpace(6f);
        DrawHeroCard(cfg, controller, fate, inFate, accent, accentSoft, label);

        Styling.VSpace(10f);
        DrawStatTiles(cfg, controller);

        Styling.VSpace(10f);
        DrawQueue(cfg);
    }

    private static void DrawHeaderStrip(Configuration cfg, Vector4 accent, Vector4 accentSoft, bool paused)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var lineHeight = ImGui.GetTextLineHeight();
        var midY = origin.Y + lineHeight * 0.5f;

        var dot = paused ? accent : Styling.PulseColor(accent, accentSoft, Styling.PulseMedium);
        var radius = 4f * scale;
        Paint.Dot(dl, new Vector2(origin.X + radius + 3f * scale, midY), radius, dot);

        var status = paused ? Loc.T(L.Shell.StatusPaused) : Loc.T(L.Shell.StatusRunning);
        var statusSize = TextDraw.SmallCapsSize(status);
        TextDraw.SmallCaps(status, new Vector2(origin.X + radius * 2f + 12f * scale, midY - statusSize.Y * 0.5f), Styling.TextSecondary);

        var current = Svc.ClientState.TerritoryType;
        var zone = ZoneRegistry.Zones.FirstOrDefault(z => z.TerritoryId == current);
        var footer = Loc.Plural(L.Run.Rotation, cfg.SelectedZones.Count, zone?.Name ?? Loc.T(L.Run.SomewhereElse));
        using (Fonts.PushCaption())
        {
            var footerSize = TextDraw.Measure(footer);
            TextDraw.At(footer, new Vector2(origin.X + avail - footerSize.X, midY - footerSize.Y * 0.5f), Styling.TextMuted);
        }

        ImGui.Dummy(new Vector2(avail, lineHeight));
    }

    private static void DrawHeroCard(
        Configuration cfg, AutoFateController controller, PublicEvent? fate, bool inFate,
        Vector4 accent, Vector4 accentSoft, string label)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.HeroCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();
        var active = controller.Running && !controller.Paused;
        var rounding = Styling.PanelRounding * scale;

        Paint.Glass(dl, origin, end, rounding, accent, active ? 0.10f : 0.03f, 0f, elevated: true);
        if (active)
        {
            var border = Styling.PulseColor(Styling.WithAlpha(accent, 0.5f), accentSoft, inFate ? Styling.PulseFast : Styling.PulseMedium);
            Paint.Stroke(dl, origin, end, border, rounding, 1.6f);
        }

        var info = GoalProgress.Resolve(cfg, controller.SessionSnapshot);

        var padX = PadX * scale;
        var ringRadius = size.Y * 0.5f - 18f * scale;
        var ringCenter = new Vector2(origin.X + padX + ringRadius, origin.Y + size.Y * 0.5f);
        DrawGoalRing(ringCenter, ringRadius, accent, active, info);

        var columnX = ringCenter.X + ringRadius + 20f * scale;
        var columnRight = end.X - padX;
        var columnWidth = columnRight - columnX;
        var y = origin.Y + 16f * scale;

        var chipHeight = DrawPhaseChip(columnX, y, label, accent, accentSoft, inFate && (fate?.HasBonus ?? false));
        y += chipHeight + 10f * scale;

        if (inFate)
        {
            using (Fonts.PushHeadline())
            {
                var name = TextDraw.Truncate($"L{fate!.Level}   {fate.Name}", columnWidth);
                var nameSize = TextDraw.Measure(name);
                TextDraw.At(name, new Vector2(columnX, y), Styling.TextStrong);
                y += nameSize.Y + 9f * scale;
            }

            var barHeight = 10f * scale;
            var progress = Motion.Approach(Motion.Key("##afg_fate_progress"), fate.Progress / 100f, 10f);
            Paint.Bar(dl, new Vector2(columnX, y), columnWidth, barHeight, progress, accent);
            y += barHeight + 8f * scale;

            using (Fonts.PushCaption())
            {
                TextDraw.At(Loc.T(L.Run.FateProgress, fate.Progress, Formatting.Time(fate.TimeRemaining)), new Vector2(columnX, y), Styling.TextDim);
                TextDraw.Right(info.Remaining, columnRight, y, Styling.WithAlpha(accentSoft, 0.9f));
            }
        }
        else
        {
            var status = TextDraw.Truncate(string.IsNullOrWhiteSpace(controller.Status) ? Loc.T(L.Common.Working) : controller.Status, columnWidth);
            var statusSize = TextDraw.Measure(status);
            TextDraw.At(status, new Vector2(columnX, y), Styling.TextSecondary);
            y += statusSize.Y + 12f * scale;

            var barHeight = 8f * scale;
            if (active) Paint.IndeterminateBar(dl, new Vector2(columnX, y), columnWidth, barHeight, accent);
            else Paint.Bar(dl, new Vector2(columnX, y), columnWidth, barHeight, 0f, accent);
            y += barHeight + 8f * scale;

            using (Fonts.PushCaption())
                TextDraw.At(info.Remaining, new Vector2(columnX, y), Styling.WithAlpha(accentSoft, 0.9f));
        }

        ImGui.Dummy(size);
    }

    private static void DrawGoalRing(Vector2 center, float radius, Vector4 accent, bool active, GoalProgress.Info info)
    {
        var thickness = 6f * ImGuiHelpers.GlobalScale;
        ProgressRing.Track(center, radius, thickness, Styling.WithAlpha(Styling.BorderDim, 0.7f));

        if (info.Endless)
        {
            if (active) ProgressRing.Sweep(center, radius, thickness, accent, Styling.PulseOrbit, MathF.PI * 0.6f, 1f);
        }
        else
        {
            var fraction = Motion.Approach(Motion.Key("##afg_goal_ring"), info.Fraction ?? 0f, 6f);
            ProgressRing.Fill(center, radius, thickness, fraction, accent);
        }

        ProgressRing.CenterValue(center, info.CenterBig, info.CenterSmall, Styling.TextStrong, Styling.TextDim);
    }

    private static float DrawPhaseChip(float x, float y, string text, Vector4 accent, Vector4 accentSoft, bool bonus)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var padX = 9f * scale;
        var padY = 3f * scale;

        using (Fonts.PushCaption())
        {
            var label = TextDraw.Upper(text);
            var textSize = TextDraw.Measure(label);
            var starWidth = bonus ? TextDraw.IconSize(FontAwesomeIcon.Star).X + 6f * scale : 0f;
            var chipMin = new Vector2(x, y);
            var chipMax = chipMin + new Vector2(padX * 2f + textSize.X + starWidth, textSize.Y + padY * 2f);

            Paint.Pill(dl, chipMin, chipMax, Styling.WithAlpha(accent, 0.28f), Styling.WithAlpha(accent, 0.65f));
            TextDraw.At(label, new Vector2(x + padX, y + padY), accentSoft);
            if (bonus)
            {
                var starSize = TextDraw.IconSize(FontAwesomeIcon.Star);
                TextDraw.Icon(FontAwesomeIcon.Star, new Vector2(x + padX + textSize.X + 6f * scale, y + padY + (textSize.Y - starSize.Y) * 0.5f), Styling.AccentAmber);
            }

            return chipMax.Y - chipMin.Y;
        }
    }

    private static (Vector4 Accent, Vector4 AccentSoft, string Label) PhasePalette(AutoFateController controller, bool inFate)
    {
        if (!controller.Running) return (Styling.TextDim, Styling.TextSecondary, Loc.T(L.Run.PhaseReady));

        if (controller.Paused)
        {
            return controller.PauseReason == PauseReason.InContent
                ? (Styling.AccentAmber, Styling.AccentAmberSoft, Loc.T(L.Run.PhasePausedInContent))
                : (Styling.AccentAmber, Styling.AccentAmberSoft, Loc.T(L.Run.PhasePaused));
        }

        return controller.Phase switch
        {
            AutoPhase.Trading    => (Styling.AccentAmber, Styling.AccentAmberSoft, Loc.T(L.Run.PhaseTrading)),
            AutoPhase.Repairing  => (Styling.TextStrong,  Styling.TextSecondary,   Loc.T(L.Run.PhaseRepairing)),
            AutoPhase.Humanizing => (Styling.AccentMint,  Styling.AccentMintSoft,  Loc.T(L.Run.PhaseBreak)),
            AutoPhase.Finishing  => (Styling.AccentMint,  Styling.AccentMintSoft,  Loc.T(L.Run.PhaseFinishing)),
            AutoPhase.Grinding   => (Styling.AccentBlue,  Styling.AccentBlueSoft,  inFate ? Loc.T(L.Run.PhaseEngaging) : Loc.T(L.Run.PhaseGrinding)),
            _                    => (Styling.TextDim,     Styling.TextSecondary,   Loc.T(L.Run.PhaseStandingBy)),
        };
    }

    private static void DrawStatTiles(Configuration cfg, AutoFateController controller)
    {
        var session = controller.SessionSnapshot;
        var scale = ImGuiHelpers.GlobalScale;
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = 8f * scale;
        var tileWidth = (avail - gap * 3f) / 4f;

        var completed = session?.CompletedCount ?? 0;
        var fatesPerHour = session?.FatesPerHour ?? 0;
        var gems = session?.GemstonesEarned ?? 0;
        var hours = session?.Elapsed.TotalHours ?? 0;
        var gemsPerHour = hours > 0 ? gems / hours : 0;

        var info = GoalProgress.Resolve(cfg, session);
        var elapsedValue = session is null ? Formatting.Elapsed(TimeSpan.Zero) : Formatting.Elapsed(session.Elapsed);
        var elapsedSub = info.Endless ? null : info.Remaining;

        StatTile.Draw(Loc.T(L.Run.TileFates), completed.ToString(Loc.Culture), null, Styling.AccentBlue, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileGems), gems.ToString(Loc.Culture), gemsPerHour >= 1 ? Loc.T(L.Run.PerHour, gemsPerHour.ToString("F0", Loc.Culture)) : null, Styling.AccentAmber, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileFatesPerHour), fatesPerHour > 0 ? fatesPerHour.ToString("F1", Loc.Culture) : "—", null, Styling.AccentMint, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileElapsed), elapsedValue, elapsedSub, Styling.AccentViolet, tileWidth);
    }

    private static void DrawQueue(Configuration cfg)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var heading = Loc.T(L.Run.UpNext);
        var labelSize = TextDraw.SectionTitleSize(heading);
        TextDraw.SectionTitle(heading, origin, Styling.TextStrong);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, labelSize.Y + 8f * scale));

        var player = Svc.Objects.LocalPlayer;
        if (player is null)
        {
            EmptyHint(Loc.T(L.Common.PlayerNotLoaded));
            return;
        }

        var current = PublicEvent.CurrentFate;
        var eligible = (PublicEvent.Fates ?? Enumerable.Empty<PublicEvent>())
            .Where(f => current is null || f.Id != current.Id)
            .Where(f => FateScanner.IsEligible(f, cfg, null));
        var fates = FateScanner.ApplySort(eligible, cfg.FateSortOrder, player.Position)
            .Take(QueueLength)
            .ToArray();
        if (fates.Length == 0)
        {
            EmptyHint(Loc.T(L.Run.NoOtherFates));
            return;
        }

        for (var index = 0; index < fates.Length; index++)
        {
            DrawQueueRow(fates[index], player.Position, index == 0);
        }
    }

    private static void DrawQueueRow(PublicEvent fate, Vector3 playerPos, bool emphasize)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.QueueRowHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();

        var accent = fate.HasBonus ? Styling.AccentAmber : Styling.AccentViolet;
        Paint.Glass(dl, origin, end, Styling.CardRounding * scale, accent, emphasize ? 0.10f : 0.03f);

        var padX = 13f * scale;
        var topY = origin.Y + 9f * scale;

        var icon = fate.HasBonus ? FontAwesomeIcon.Star : FontAwesomeIcon.Bolt;
        var iconColor = fate.HasBonus ? Styling.AccentAmber : Styling.TextDim;
        var iconSize = TextDraw.IconSize(icon);
        TextDraw.Icon(icon, new Vector2(origin.X + padX, topY + (ImGui.GetTextLineHeight() - iconSize.Y) * 0.5f), iconColor);

        var distance = (int)Math.Round(Vector3.Distance(playerPos, fate.Position));
        var meta = Loc.T(L.Run.QueueMeta, fate.Progress, Formatting.Time(fate.TimeRemaining), distance);
        Vector2 metaSize;
        using (Fonts.PushCaption())
        {
            metaSize = TextDraw.Measure(meta);
            TextDraw.At(meta, new Vector2(end.X - padX - metaSize.X, topY + (ImGui.GetTextLineHeight() - metaSize.Y) * 0.5f), Styling.TextDim);
        }

        var nameX = origin.X + padX + iconSize.X + 10f * scale;
        var name = TextDraw.Truncate($"L{fate.Level}   {fate.Name}", end.X - padX - metaSize.X - 12f * scale - nameX);
        TextDraw.At(name, new Vector2(nameX, topY), emphasize ? Styling.TextStrong : Styling.TextSecondary);

        Paint.Bar(dl, new Vector2(origin.X + padX, end.Y - 13f * scale), size.X - padX * 2f, Layout.QueueBarHeight * scale, fate.Progress / 100f, accent);

        ImGui.Dummy(size);
    }

    private static void EmptyHint(string text)
    {
        var origin = ImGui.GetCursorScreenPos();
        TextDraw.At(text, origin, Styling.TextMuted);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight()));
    }
}
