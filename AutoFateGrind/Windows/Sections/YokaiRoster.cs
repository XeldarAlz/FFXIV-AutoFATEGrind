using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class YokaiRoster
{
    private const float Gap = 8f;

    private static readonly (int Medals, int Target, string? Text)[] progressLabels =
        new (int Medals, int Target, string? Text)[YokaiCatalog.Entries.Length];

    public static void Draw(Configuration cfg, AutoFateController ctrl, bool scrollIntoView)
    {
        DrawHeader(cfg, scrollIntoView);
        Styling.VSpace(10f);
        DrawGrid(cfg, ctrl);
    }

    public static void DrawPlanNote(Configuration cfg)
    {
        var targetIndex = YokaiProgress.ResolveTargetIndex(cfg, 0);
        var note = targetIndex < 0
            ? Loc.T(L.Grind.DetailNoYokai)
            : Loc.T(L.Grind.YokaiPlanNext, YokaiProgress.MinionName(targetIndex), YokaiProgress.ZoneNames(targetIndex));

        using (Fonts.PushCaption())
        {
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            TextDraw.Wrapped(note, new Vector2(origin.X + 2f * ImGuiHelpers.GlobalScale, origin.Y), width, Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, TextDraw.MeasureWrapped(note, width).Y));
        }
    }

    private static void DrawHeader(Configuration cfg, bool scrollIntoView)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.LibraryHeaderHeight * scale;
        var midY = origin.Y + height * 0.5f;

        var label = Loc.T(L.Grind.Yokai);
        var labelSize = TextDraw.SectionTitleSize(label);
        TextDraw.SectionTitle(label, new Vector2(origin.X, midY - labelSize.Y * 0.5f), Styling.TextStrong);

        var (collected, needed) = YokaiProgress.Totals(cfg);
        using (Fonts.PushCaption())
        {
            var summary = Loc.T(L.Grind.YokaiSummary, collected, needed);
            var summarySize = TextDraw.Measure(summary);
            TextDraw.At(summary, new Vector2(origin.X + width - summarySize.X, midY - summarySize.Y * 0.5f), Styling.TextDim);
        }

        ImGui.Dummy(new Vector2(width, height));
        if (scrollIntoView)
        {
            ImGui.SetScrollHereY(0f);
        }
    }

    private static void DrawGrid(Configuration cfg, AutoFateController ctrl)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = Gap * scale;
        var avail = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)MathF.Floor((avail + gap) / (Layout.ZoneCardMinWidth * scale + gap)));
        var cardWidth = (avail - gap * (columns - 1)) / columns;
        var targetIndex = YokaiProgress.ResolveTargetIndex(cfg, 0);
        var count = YokaiCatalog.Entries.Length;

        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));
        for (var index = 0; index < count; index++)
        {
            if (index % columns != 0)
            {
                ImGui.SameLine(0f, gap);
            }

            DrawCard(cfg, ctrl, index, cardWidth, index == targetIndex);
        }
    }

    private static void DrawCard(Configuration cfg, AutoFateController ctrl, int entryIndex, float width, bool isTarget)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(width, Layout.ZoneCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var status = YokaiProgress.Statuses[entryIndex];
        var target = YokaiProgress.MedalTarget(cfg);
        var available = status.MinionUnlocked && !status.WeaponOwned;
        var enabled = available && YokaiProgress.IsEnabled(cfg, entryIndex);
        var interactive = available && !ctrl.Running;

        ImGui.PushID((nint)(entryIndex + 1));
        var hit = Hit.Area("##yokai", size, interactive);
        var hover = Motion.Hover(Motion.Key("##yokai"), hit.Hovered);
        var active = Motion.Approach(Motion.Key("##yokai", 1), enabled ? 1f : 0f, 14f);
        ImGui.PopID();

        if (hit.Clicked)
        {
            Toggle(cfg, YokaiCatalog.Entries[entryIndex].MinionId);
        }

        var dl = ImGui.GetWindowDrawList();
        Paint.Glass(dl, origin, end, Styling.CardRounding * scale, Styling.AccentViolet, 0.02f + 0.16f * active, hover);

        var midY = origin.Y + size.Y * 0.5f;
        var discRadius = 9f * scale;
        var discCenter = new Vector2(origin.X + 14f * scale + discRadius, midY);
        DrawSelector(dl, discCenter, discRadius, status, active);

        var rightX = end.X - 12f * scale;
        rightX -= DrawProgress(entryIndex, status, target, rightX, midY) + 8f * scale;
        if (isTarget)
        {
            rightX -= DrawNextBadge(dl, rightX, midY) + 8f * scale;
        }

        var textX = discCenter.X + discRadius + 12f * scale;
        var nameColor = available ? Vector4.Lerp(Styling.TextSecondary, Styling.TextStrong, MathF.Max(active, hover)) : Styling.TextMuted;
        var name = TextDraw.Truncate(YokaiProgress.MinionName(entryIndex), rightX - textX);
        var nameSize = TextDraw.Measure(name);
        TextDraw.At(name, new Vector2(textX, midY - nameSize.Y * 0.5f), nameColor);

        if (Hit.HoveringRect(origin, end))
        {
            Tooltip.Show(CardTooltip(ctrl, entryIndex, status));
        }
    }

    private static void DrawSelector(ImDrawListPtr dl, Vector2 center, float radius, YokaiStatus status, float active)
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (!status.MinionUnlocked)
        {
            TextDraw.IconCentered(FontAwesomeIcon.Lock, center, Styling.TextMuted);
            return;
        }

        if (status.WeaponOwned)
        {
            TextDraw.IconCentered(FontAwesomeIcon.Check, center, Styling.AccentMint);
            return;
        }

        var ring = Vector4.Lerp(Styling.WithAlpha(Styling.BorderDim, 0.9f), Styling.AccentVioletSoft, active);
        dl.AddCircle(center, radius, Paint.Col(ring), 0, 1.4f * scale);
        if (active <= 0.01f)
        {
            return;
        }

        dl.AddCircleFilled(center, radius * active, Paint.Col(Styling.AccentViolet));
        if (active > 0.5f)
        {
            Paint.Check(dl, center, radius * 1.1f, Styling.WithAlpha(Styling.TextStrong, (active - 0.5f) * 2f), 1.8f * scale);
        }
    }

    private static string ProgressLabel(int entryIndex, int medals, int target)
    {
        var cached = progressLabels[entryIndex];
        if (cached.Text is not null && cached.Medals == medals && cached.Target == target)
        {
            return cached.Text;
        }

        var text = $"{medals} / {target}";
        progressLabels[entryIndex] = (medals, target, text);
        return text;
    }

    private static float DrawProgress(int entryIndex, YokaiStatus status, int target, float rightX, float midY)
    {
        if (!status.MinionUnlocked)
        {
            return 0f;
        }

        using (Fonts.PushCaption())
        {
            var label = status.WeaponOwned ? Loc.T(L.Grind.YokaiWeaponTag) : ProgressLabel(entryIndex, status.Medals, target);
            var color = status.WeaponOwned ? Styling.TextMuted
                : status.Medals >= target ? Styling.AccentMint
                : !status.Reachable ? Styling.AccentAmber
                : Styling.TextDim;
            var labelSize = TextDraw.Measure(label);
            TextDraw.At(label, new Vector2(rightX - labelSize.X, midY - labelSize.Y * 0.5f), color);
            return labelSize.X;
        }
    }

    private static float DrawNextBadge(ImDrawListPtr dl, float rightX, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        using (Fonts.PushCaption())
        {
            var label = Loc.T(L.Grind.YokaiNext);
            var badgeSize = TextDraw.Measure(label) + new Vector2(12f * scale, 4f * scale);
            var badgeMin = new Vector2(rightX - badgeSize.X, midY - badgeSize.Y * 0.5f);
            var badgeMax = badgeMin + badgeSize;
            Paint.Pill(dl, badgeMin, badgeMax, Styling.WithAlpha(Styling.AccentViolet, 0.35f), Styling.WithAlpha(Styling.AccentVioletSoft, 0.5f));
            TextDraw.Middle(label, badgeMin, badgeMax, Styling.TextStrong);
            return badgeSize.X;
        }
    }

    private static string CardTooltip(AutoFateController ctrl, int entryIndex, YokaiStatus status)
    {
        if (!status.MinionUnlocked)
        {
            return Loc.T(L.Grind.YokaiMinionMissing);
        }

        if (status.WeaponOwned)
        {
            return Loc.T(L.Grind.YokaiWeaponOwned);
        }

        if (ctrl.Running)
        {
            return Loc.T(L.Grind.YokaiLockedRunning);
        }

        return status.Reachable
            ? Loc.T(L.Grind.YokaiDropsIn, YokaiProgress.ZoneNames(entryIndex))
            : Loc.T(L.Grind.YokaiUnreachable, YokaiProgress.ZoneNames(entryIndex));
    }

    private static void Toggle(Configuration cfg, uint minionId)
    {
        if (!cfg.YokaiSkippedMinionIds.Remove(minionId))
        {
            cfg.YokaiSkippedMinionIds.Add(minionId);
        }

        cfg.SaveDebounced();
    }
}
