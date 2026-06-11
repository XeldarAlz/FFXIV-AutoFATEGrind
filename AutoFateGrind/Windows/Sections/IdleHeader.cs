using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

// Resting-state header: an activity teaser + toolbar strip, then an always-on status card that tells the
// user at a glance whether they're ready, what's blocking them, and how the last run went.
internal static class IdleHeader
{
    public static void Draw(Configuration cfg, Plugin plugin)
    {
        DrawTopStrip(plugin);
        DrawStatusCard(cfg);
    }

    private static void DrawTopStrip(Plugin plugin)
    {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentViolet))
            ImGui.TextUnformatted(FontAwesomeIcon.Bolt.ToIconString());

        ImGui.SameLine(0, 6f);
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
            ImGui.TextUnformatted("Open-World FATEs");

        ImGui.SameLine(0, 8f);
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextUnformatted("· Field Operations soon");

        TopToolbar.DrawIconsInline(plugin);
    }

    private static void DrawStatusCard(Configuration cfg)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = Layout.IdleStatusHeight * scale;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(width, height);
        var dl = ImGui.GetWindowDrawList();

        var (accent, icon, label, sub) = Resolve(cfg);

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, accent, 0.10f)), Styling.CardRounding);
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.WithAlpha(accent, 0.55f)), Styling.CardRounding, ImDrawFlags.None, 1.5f);

        var padX = 14f * scale;
        var midY = origin.Y + height * 0.5f;
        var iconBox = 24f * scale;
        ProgressRing.CenterIcon(new Vector2(origin.X + padX + iconBox * 0.5f, midY), icon, accent, iconBox);

        var lineH = ImGui.GetTextLineHeight();
        var gap = 3f * scale;
        var topY = midY - (lineH * 2 + gap) * 0.5f;
        var textX = origin.X + padX + iconBox + 12f * scale;

        Put(label, textX, topY, Styling.TextStrong);
        Put(sub, textX, topY + lineH + gap, Styling.TextDim);

        var (rt, rd) = LastRun();
        PutRight(rt, end.X - padX, topY, Styling.TextSecondary);
        PutRight(rd, end.X - padX, topY + lineH + gap, Styling.TextDim);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static (Vector4 accent, FontAwesomeIcon icon, string label, string sub) Resolve(Configuration cfg)
    {
        if (!ExternalPlugins.AllRequiredInstalled())
            return (Styling.AccentRose, FontAwesomeIcon.ExclamationTriangle, "SETUP NEEDED",
                "Install the required plugins — open the plug menu.");

        var zones = ZoneSelection.ResolveStartList(cfg).Count;
        if (zones == 0)
            return (Styling.AccentAmber, FontAwesomeIcon.MapMarkedAlt, "PICK YOUR ZONES",
                "Tick zones below to build your grind order.");

        return (Styling.AccentMint, FontAwesomeIcon.CheckCircle, "READY TO GRIND",
            $"{zones} zone{(zones == 1 ? "" : "s")}  ·  {StopSummary(cfg)}");
    }

    private static string StopSummary(Configuration cfg) => cfg.ActiveMode.Id switch
    {
        MaxGemstonesMode.ModeId => $"stops at {cfg.TargetGemstoneCount} gems",
        RunCountMode.ModeId     => $"stops after {cfg.TargetFateCount} FATEs",
        TimeBoxedMode.ModeId    => $"stops after {cfg.TargetMinutes} min",
        EndlessMode.ModeId      => "runs until you stop",
        _                       => "ready",
    };

    private static (string title, string detail) LastRun()
    {
        var records = Plugin.Instance.History.Records;
        if (records.Count == 0)
            return ("No runs yet", "your stats will appear here");

        var r = records[0];
        return ($"Last run  ·  {r.FatesCompleted} FATEs", $"{Formatting.Elapsed(r.Duration)}  ·  {r.GemstonesEarned} gems");
    }

    private static void Put(string text, float x, float y, Vector4 color)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
    }

    private static void PutRight(string text, float rightX, float y, Vector4 color)
        => Put(text, rightX - ImGui.CalcTextSize(text).X, y, color);
}
