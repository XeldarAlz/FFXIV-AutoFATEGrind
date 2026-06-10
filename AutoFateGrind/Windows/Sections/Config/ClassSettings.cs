using AutoFateGrind.Core.Game;
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
        new("Keep grinding on the last class", "When every queued class is capped, keep going on the last one."),
        new("Stop the run", "When every queued class is capped, end the run."),
    ];

    public static void Draw(Configuration cfg)
    {
        DrawSwitchingGroup(cfg);
        if (!cfg.ApplyClassOnStart)
        {
            return;
        }

        DrawDoneGroup(cfg);
        DrawQueueGroup(cfg);
    }

    private static void DrawSwitchingGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Class switching");

        SettingsRow.Draw("Switch class when run starts",
            "Equip the first eligible gearset below when you press Start. Disable to leave the run on whatever class you're currently on.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.ApplyClassOnStart, v => cfg.ApplyClassOnStart = v, "##cls_apply"),
            SettingsRow.ToggleHeight);

        if (!cfg.ApplyClassOnStart)
        {
            SettingsRow.Note("Class switching is off. Enable it to configure the queue.");
        }
    }

    private static void DrawDoneGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("When the queue is done");

        var selected = cfg.AfterClassQueueDone == AfterClassQueueDone.StopRun ? 1 : 0;
        SettingsRow.Draw("All classes capped",
            "What to do after every queued class has hit its level cap.",
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##cls_done", afterDoneChoices, selected, choice =>
            {
                cfg.AfterClassQueueDone = choice == 1 ? AfterClassQueueDone.StopRun : AfterClassQueueDone.KeepGrindingOnLast;
                cfg.SaveDebounced();
            }));

        SettingsRow.Caption(afterDoneChoices[selected].Detail);
    }

    private static void DrawQueueGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Queue");

        SettingsRow.DrawBlock("Add a gearset",
            "Use the gear-set number shown in your in-game Gear Set list (1-100). Class is resolved automatically.",
            () => DrawAddClassRow(cfg));

        SettingsRow.DrawBlock("Queue order",
            "Order matters: top entry runs first, then advances when its level cap is hit.",
            () => DrawClassQueueList(cfg));
    }

    private static void DrawAddClassRow(Configuration cfg)
    {
        var gearsets = ClassSwitcher.EnumerateGearsets();
        if (gearsets.Count == 0)
        {
            SettingsRow.Note("No gearsets found. Save one in-game (Character -> Gear Set List) first.");
            return;
        }

        var alreadyQueued = cfg.ClassQueue.Select(e => e.GearsetIndex).ToHashSet();
        var labels = gearsets.Select(g =>
        {
            var job = ClassSwitcher.JobNameForJobId(g.JobId);
            var name = string.IsNullOrWhiteSpace(g.Name) ? "" : $" - {g.Name}";
            var taken = alreadyQueued.Contains(g.UserIndex) ? "  (queued)" : "";
            return $"{g.UserIndex,3}. {job}{name}{taken}";
        }).ToArray();

        classPickerSelection = Math.Clamp(classPickerSelection, 0, gearsets.Count - 1);

        SettingsControls.DrawPlainCombo("##cls_picker", ref classPickerSelection, labels, 360f);

        var picked = gearsets[classPickerSelection];
        var duplicate = alreadyQueued.Contains(picked.UserIndex);

        ImGui.SameLine();
        using (ImRaii.Disabled(duplicate))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
            if (ImGui.SmallButton("Add##cls_add"))
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
                ImGui.TextUnformatted("Already in the queue.");
            }
    }

    private static void DrawClassQueueList(Configuration cfg)
    {
        if (cfg.ClassQueue.Count == 0)
        {
            SettingsRow.Note("No classes queued. Automation will use whatever class you're on.");
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
                ImGui.TextUnformatted($"{jobName} - gearset {entry.GearsetIndex}");

            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            {
                var jobId = ClassSwitcher.JobIdForUserIndex(entry.GearsetIndex);
                var lvl = ClassSwitcher.UnsyncedLevelForJobId(jobId);
                ImGui.TextUnformatted($"  (lvl {lvl})");
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            var cap = entry.StopAtLevel;
            using (SettingsControls.PushFrameColors())
                if (ImGui.SliderInt($"##cls_cap_{index}", ref cap, 0, ClassSwitcher.GameMaxLevel, cap == 0 ? "no cap" : "Stop at %d Level"))
                { entry.StopAtLevel = cap; cfg.SaveDebounced(); }

            ImGui.SameLine(SettingsGroup.InnerRightLocalX() - rowRightWidth);

            using (ImRaii.Disabled(index == 0))
                if (IconButton.Draw(FontAwesomeIcon.ArrowUp, $"##cls_up_{index}", btnSize)) onUp();
            ImGui.SameLine(0, spacingX);
            using (ImRaii.Disabled(index == total - 1))
                if (IconButton.Draw(FontAwesomeIcon.ArrowDown, $"##cls_dn_{index}", btnSize)) onDown();
            ImGui.SameLine(0, spacingX);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                if (IconButton.Draw(FontAwesomeIcon.Times, $"##cls_rm_{index}", btnSize)) onRemove();
        }
    }
}
