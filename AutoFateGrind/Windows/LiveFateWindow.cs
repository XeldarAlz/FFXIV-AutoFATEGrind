using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Windows.Components;
using clib.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;

namespace AutoFateGrind.Windows;

public sealed class LiveFateWindow : Window, IDisposable
{
    private const float ContentWidth = 330f;
    private const float HeaderHeight = 30f;
    private const float RowHeight = 26f;
    private const int QueueLength = 3;

    private readonly Plugin plugin;
    private IDisposable? chrome;
    private IDisposable? bodyFont;

    public LiveFateWindow(Plugin plugin) : base("Live FATEs###AutoFateGrindLive",
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(100, 60),
            MaximumSize = new Vector2(600, 700),
        };
        RespectCloseHotkey = true;
    }

    public void Dispose() { }

    public override void OnClose()
    {
        if (!Plugin.Cfg.ShowLivePopout) return;
        Plugin.Cfg.ShowLivePopout = false;
        Plugin.Cfg.Save();
    }

    public override void PreDraw()
    {
        bodyFont = Fonts.PushBody();
        chrome = Styling.PushChrome(new Vector2(14f, 12f));
    }

    public override void PostDraw()
    {
        chrome?.Dispose();
        chrome = null;
        bodyFont?.Dispose();
        bodyFont = null;
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ContentWidth * scale;
        var controller = plugin.Controller;
        var fate = PublicEvent.CurrentFate;
        var inFate = fate is not null && fate.State == FateState.Running;

        DrawHeader(width, controller);
        Styling.VSpace(4f);

        if (inFate) DrawActive(fate!, width);
        else DrawIdle(controller, width);

        Paint.Divider(6f);
        DrawQueue(width);
        Paint.Divider(6f);
        DrawSession(controller, width);
    }

    private void DrawHeader(float width, AutoFateController controller)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = HeaderHeight * scale;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var buttonSize = 24f * scale;

        ImGui.InvisibleButton("##afg_live_drag", new Vector2(width - buttonSize - 6f * scale, height));
        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta;
            if (delta != Vector2.Zero) ImGui.SetWindowPos(ImGui.GetWindowPos() + delta, ImGuiCond.Always);
        }

        var midY = origin.Y + height * 0.5f;
        var accent = controller.Paused ? Styling.AccentAmber : controller.Running ? Styling.AccentBlue : Styling.TextDim;
        var dot = controller.Running && !controller.Paused ? Styling.PulseColor(accent, Styling.AccentBlueSoft, Styling.PulseMedium) : accent;
        Paint.Dot(dl, new Vector2(origin.X + 6f * scale, midY), 3.5f * scale, dot);

        var title = Loc.T(L.Live.Title);
        var label = TextDraw.SmallCapsSize(title);
        TextDraw.SmallCaps(title, new Vector2(origin.X + 18f * scale, midY - label.Y * 0.5f), Styling.TextSecondary);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonSize, midY - buttonSize * 0.5f));
        if (IconButton.Draw(FontAwesomeIcon.Times, "##afg_live_close", buttonSize, tooltip: Loc.T(L.Live.Hide)))
        {
            IsOpen = false;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawActive(PublicEvent fate, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var y = origin.Y;

        var phase = Loc.T(L.Live.Engaging);
        var phaseSize = TextDraw.SmallCapsSize(phase);
        TextDraw.SmallCaps(phase, new Vector2(origin.X, y), Styling.AccentBlueSoft);
        y += phaseSize.Y + 4f * scale;

        using (Fonts.PushHeadline())
        {
            var starWidth = fate.HasBonus ? TextDraw.IconSize(FontAwesomeIcon.Star).X + 8f * scale : 0f;
            var name = TextDraw.Truncate($"L{fate.Level}   {fate.Name}", width - starWidth);
            var nameSize = TextDraw.Measure(name);
            TextDraw.At(name, new Vector2(origin.X, y), Styling.TextStrong);
            if (fate.HasBonus)
            {
                var starSize = TextDraw.IconSize(FontAwesomeIcon.Star);
                TextDraw.Icon(FontAwesomeIcon.Star, new Vector2(origin.X + nameSize.X + 8f * scale, y + (nameSize.Y - starSize.Y) * 0.5f), Styling.AccentAmber);
            }

            y += nameSize.Y + 8f * scale;
        }

        var barHeight = 9f * scale;
        Paint.Bar(dl, new Vector2(origin.X, y), width, barHeight, fate.Progress / 100f, Styling.AccentBlue);
        y += barHeight + 6f * scale;

        using (Fonts.PushCaption())
        {
            var meta = Loc.T(L.Run.FateProgress, fate.Progress, Formatting.Time(fate.TimeRemaining));
            TextDraw.At(meta, new Vector2(origin.X, y), Styling.TextDim);
            y += TextDraw.Measure(meta).Y;
        }

        ImGui.Dummy(new Vector2(width, y - origin.Y));
    }

    private static void DrawIdle(AutoFateController controller, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var label = controller.Paused ? Loc.T(L.Live.Paused) : controller.Running ? Loc.T(L.Live.StandingBy) : Loc.T(L.Live.Ready);
        var color = controller.Paused ? Styling.AccentAmberSoft : Styling.TextDim;
        var labelSize = TextDraw.SmallCapsSize(label);
        TextDraw.SmallCaps(label, origin, color);

        var status = TextDraw.Truncate(controller.Running ? controller.Status : Loc.T(L.Live.NoFate), width);
        var statusSize = TextDraw.Measure(status);
        TextDraw.At(status, new Vector2(origin.X, origin.Y + labelSize.Y + 4f * scale), Styling.TextSecondary);

        ImGui.Dummy(new Vector2(width, labelSize.Y + 4f * scale + statusSize.Y));
    }

    private void DrawQueue(float width)
    {
        var cfg = plugin.Configuration;
        var player = Svc.Objects.LocalPlayer;
        if (player is null)
        {
            Hint(Loc.T(L.Common.PlayerNotLoaded), width);
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
            Hint(Loc.T(L.Live.NoOtherFates), width);
            return;
        }

        for (var index = 0; index < fates.Length; index++)
        {
            DrawCompactRow(fates[index], width);
        }
    }

    private static void DrawCompactRow(PublicEvent fate, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = RowHeight * scale;
        var origin = ImGui.GetCursorScreenPos();
        var midY = origin.Y + height * 0.5f;
        var buttonSize = 22f * scale;

        var icon = fate.HasBonus ? FontAwesomeIcon.Star : FontAwesomeIcon.Bolt;
        var iconColor = fate.HasBonus ? Styling.AccentAmber : Styling.TextDim;
        var iconSize = TextDraw.IconSize(icon);
        TextDraw.Icon(icon, new Vector2(origin.X, midY - iconSize.Y * 0.5f), iconColor);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonSize, midY - buttonSize * 0.5f));
        ImGui.PushID((nint)fate.Id);
        if (IconButton.Draw(FontAwesomeIcon.Ban, "##ban", buttonSize, Styling.AccentRose, Loc.T(L.Live.Ban)))
        {
            FateBlacklist.ToggleId(Plugin.Cfg, fate);
        }

        ImGui.PopID();

        string meta;
        Vector2 metaSize;
        using (Fonts.PushCaption())
        {
            meta = Loc.T(L.Live.QueueMeta, fate.Progress, Formatting.Time(fate.TimeRemaining));
            metaSize = TextDraw.Measure(meta);
            TextDraw.At(meta, new Vector2(origin.X + width - buttonSize - 8f * scale - metaSize.X, midY - metaSize.Y * 0.5f), Styling.TextDim);
        }

        var nameX = origin.X + iconSize.X + 8f * scale;
        var name = TextDraw.Truncate($"L{fate.Level} {fate.Name}", origin.X + width - buttonSize - 14f * scale - metaSize.X - nameX);
        var nameSize = TextDraw.Measure(name);
        TextDraw.At(name, new Vector2(nameX, midY - nameSize.Y * 0.5f), Styling.TextSecondary);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawSession(AutoFateController controller, float width)
    {
        var session = controller.SessionSnapshot;
        if (session is null)
        {
            Hint(Loc.T(L.Live.NoSession), width);
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var line = Loc.T(L.Live.Session, session.CompletedCount, session.GemstonesEarned, Formatting.Elapsed(session.Elapsed));
        var lineSize = TextDraw.Measure(line);
        TextDraw.At(line, origin, Styling.TextDim);
        var height = lineSize.Y;

        if (session.ExpEarned > 0)
        {
            using (Fonts.PushCaption())
            {
                var exp = Loc.T(L.Live.Exp, Formatting.Exp(session.ExpEarned), Formatting.Exp((long)session.ExpPerHour));
                TextDraw.At(exp, new Vector2(origin.X, origin.Y + height + 2f * scale), Styling.TextMuted);
                height += 2f * scale + TextDraw.Measure(exp).Y;
            }
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private static void Hint(string text, float width)
    {
        var origin = ImGui.GetCursorScreenPos();
        TextDraw.At(text, origin, Styling.TextMuted);
        ImGui.Dummy(new Vector2(width, TextDraw.Measure(text).Y));
    }
}
