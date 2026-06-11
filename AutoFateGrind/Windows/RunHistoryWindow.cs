using AutoFateGrind.Core.Stats;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoFateGrind.Windows;

public sealed class RunHistoryWindow : Window, IDisposable
{
    private bool confirmClear;

    public RunHistoryWindow() : base("Auto FATE Grind — Run History###AutoFateGrindHistory")
    {
        Size = new Vector2(660, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(100, 100),
            MaximumSize = new Vector2(2000, 1600),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        using var style = Styling.PushWindowStyle();

        var history = Plugin.Instance.History;
        DrawLifetime(history.Lifetime);
        ImGui.Spacing();
        ImGui.Spacing();

        Styling.SectionLabel(history.Records.Count > 0 ? $"Recent runs  ·  {history.Records.Count}" : "Recent runs");
        if (history.Records.Count == 0)
        {
            ImGui.Spacing();
            DrawEmptyState();
            return;
        }

        ImGui.Spacing();
        DrawRunTable(history);

        ImGui.Spacing();
        DrawClearControl(history);
    }

    private static void DrawLifetime(RunHistory.LifetimeTotals t)
    {
        Styling.SectionLabel("Lifetime");
        ImGui.Spacing();

        var s = ImGuiHelpers.GlobalScale;
        var gap = 7f * s;
        var avail = ImGui.GetContentRegionAvail().X;
        var tileW = (avail - gap * 4f) / 5f;
        var size = new Vector2(tileW, 60f * s);

        StatTile(FontAwesomeIcon.Flag, t.Runs.ToString("N0"), "Runs", Styling.AccentViolet, size);
        ImGui.SameLine(0, gap);
        StatTile(FontAwesomeIcon.Bolt, t.Fates.ToString("N0"), "FATEs", Styling.AccentBlue, size);
        ImGui.SameLine(0, gap);
        StatTile(FontAwesomeIcon.Gem, t.Gemstones.ToString("N0"), "Gems", Styling.AccentAmber, size);
        ImGui.SameLine(0, gap);
        StatTile(FontAwesomeIcon.Star, Formatting.Exp(t.Exp), "Exp", Styling.AccentMint, size);
        ImGui.SameLine(0, gap);
        StatTile(FontAwesomeIcon.ArrowUp, t.Levels > 0 ? $"+{t.Levels}" : "0", "Levels", Styling.AccentPink, size);

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted($"{Formatting.Elapsed(t.Duration)} grinding   ·   {t.FatesPerHour:F1} FATEs/h average");
    }

    private static void StatTile(FontAwesomeIcon icon, string value, string caption, Vector4 accent, Vector2 size)
    {
        var s = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, accent, 0.10f)), Styling.CardRounding);
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.BorderDim), Styling.CardRounding, ImDrawFlags.None, 1f);
        dl.AddRectFilled(origin, new Vector2(end.X, origin.Y + 3f * s), ImGui.GetColorU32(accent), Styling.CardRounding, ImDrawFlags.RoundCornersTop);

        var pad = 10f * s;
        var iconStr = icon.ToIconString();
        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            iconSize = ImGui.CalcTextSize(iconStr);

        var topY = origin.Y + 9f * s;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, topY));
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, accent))
            ImGui.TextUnformatted(iconStr);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad + iconSize.X + 6f * s, topY + (iconSize.Y - ImGui.GetTextLineHeight()) * 0.5f));
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(caption);

        ImGui.SetWindowFontScale(1.45f);
        var valSize = ImGui.CalcTextSize(value);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, end.Y - valSize.Y - 8f * s));
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            ImGui.TextUnformatted(value);
        ImGui.SetWindowFontScale(1f);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    private static void DrawEmptyState()
    {
        var s = ImGuiHelpers.GlobalScale;
        var size = new Vector2(-1, 88f * s);
        using (Components.Card.Begin("##afg_hist_empty", size, Styling.CardBgSoft, Styling.BorderDim))
        {
            var icon = FontAwesomeIcon.History.ToIconString();
            ImGui.SetWindowFontScale(1.6f);
            Vector2 iconSize;
            using (ImRaii.PushFont(UiBuilder.IconFont))
                iconSize = ImGui.CalcTextSize(icon);
            ImGui.SetCursorPosX((ImGui.GetWindowSize().X - iconSize.X) * 0.5f);
            using (ImRaii.PushFont(UiBuilder.IconFont))
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted(icon);
            ImGui.SetWindowFontScale(1f);

            ImGui.Spacing();
            var msg = "No runs recorded yet. Finish (or stop) a grind and it'll show up here.";
            ImGui.SetCursorPosX((ImGui.GetWindowSize().X - ImGui.CalcTextSize(msg).X) * 0.5f);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted(msg);
        }
    }

    private static void DrawRunTable(RunHistory history)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX;

        using var rowPad = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(8, 5) * ImGuiHelpers.GlobalScale);
        using var table = ImRaii.Table("##afg_history", 7, flags, new Vector2(-1, -44 * ImGuiHelpers.GlobalScale));
        if (!table) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("FATEs", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Gems", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Exp", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Levels", ImGuiTableColumnFlags.WidthStretch, 0.7f);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        HeaderCell(FontAwesomeIcon.Clock, "When");
        HeaderCell(FontAwesomeIcon.User, "Job");
        HeaderCell(FontAwesomeIcon.Stopwatch, "Time");
        HeaderCell(FontAwesomeIcon.Bolt, "FATEs");
        HeaderCell(FontAwesomeIcon.Gem, "Gems");
        HeaderCell(FontAwesomeIcon.Star, "Exp");
        HeaderCell(FontAwesomeIcon.ArrowUp, "Levels");

        var i = 0;
        foreach (var r in history.Records)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Selectable($"##afg_run{i++}", false, ImGuiSelectableFlags.SpanAllColumns);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(RunTooltip(r));
            ImGui.SameLine(0, 0);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
                ImGui.TextUnformatted(RelativeTime(r.EndedAtUtc));

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, string.IsNullOrEmpty(r.JobAbbr) ? Styling.TextMuted : Styling.TextSecondary))
                ImGui.TextUnformatted(string.IsNullOrEmpty(r.JobAbbr) ? "—" : r.JobAbbr);

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
                ImGui.TextUnformatted(Formatting.Elapsed(r.Duration));

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentBlue))
                ImGui.TextUnformatted(r.FatesCompleted.ToString());

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, r.GemstonesEarned > 0 ? Styling.AccentAmber : Styling.TextMuted))
                ImGui.TextUnformatted(r.GemstonesEarned > 0 ? r.GemstonesEarned.ToString() : "—");

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, r.ExpEarned > 0 ? Styling.AccentMint : Styling.TextMuted))
                ImGui.TextUnformatted(r.ExpEarned > 0 ? Formatting.Exp(r.ExpEarned) : "—");

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, r.LevelsGained > 0 ? Styling.AccentPink : Styling.TextMuted))
                ImGui.TextUnformatted(r.LevelsGained > 0 ? $"+{r.LevelsGained}" : "—");
        }
    }

    private static void HeaderCell(FontAwesomeIcon icon, string label)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextUnformatted(icon.ToIconString());
        ImGui.SameLine(0, 5f * ImGuiHelpers.GlobalScale);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(label);
    }

    private static string RunTooltip(RunRecord r)
    {
        var when = r.EndedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var lines = $"{when}";
        if (!string.IsNullOrEmpty(r.ModeName)) lines += $"\nMode: {r.ModeName}";
        if (r.EndLevel > 0) lines += $"\nLevel: {r.StartLevel} → {r.EndLevel}";
        if (r.FatesPerHour > 0) lines += $"\nRate: {r.FatesPerHour:F1} FATEs/h  ·  {Formatting.Exp((long)r.ExpPerHour)} exp/h";
        if (r.ZoneNames.Count > 0) lines += $"\nZones: {string.Join(", ", r.ZoneNames)}";
        return lines;
    }

    private static string RelativeTime(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return utc.ToLocalTime().ToString("MMM d");
    }

    private void DrawClearControl(RunHistory history)
    {
        if (!confirmClear)
        {
            const string label = "Clear history##afg_hist_clear";
            var w = ImGui.CalcTextSize("Clear history").X + ImGui.GetStyle().FramePadding.X * 2f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - w);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                if (ImGui.SmallButton(label))
                    confirmClear = true;
            return;
        }

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
            ImGui.TextUnformatted("Delete all recorded runs?");
        ImGui.SameLine();
        if (ImGui.SmallButton("Yes, clear##afg_hist_clear_yes"))
        {
            history.Clear();
            confirmClear = false;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Cancel##afg_hist_clear_no"))
            confirmClear = false;
    }
}
