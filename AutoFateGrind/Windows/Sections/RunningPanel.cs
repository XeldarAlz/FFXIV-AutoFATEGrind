using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using clib.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

// The live "mission control" view shown while a grind is running: a goal-progress ring + current-activity
// hero, a STOP hero, a scannable stat strip, the up-next queue, and the current zone.
internal static class RunningPanel
{
    public static void Draw(Configuration cfg, AutoFateController controller)
    {
        var fate = PublicEvent.CurrentFate;
        var inFate = fate is not null && fate.State == FateState.Running;
        var (accent, accentSoft, label) = PhasePalette(controller, inFate);

        DrawHeaderStrip(accent, accentSoft);
        DrawHeroCard(cfg, controller, fate, inFate, accent, accentSoft, label);

        Styling.VSpace(3f);
        var s = controller.SessionSnapshot;
        var stopSub = s is null ? "running" : $"running · {Formatting.Elapsed(s.Elapsed)}";
        if (StopButton.Draw(stopSub)) controller.Stop();

        Styling.VSpace(7f);
        DrawStatTiles(cfg, controller);

        Styling.VSpace(5f);
        DrawQueue(cfg);

        Styling.VSpace(4f);
        DrawFooter(cfg);
    }

    private static void DrawHeaderStrip(Vector4 accent, Vector4 accentSoft)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var dot = Styling.PulseColor(accent, accentSoft, Styling.PulseMedium);

        var cur = ImGui.GetCursorScreenPos();
        var fh = ImGui.GetFrameHeight();
        var r = 4f * scale;
        var center = new Vector2(cur.X + r + 3f * scale, cur.Y + fh * 0.5f);
        dl.AddCircleFilled(center, r * 2.2f, ImGui.GetColorU32(Styling.WithAlpha(dot, 0.22f)));
        dl.AddCircleFilled(center, r, ImGui.GetColorU32(dot));
        ImGui.Dummy(new Vector2(r * 2f + 8f * scale, fh));

        ImGui.SameLine(0, 4f);
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
            ImGui.TextUnformatted("RUNNING");

        TopToolbar.DrawIconsInline(Plugin.Instance);
    }

    private static void DrawHeroCard(
        Configuration cfg, AutoFateController controller, PublicEvent? fate, bool inFate,
        Vector4 accent, Vector4 accentSoft, string label)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = Layout.HeroCardHeight * scale;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(width, height);
        var dl = ImGui.GetWindowDrawList();
        var active = controller.Running;

        var border = active
            ? Styling.PulseColor(accent, accentSoft, inFate ? Styling.PulseFast : Styling.PulseMedium)
            : Styling.BorderDim;
        var bg = active ? Vector4.Lerp(Styling.CardBg, accent, 0.08f) : Styling.CardBgSoft;
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(bg), Styling.CardRounding);
        dl.AddRect(origin, end, ImGui.GetColorU32(border), Styling.CardRounding, ImDrawFlags.None, active ? 2f : 1f);

        var info = GoalProgress.Resolve(cfg, controller.SessionSnapshot);

        var padX = 16f * scale;
        var ringRadius = height * 0.5f - 15f * scale;
        var ringCenter = new Vector2(origin.X + padX + ringRadius, origin.Y + height * 0.5f);
        DrawGoalRing(ringCenter, ringRadius, accent, active, info);

        var colX = ringCenter.X + ringRadius + 18f * scale;
        var colRight = end.X - padX;
        var colW = colRight - colX;
        var y = origin.Y + 13f * scale;

        var chipH = DrawPhaseChip(colX, y, label, accent, accentSoft, inFate && (fate?.HasBonus ?? false));
        y += chipH + 9f * scale;

        if (inFate)
        {
            var name = Truncate($"L{fate!.Level}   {fate.Name}", colW, 1.18f);
            PutScaled(name, colX, y, Styling.TextStrong, 1.18f);
            y += MeasureHeight(name, 1.18f) + 8f * scale;

            var barH = 13f * scale;
            DrawBar(new Vector2(colX, y), colW, barH, fate.Progress / 100f, accent, 4f);
            y += barH + 6f * scale;

            PutScaled($"{fate.Progress}%   ·   {Formatting.Time(fate.TimeRemaining)} left", colX, y, Styling.TextDim, 0.92f);
            PutRightScaled(info.Remaining, colRight, y, Styling.WithAlpha(accentSoft, 0.9f), 0.92f);
        }
        else
        {
            var status = Truncate(string.IsNullOrWhiteSpace(controller.Status) ? "Working…" : controller.Status, colW, 1.05f);
            PutScaled(status, colX, y, Styling.TextSecondary, 1.05f);
            y += MeasureHeight(status, 1.05f) + 10f * scale;

            var barH = 9f * scale;
            if (active) DrawIndeterminateBar(new Vector2(colX, y), colW, barH, accent);
            else DrawBar(new Vector2(colX, y), colW, barH, 0f, accent, 4f);
            y += barH + 7f * scale;

            PutScaled(info.Remaining, colX, y, Styling.WithAlpha(accentSoft, 0.9f), 0.92f);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawGoalRing(Vector2 center, float radius, Vector4 accent, bool active, GoalProgress.Info info)
    {
        var thickness = 5f * ImGuiHelpers.GlobalScale;
        ProgressRing.Track(center, radius, thickness, Styling.WithAlpha(Styling.BorderDim, 0.8f));

        if (info.Endless)
        {
            if (active)
                ProgressRing.Sweep(center, radius, thickness, accent, Styling.PulseOrbit, MathF.PI * 0.6f, 1f);
        }
        else
        {
            ProgressRing.Fill(center, radius, thickness, info.Fraction ?? 0f, accent);
        }

        ProgressRing.CenterValue(center, info.CenterBig, info.CenterSmall, Styling.TextStrong, Styling.TextDim, 1.5f);
    }

    private static float DrawPhaseChip(float x, float y, string text, Vector4 accent, Vector4 accentSoft, bool bonus)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        const float chipScale = 0.84f;
        var padX = 8f * scale;
        var padY = 3f * scale;

        ImGui.SetWindowFontScale(chipScale);
        var textSize = ImGui.CalcTextSize(text);
        var star = bonus ? FontAwesomeIcon.Star.ToIconString() : "";
        var starGap = bonus ? 6f * scale : 0f;
        float starW = 0f;
        if (bonus)
            using (ImRaii.PushFont(UiBuilder.IconFont))
                starW = ImGui.CalcTextSize(star).X;
        ImGui.SetWindowFontScale(1f);

        var chipW = padX * 2 + textSize.X + (bonus ? starGap + starW : 0f);
        var chipH = textSize.Y + padY * 2;
        var origin = new Vector2(x, y);
        var end = origin + new Vector2(chipW, chipH);

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, accent, 0.30f)), 4f);
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.WithAlpha(accent, 0.65f)), 4f);

        PutScaled(text, x + padX, y + padY, accentSoft, chipScale);
        if (bonus)
        {
            ImGui.SetWindowFontScale(chipScale);
            using (ImRaii.PushFont(UiBuilder.IconFont))
                Put(star, x + padX + textSize.X + starGap, y + padY, Styling.AccentAmber);
            ImGui.SetWindowFontScale(1f);
        }

        return chipH;
    }

    private static (Vector4 accent, Vector4 accentSoft, string label) PhasePalette(AutoFateController controller, bool inFate)
    {
        if (!controller.Running)
            return (Styling.TextDim, Styling.TextSecondary, "READY");

        return controller.Phase switch
        {
            AutoPhase.Trading    => (Styling.AccentAmber, Styling.AccentAmberSoft, "TRADING GEMSTONES"),
            AutoPhase.Repairing  => (Styling.TextStrong,  Styling.TextSecondary,   "REPAIRING GEAR"),
            AutoPhase.Humanizing => (Styling.AccentMint,  Styling.AccentMintSoft,  "ON A BREAK"),
            AutoPhase.Finishing  => (Styling.AccentMint,  Styling.AccentMintSoft,  "FINISHING UP"),
            AutoPhase.Grinding   => (Styling.AccentBlue,  Styling.AccentBlueSoft,  inFate ? "ENGAGING FATE" : "GRINDING FATES"),
            _                    => (Styling.TextDim,     Styling.TextSecondary,   "STANDING BY"),
        };
    }

    private static void DrawStatTiles(Configuration cfg, AutoFateController controller)
    {
        var s = controller.SessionSnapshot;
        var scale = ImGuiHelpers.GlobalScale;
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = 6f * scale;
        var tileW = (avail - gap * 3f) / 4f;

        var completed = s?.CompletedCount ?? 0;
        var fph = s?.FatesPerHour ?? 0;
        var gems = s?.GemstonesEarned ?? 0;
        var hours = s?.Elapsed.TotalHours ?? 0;
        var gph = hours > 0 ? gems / hours : 0;

        var info = GoalProgress.Resolve(cfg, s);
        var elapsedVal = s is null ? "0m 00s" : Formatting.Elapsed(s.Elapsed);
        var elapsedSub = info.Endless ? "" : info.Remaining;

        StatTile.Draw("FATEs", completed.ToString(), null, Styling.AccentBlue, tileW);
        ImGui.SameLine(0, gap);
        StatTile.Draw("Gems", gems.ToString(), gph >= 1 ? $"{gph:F0} /h" : null, Styling.AccentAmber, tileW);
        ImGui.SameLine(0, gap);
        StatTile.Draw("FATEs/h", fph > 0 ? $"{fph:F1}" : "—", null, Styling.AccentMint, tileW);
        ImGui.SameLine(0, gap);
        StatTile.Draw("Elapsed", elapsedVal, string.IsNullOrEmpty(elapsedSub) ? null : elapsedSub, Styling.AccentViolet, tileW);
    }

    private const int QueuePreviewCount = 5;
    private static readonly List<PublicEvent> queueBuffer = new();

    private static void DrawQueue(Configuration cfg)
    {
        Styling.SectionLabel("Up Next");
        Styling.VSpace(2f);

        var player = Svc.Objects.LocalPlayer;
        if (player is null) { EmptyHint("Player not loaded."); return; }

        var current = PublicEvent.CurrentFate;
        FateScanner.CollectEligible(cfg, player.Position, current?.Id, queueBuffer);
        if (queueBuffer.Count == 0) { EmptyHint("No other eligible FATEs in this zone."); return; }

        var count = Math.Min(QueuePreviewCount, queueBuffer.Count);
        for (var index = 0; index < count; index++)
        {
            DrawQueueRow(queueBuffer[index], player.Position, index == 0);
            ImGui.Spacing();
        }
    }

    private static void DrawQueueRow(PublicEvent fate, Vector3 playerPos, bool emphasize)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowHeight = Layout.QueueRowHeight * scale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var end = origin + new Vector2(width, rowHeight);
        var dl = ImGui.GetWindowDrawList();

        var accent = fate.HasBonus ? Styling.AccentAmber : Styling.AccentViolet;
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBgSoft), 6f);
        dl.AddRect(origin, end,
            ImGui.GetColorU32(Styling.WithAlpha(emphasize ? accent : Styling.BorderDim, emphasize ? 0.8f : 0.5f)),
            6f, ImDrawFlags.None, emphasize ? 1.5f : 1f);

        var padX = 11f * scale;
        var topY = origin.Y + 7f * scale;

        var icon = (fate.HasBonus ? FontAwesomeIcon.Star : FontAwesomeIcon.Bolt).ToIconString();
        var iconColor = fate.HasBonus ? Styling.AccentAmber : Styling.TextDim;
        float iconW;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            iconW = ImGui.CalcTextSize(icon).X;
            Put(icon, origin.X + padX, topY, iconColor);
        }

        Put($"L{fate.Level}   {fate.Name}", origin.X + padX + iconW + 9f * scale, topY, Styling.TextStrong);

        var dist = (int)Math.Round(Vector3.Distance(playerPos, fate.Position));
        PutRight($"{fate.Progress}%   ·   {Formatting.Time(fate.TimeRemaining)}   ·   {dist}y", end.X - padX, topY, Styling.TextDim);

        DrawBar(new Vector2(origin.X + padX, origin.Y + rowHeight - 12f * scale),
            width - padX * 2f, Layout.QueueBarHeight * scale, fate.Progress / 100f, accent, 3f);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, rowHeight));
    }

    private static void DrawFooter(Configuration cfg)
    {
        var current = Svc.ClientState.TerritoryType;
        var zone = ZoneRegistry.Zones.FirstOrDefault(z => z.TerritoryId == current);
        var name = zone?.Name ?? "(somewhere else)";
        var queued = cfg.SelectedZones.Count;
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextUnformatted($"{name}   ·   {queued} zone{(queued == 1 ? "" : "s")} in rotation");
    }

    private static void DrawBar(Vector2 origin, float width, float height, float fraction, Vector4 color, float rounding)
    {
        var dl = ImGui.GetWindowDrawList();
        var end = origin + new Vector2(width, height);
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBgSoft), rounding);
        if (fraction > 0)
        {
            var fillEnd = new Vector2(origin.X + width * Math.Clamp(fraction, 0f, 1f), end.Y);
            dl.AddRectFilled(origin, fillEnd, ImGui.GetColorU32(color * 0.9f), rounding);
        }
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.WithAlpha(Styling.BorderDim, 0.6f)), rounding);
    }

    private static void DrawIndeterminateBar(Vector2 origin, float width, float height, Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList();
        var end = origin + new Vector2(width, height);
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBgSoft), 4f);

        var segW = width * 0.32f;
        var x0 = origin.X - segW + (width + segW) * Styling.Phase(1400.0);
        var segMin = new Vector2(Math.Max(origin.X, x0), origin.Y);
        var segMax = new Vector2(Math.Min(end.X, x0 + segW), end.Y);
        if (segMax.X > segMin.X)
            dl.AddRectFilled(segMin, segMax, ImGui.GetColorU32(color * 0.9f), 4f);

        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.WithAlpha(Styling.BorderDim, 0.6f)), 4f);
    }

    private static void EmptyHint(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextUnformatted(text);
    }

    private static string Truncate(string text, float maxWidth, float fontScale)
    {
        ImGui.SetWindowFontScale(fontScale);
        if (ImGui.CalcTextSize(text).X <= maxWidth) { ImGui.SetWindowFontScale(1f); return text; }
        const string ell = "…";
        var t = text;
        while (t.Length > 1 && ImGui.CalcTextSize(t + ell).X > maxWidth) t = t[..^1];
        ImGui.SetWindowFontScale(1f);
        return t + ell;
    }

    private static float MeasureHeight(string text, float fontScale)
    {
        ImGui.SetWindowFontScale(fontScale);
        var h = ImGui.CalcTextSize(text).Y;
        ImGui.SetWindowFontScale(1f);
        return h;
    }

    private static void Put(string text, float x, float y, Vector4 color)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
    }

    private static void PutScaled(string text, float x, float y, Vector4 color, float fontScale)
    {
        ImGui.SetWindowFontScale(fontScale);
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1f);
    }

    private static void PutRightScaled(string text, float rightX, float y, Vector4 color, float fontScale)
    {
        ImGui.SetWindowFontScale(fontScale);
        var w = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorScreenPos(new Vector2(rightX - w, y));
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1f);
    }

    private static void PutRight(string text, float rightX, float y, Vector4 color)
        => Put(text, rightX - ImGui.CalcTextSize(text).X, y, color);
}
