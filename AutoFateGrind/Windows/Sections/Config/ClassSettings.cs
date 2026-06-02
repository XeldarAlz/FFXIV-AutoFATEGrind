using AutoFateGrind.Core.Game;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class ClassSettings
{
    private static int classPickerSelection;

    public static void Draw(Configuration cfg)
    {
        SettingsRow.Draw("Switch class when run starts",
            "Equip the first eligible gearset below when you press Start. Disable to leave the run on whatever class you're currently on.",
            () => SettingsControls.DrawToggle(cfg, () => cfg.ApplyClassOnStart, v => cfg.ApplyClassOnStart = v, "##cls_apply"));

        if (!cfg.ApplyClassOnStart)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("Class switching is off. Enable the toggle above to configure the queue.");
            return;
        }

        SettingsRow.Draw("When all classes are done",
            "After every queued class has hit its level cap, either keep grinding on the last one or stop the run.",
            () =>
            {
                var keep = cfg.AfterClassQueueDone == AfterClassQueueDone.KeepGrindingOnLast;
                if (ImGui.RadioButton("Keep grinding on the last class", keep))
                { cfg.AfterClassQueueDone = AfterClassQueueDone.KeepGrindingOnLast; cfg.SaveDebounced(); }
                if (ImGui.RadioButton("Stop the run", !keep))
                { cfg.AfterClassQueueDone = AfterClassQueueDone.StopRun; cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Add a gearset",
            "Use the gear-set number shown in your in-game Gear Set list (1–100). Class is resolved automatically.",
            () => DrawAddClassRow(cfg));

        SettingsRow.Draw("Queue",
            "Order matters: top entry runs first, then advances when its level cap is hit.",
            () => DrawClassQueueList(cfg));
    }

    private static void DrawAddClassRow(Configuration cfg)
    {
        var gearsets = ClassSwitcher.EnumerateGearsets();
        if (gearsets.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No gearsets found. Save one in-game (Character → Gear Set List) first.");
            return;
        }

        var alreadyQueued = cfg.ClassQueue.Select(e => e.GearsetIndex).ToHashSet();
        var labels = gearsets.Select(g =>
        {
            var job = ClassSwitcher.JobNameForJobId(g.JobId);
            var name = string.IsNullOrWhiteSpace(g.Name) ? "" : $" — {g.Name}";
            var taken = alreadyQueued.Contains(g.UserIndex) ? "  (queued)" : "";
            return $"{g.UserIndex,3}. {job}{name}{taken}";
        }).ToArray();

        classPickerSelection = Math.Clamp(classPickerSelection, 0, gearsets.Count - 1);

        ImGui.SetNextItemWidth(360);
        ImGui.Combo("##cls_picker", ref classPickerSelection, labels, labels.Length);

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
                ImGui.TextUnformatted("Already in the queue.");
    }

    private static void DrawClassQueueList(Configuration cfg)
    {
        if (cfg.ClassQueue.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No classes queued. Automation will use whatever class you're on.");
            return;
        }

        int? moveUp = null, moveDown = null, remove = null;
        var btnSize = ImGui.GetFrameHeight();
        var spacingX = 4f * ImGuiHelpers.GlobalScale;
        var rowRightWidth = btnSize * 3 + spacingX * 2 + Layout.RowRightMargin * ImGuiHelpers.GlobalScale;

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
                ImGui.TextUnformatted($"{jobName} · gearset {entry.GearsetIndex}");

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
            if (ImGui.SliderInt($"##cls_cap_{index}", ref cap, 0, ClassSwitcher.GameMaxLevel, cap == 0 ? "no cap" : "Stop at %d Level"))
            { entry.StopAtLevel = cap; cfg.SaveDebounced(); }

            var rightStart = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - rowRightWidth;
            ImGui.SameLine(rightStart);

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
