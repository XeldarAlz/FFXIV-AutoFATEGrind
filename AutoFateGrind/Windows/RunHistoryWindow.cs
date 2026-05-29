using AutoFateGrind.Core.Stats;
using Dalamud.Bindings.ImGui;
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
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(660, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320),
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

        Styling.SectionLabel("Recent runs");
        if (history.Records.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("No runs recorded yet. Finish (or stop) a grind and it'll show up here.");
            return;
        }

        DrawRunTable(history);

        ImGui.Spacing();
        DrawClearControl(history);
    }

    private static void DrawLifetime(RunHistory.LifetimeTotals t)
    {
        Styling.SectionLabel("Lifetime");
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            ImGui.TextUnformatted(
                $"{t.Runs} run{(t.Runs == 1 ? "" : "s")}  ·  {t.Fates} FATEs  ·  {t.Gemstones} gems  ·  {Formatting.Exp(t.Exp)} exp  ·  {t.Levels} levels");
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted($"{Formatting.Elapsed(t.Duration)} grinding  ·  {t.FatesPerHour:F1} FATEs/h average");
    }

    private static void DrawRunTable(RunHistory history)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;

        using var table = ImRaii.Table("##afg_history", 7, flags, new Vector2(-1, -52 * ImGuiHelpers.GlobalScale));
        if (!table) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("FATEs", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Gems", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Exp", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Levels", ImGuiTableColumnFlags.WidthStretch, 0.7f);

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TableHeadersRow();

        foreach (var r in history.Records)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
                ImGui.TextUnformatted(r.EndedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            if (r.ZoneNames.Count > 0 && ImGui.IsItemHovered())
                ImGui.SetTooltip($"Mode: {r.ModeName}\nZones: {string.Join(", ", r.ZoneNames)}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(string.IsNullOrEmpty(r.JobAbbr) ? "—" : r.JobAbbr);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Formatting.Elapsed(r.Duration));

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentBlue))
                ImGui.TextUnformatted(r.FatesCompleted.ToString());

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
                ImGui.TextUnformatted(r.GemstonesEarned.ToString());

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
                ImGui.TextUnformatted(Formatting.Exp(r.ExpEarned));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(r.LevelsGained > 0 ? $"+{r.LevelsGained}" : "—");
        }
    }

    private void DrawClearControl(RunHistory history)
    {
        if (!confirmClear)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                if (ImGui.SmallButton("Clear history##afg_hist_clear"))
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
