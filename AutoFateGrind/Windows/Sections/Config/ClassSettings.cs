using AutoFateGrind.Core.Game.Player;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class ClassSettings
{
    private static int classPickerSelection;

    private static readonly SettingsControls.Choices.Choice[] afterDoneChoices =
    [
        new(L.Settings.DoneKeepName, L.Settings.DoneKeepDetail),
        new(L.Settings.DoneStopName, L.Settings.DoneStopDetail),
    ];

    public static void Draw(Configuration cfg)
    {
        DrawSwitchingGroup(cfg);
        using var more = Motion.PushSection("##cls_more", cfg.ApplyClassOnStart);
        if (more is null)
        {
            return;
        }

        DrawDoneGroup(cfg);
        DrawQueueGroup(cfg);
    }

    private static void DrawSwitchingGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.ClassesSwitching));

        SettingsRow.Draw(Loc.T(L.Settings.SwitchOnStart),
            Loc.T(L.Settings.SwitchOnStartHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.ApplyClassOnStart, v => cfg.ApplyClassOnStart = v, "##cls_apply"),
            SettingsRow.ToggleHeight);

        using var note = Motion.PushSection("##cls_off_note", !cfg.ApplyClassOnStart);
        if (note is null)
        {
            return;
        }

        SettingsRow.Note(Loc.T(L.Settings.SwitchingOff));
    }

    private static void DrawDoneGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.ClassesDone));

        var selected = cfg.AfterClassQueueDone == AfterClassQueueDone.StopRun ? 1 : 0;
        SettingsRow.Draw(Loc.T(L.Settings.AllCapped),
            Loc.T(L.Settings.AllCappedHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##cls_done", afterDoneChoices, selected, choice =>
            {
                cfg.AfterClassQueueDone = choice == 1 ? AfterClassQueueDone.StopRun : AfterClassQueueDone.KeepGrindingOnLast;
                cfg.SaveDebounced();
            }));

        SettingsRow.Caption(Loc.T(afterDoneChoices[selected].Detail));
    }

    private static void DrawQueueGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.ClassesQueue));

        SettingsRow.DrawBlock(Loc.T(L.Settings.AddGearset),
            Loc.T(L.Settings.AddGearsetHelp),
            () => DrawAddClassRow(cfg));

        SettingsRow.DrawBlock(Loc.T(L.Settings.QueueOrder),
            Loc.T(L.Settings.QueueOrderHelp),
            () => DrawClassQueueList(cfg));
    }

    private static void DrawAddClassRow(Configuration cfg)
    {
        var gearsets = ClassSwitcher.EnumerateGearsets();
        if (gearsets.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Settings.NoGearsets));
            return;
        }

        var alreadyQueued = cfg.ClassQueue.Select(e => e.GearsetIndex).ToHashSet();
        var queuedSuffix = "  " + Loc.T(L.Settings.Queued);
        var labels = gearsets.Select(g =>
        {
            var job = ClassSwitcher.JobNameForJobId(g.JobId);
            var name = string.IsNullOrWhiteSpace(g.Name) ? "" : $" - {g.Name}";
            var taken = alreadyQueued.Contains(g.UserIndex) ? queuedSuffix : "";
            return $"{g.UserIndex,3}. {job}{name}{taken}";
        }).ToArray();

        classPickerSelection = Math.Clamp(classPickerSelection, 0, gearsets.Count - 1);

        SettingsControls.DrawSearchableCombo("##cls_picker", labels, ref classPickerSelection, 360f);

        var picked = gearsets[classPickerSelection];
        var duplicate = alreadyQueued.Contains(picked.UserIndex);

        ImGui.SameLine();
        using (ImRaii.Disabled(duplicate))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
            if (ImGui.SmallButton($"{Loc.T(L.Common.Add)}##cls_add"))
            {
                var maxLevel = ClassSwitcher.GameMaxLevel;
                var atCap = ClassSwitcher.UnsyncedLevelForJobId(picked.JobId) >= maxLevel;
                cfg.ClassQueue.Add(new ClassQueueEntry
                {
                    GearsetIndex = picked.UserIndex,
                    JobId = picked.JobId,
                    StopAtLevel = atCap ? 0 : maxLevel,
                });
                cfg.SaveDebounced();
                var nextFree = gearsets.FindIndex(g => !alreadyQueued.Contains(g.UserIndex) && g.UserIndex != picked.UserIndex);
                if (nextFree >= 0) classPickerSelection = nextFree;
            }

        if (duplicate)
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            {
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Loc.T(L.Settings.AlreadyQueued));
            }
    }

    private static void DrawClassQueueList(Configuration cfg)
    {
        if (cfg.ClassQueue.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Settings.NoClassesQueued));
            return;
        }

        int? moveUp = null, moveDown = null, remove = null;
        var btnSize = ImGui.GetFrameHeight();
        var spacingX = 4f * ImGuiHelpers.GlobalScale;
        var rowRightWidth = btnSize * 3 + spacingX * 2;

        for (var i = 0; i < cfg.ClassQueue.Count; i++)
        {
            var entry = cfg.ClassQueue[i];
            DrawClassQueueRow(i, cfg.ClassQueue.Count, entry, cfg, btnSize, spacingX, rowRightWidth,
                onUp: () => moveUp = i,
                onDown: () => moveDown = i,
                onRemove: () => remove = i);
        }

        if (ListReorder.Apply(cfg.ClassQueue, cfg.ClassQueue.Count, moveUp, moveDown, remove))
            cfg.SaveDebounced();
    }

    private static void DrawClassQueueRow(
        int index, int total, ClassQueueEntry entry, Configuration cfg,
        float btnSize, float spacingX, float rowRightWidth,
        Action onUp, Action onDown, Action onRemove)
    {
        var running = Plugin.Instance.Controller.Running;
        using (ImRaii.Disabled(running))
        {
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted($"{index + 1}.");
            ImGui.SameLine();
            var jobName = ClassSwitcher.JobNameForUserIndex(entry.GearsetIndex);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                ImGui.TextUnformatted(Loc.T(L.Settings.QueueEntry, jobName, entry.GearsetIndex));

            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            {
                var jobId = ClassSwitcher.JobIdForUserIndex(entry.GearsetIndex);
                var lvl = ClassSwitcher.UnsyncedLevelForJobId(jobId);
                ImGui.TextUnformatted("  " + Loc.T(L.Settings.EntryLevel, lvl));
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            var cap = entry.StopAtLevel;
            using (SettingsControls.PushFrameColors())
                if (ImGui.SliderInt($"##cls_cap_{index}", ref cap, 0, ClassSwitcher.GameMaxLevel, cap == 0 ? Loc.T(L.Settings.NoCap) : Loc.T(L.Settings.StopAtLevel)))
                { entry.StopAtLevel = cap; cfg.SaveDebounced(); }

            ImGui.SameLine(SettingsGroup.InnerRightLocalX() - rowRightWidth);

            if (IconButton.Draw(FontAwesomeIcon.ArrowUp, $"##cls_up_{index}", btnSize, tooltip: Loc.T(L.Common.MoveUp), enabled: index > 0 && !running)) onUp();
            ImGui.SameLine(0, spacingX);
            if (IconButton.Draw(FontAwesomeIcon.ArrowDown, $"##cls_dn_{index}", btnSize, tooltip: Loc.T(L.Common.MoveDown), enabled: index < total - 1 && !running)) onDown();
            ImGui.SameLine(0, spacingX);
            if (IconButton.Draw(FontAwesomeIcon.Times, $"##cls_rm_{index}", btnSize, Styling.AccentRose, Loc.T(L.Common.Remove), enabled: !running)) onRemove();
        }
    }
}
