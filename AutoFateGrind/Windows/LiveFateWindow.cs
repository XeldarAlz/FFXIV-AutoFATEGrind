using AutoFateGrind.Core.Tasks;
using clib.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;

namespace AutoFateGrind.Windows;

public sealed class LiveFateWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public LiveFateWindow(Plugin plugin) : base("Live FATEs###AutoFateGrindLive")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(280, 120),
            MaximumSize = new Vector2(520, 600),
        };
        Size = new Vector2(320, 0);
        SizeCondition = ImGuiCond.FirstUseEver;
        RespectCloseHotkey = true;
    }

    public void Dispose() { }

    public override void OnClose()
    {
        if (!Plugin.Cfg.ShowLivePopout) return;
        Plugin.Cfg.ShowLivePopout = false;
        Plugin.Cfg.Save();
    }

    public override void Draw()
    {
        using var style = Styling.PushWindowStyle();

        var fate = PublicEvent.CurrentFate;
        var inFate = fate is not null && fate.State == FateState.Running;

        if (inFate) DrawActive(fate!);
        else        DrawIdle(plugin.Controller);

        ImGui.Separator();
        DrawQueue();
        ImGui.Separator();
        DrawSession(plugin.Controller);
    }

    private static void DrawActive(PublicEvent fate)
    {
        ImGui.SetWindowFontScale(0.85f);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted("ENGAGING");
        ImGui.SetWindowFontScale(1.0f);

        ImGui.SetWindowFontScale(1.10f);
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

        DrawBar(fate.Progress / 100f, Styling.AccentViolet);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted($"{fate.Progress}%   ·   {FormatTime(fate.TimeRemaining)}");
    }

    private static void DrawIdle(AutoFateController controller)
    {
        ImGui.SetWindowFontScale(0.85f);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(controller.Running ? "STANDING BY" : "READY");
        ImGui.SetWindowFontScale(1.0f);

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
            ImGui.TextUnformatted(controller.Running ? controller.Status : "No FATE engaged.");
    }

    private void DrawQueue()
    {
        var cfg = plugin.Configuration;
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return;

        var fates = (PublicEvent.Fates ?? Enumerable.Empty<PublicEvent>())
            .Where(f => f.State == FateState.Running)
            .Where(f => !cfg.BlacklistedFateIds.Contains(f.Id))
            .OrderByDescending(f => f.HasBonus)
            .ThenBy(f => f.TimeRemaining)
            .ThenBy(f => Vector3.DistanceSquared(f.Position, player.Position))
            .Take(3)
            .ToArray();

        var current = PublicEvent.CurrentFate;
        var any = false;
        foreach (var f in fates)
        {
            if (current is not null && f.Id == current.Id) continue;
            any = true;
            DrawCompactRow(f);
        }
        if (!any)
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No other FATEs.");
    }

    private static void DrawCompactRow(PublicEvent fate)
    {
        var iconColor = fate.HasBonus ? Styling.AccentAmber : Styling.TextDim;
        var icon = fate.HasBonus ? FontAwesomeIcon.Star : FontAwesomeIcon.Bolt;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, iconColor))
            ImGui.TextUnformatted(icon.ToIconString());

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
            ImGui.TextUnformatted($"L{fate.Level} {fate.Name}");

        var right = $"{fate.Progress}%  {FormatTime(fate.TimeRemaining)}";
        var rightSize = ImGui.CalcTextSize(right);
        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - rightSize.X);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(right);
    }

    private static void DrawSession(AutoFateController controller)
    {
        var s = controller.SessionSnapshot;
        if (s is null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No session.");
            return;
        }
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted($"{s.CompletedCount} FATEs · {s.GemstonesEarned} gems · {FormatElapsed(s.Elapsed)}");
    }

    private static void DrawBar(float fraction, Vector4 color)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var height = 10f * ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(width, height);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBgSoft), 4f);
        if (fraction > 0)
        {
            var fillEnd = new Vector2(origin.X + width * Math.Clamp(fraction, 0f, 1f), end.Y);
            dl.AddRectFilled(origin, fillEnd, ImGui.GetColorU32(color * 0.85f), 4f);
        }
        ImGui.Dummy(new Vector2(width, height));
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
