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

internal static class RunningPanel
{
    public static void Draw(Configuration cfg, AutoFateController controller)
    {
        DrawHeaderStrip();
        DrawStatusCard(controller);
        ImGui.Spacing();

        if (PrimaryButton.Draw("STOP", Styling.AccentRose, true))
            controller.Stop();

        ImGui.Spacing();
        ImGui.Spacing();
        DrawQueue(cfg);
        ImGui.Spacing();
        DrawFooter(cfg);
    }

    private static void DrawHeaderStrip()
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted("STATUS");
        TopToolbar.DrawIconsInline(Plugin.Instance);
    }

    private static void DrawStatusCard(AutoFateController controller)
    {
        var height = Layout.StatusCardHeight * ImGuiHelpers.GlobalScale;
        var fate = PublicEvent.CurrentFate;
        var inFate = fate is not null && fate.State == FateState.Running;

        var (accent, accentSoft, label) = PhasePalette(controller, inFate);
        var active = controller.Running;

        var border = active
            ? Styling.PulseColor(accent, accentSoft, inFate ? Styling.PulseFast : Styling.PulseMedium)
            : Styling.BorderDim;
        var bg = active ? Vector4.Lerp(Styling.CardBg, accent, 0.08f) : Styling.CardBgSoft;

        using (Card.Begin("##afg_status", new Vector2(-1, height), bg, border, active ? 2f : 1.0f))
        {
            DrawPhaseLabel(label, accent, active);
            if (inFate) DrawActive(fate!, controller, accent);
            else DrawTransitioning(controller);
        }
    }

    private static (Vector4 accent, Vector4 accentSoft, string label) PhasePalette(AutoFateController controller, bool inFate)
    {
        if (!controller.Running)
            return (Styling.TextDim, Styling.TextSecondary, "READY");

        return controller.Phase switch
        {
            AutoPhase.Trading  => (Styling.AccentAmber, Styling.AccentAmberSoft, "TRADING GEMSTONES"),
            AutoPhase.Grinding => (Styling.AccentBlue,  Styling.AccentBlueSoft,  inFate ? "ENGAGING FATE" : "GRINDING FATES"),
            _                  => (Styling.TextDim,    Styling.TextSecondary,   "STANDING BY"),
        };
    }

    private static void DrawPhaseLabel(string label, Vector4 accent, bool active)
    {
        ImGui.SetWindowFontScale(0.9f);
        using (ImRaii.PushColor(ImGuiCol.Text, active ? accent : Styling.TextDim))
            ImGui.TextUnformatted(label);
        ImGui.SetWindowFontScale(1.0f);
    }

    private static void DrawActive(PublicEvent fate, AutoFateController controller, Vector4 accent)
    {
        ImGui.SetWindowFontScale(1.45f);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            ImGui.TextUnformatted($"L{fate.Level}   {fate.Name}");
        ImGui.SetWindowFontScale(1.0f);

        if (fate.HasBonus)
        {
            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
                ImGui.TextUnformatted(FontAwesomeIcon.Star.ToIconString());
        }

        ImGui.Spacing();
        DrawFatProgressBar(fate.Progress / 100f, accent);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted($"{fate.Progress}%   ·   {FormatTime(fate.TimeRemaining)} remaining");

        ImGui.Spacing();
        DrawSessionLine(controller);
    }

    private static void DrawTransitioning(AutoFateController controller)
    {
        ImGui.SetWindowFontScale(1.30f);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(controller.Status) ? "Waiting..." : controller.Status);
        ImGui.SetWindowFontScale(1.0f);

        ImGui.Spacing();
        DrawSessionLine(controller);
    }

    private static void DrawSessionLine(AutoFateController controller)
    {
        var s = controller.SessionSnapshot;
        if (s is null) return;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 4f * ImGuiHelpers.GlobalScale);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted($"Session:  {s.CompletedCount} FATEs  ·  {s.GemstonesEarned} gems  ·  {FormatElapsed(s.Elapsed)}  ·  {s.FatesPerHour:F1}/h");
    }

    private static void DrawQueue(Configuration cfg)
    {
        Styling.SectionLabel("Up Next");
        var player = Svc.Objects.LocalPlayer;
        if (player is null)
        {
            EmptyHint("Player not loaded.");
            return;
        }
        var fates = (PublicEvent.Fates ?? Enumerable.Empty<PublicEvent>())
            .Where(f => f.State == FateState.Running)
            .Where(f => !cfg.BlacklistedFateIds.Contains(f.Id))
            .OrderByDescending(f => f.HasBonus)
            .ThenByDescending(f => f.Progress)
            .ThenBy(f => Vector3.DistanceSquared(f.Position, player.Position))
            .ThenBy(f => f.TimeRemaining)
            .Take(5)
            .ToArray();
        if (fates.Length == 0) { EmptyHint("No other active FATEs in this zone."); return; }

        var current = PublicEvent.CurrentFate;
        foreach (var f in fates)
        {
            if (current is not null && f.Id == current.Id) continue;
            DrawQueueRow(f, player.Position);
            ImGui.Spacing();
        }
    }

    private static void DrawQueueRow(PublicEvent fate, Vector3 playerPos)
    {
        var rowHeight = Layout.QueueRowHeight * ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var end = origin + new Vector2(width, rowHeight);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBgSoft), 5f);

        var padX = 10f * ImGuiHelpers.GlobalScale;
        var topY = origin.Y + 6f * ImGuiHelpers.GlobalScale;

        var iconColor = fate.HasBonus ? Styling.AccentAmber : Styling.TextDim;
        var icon = fate.HasBonus ? FontAwesomeIcon.Star : FontAwesomeIcon.Bolt;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, topY));
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, iconColor))
            ImGui.TextUnformatted(icon.ToIconString());

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            ImGui.TextUnformatted($"L{fate.Level}   {fate.Name}");

        var dist = (int)Math.Round(Vector3.Distance(playerPos, fate.Position));
        var right = $"{fate.Progress}%   ·   {FormatTime(fate.TimeRemaining)}   ·   {dist}y";
        var rightSize = ImGui.CalcTextSize(right);
        ImGui.SetCursorScreenPos(new Vector2(end.X - rightSize.X - padX, topY));
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(right);

        var barOrigin = new Vector2(origin.X + padX, origin.Y + rowHeight - 12f * ImGuiHelpers.GlobalScale);
        var barWidth = width - padX * 2;
        var barColor = fate.HasBonus ? Styling.AccentAmber : Styling.AccentViolet;
        DrawProgressBar(barOrigin, barWidth, fate.Progress / 100f, barColor);

        ImGui.Dummy(new Vector2(width, rowHeight));
    }

    private static void DrawFooter(Configuration cfg)
    {
        var current = Svc.ClientState.TerritoryType;
        var zone = ZoneRegistry.Zones.FirstOrDefault(z => z.TerritoryId == current);
        var name = zone?.Name ?? "(somewhere else)";
        var queued = ZoneSelection.IsAutoSelected(cfg)
            ? ZoneSelection.AutoQueue(cfg).Count
            : cfg.SelectedZones.Count;
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextUnformatted($"in: {name}   ·   {queued} zone{(queued == 1 ? "" : "s")} queued");
    }

    private static void DrawFatProgressBar(float fraction, Vector4 color)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var height = 18f * ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(width, height);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBgSoft), 6f);
        if (fraction > 0)
        {
            var fillEnd = new Vector2(origin.X + width * Math.Clamp(fraction, 0f, 1f), end.Y);
            dl.AddRectFilled(origin, fillEnd, ImGui.GetColorU32(color * 0.9f), 6f);
        }
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.BorderDim), 6f);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawProgressBar(Vector2 origin, float width, float fraction, Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList();
        var height = Layout.QueueBarHeight * ImGuiHelpers.GlobalScale;
        var end = origin + new Vector2(width, height);
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBg), 3f);
        if (fraction > 0)
        {
            var fillEnd = new Vector2(origin.X + width * Math.Clamp(fraction, 0f, 1f), end.Y);
            dl.AddRectFilled(origin, fillEnd, ImGui.GetColorU32(color * 0.85f), 3f);
        }
    }

    private static void EmptyHint(string text)
    {
        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextUnformatted(text);
    }

    private static string FormatTime(float secs)
    {
        if (secs <= 0) return "--:--";
        var s = (int)secs;
        return $"{s / 60}:{s % 60:D2}";
    }

    private static string FormatElapsed(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:D2}m";
        return $"{t.Minutes}m {t.Seconds:D2}s";
    }
}
