using AutoFateGrind.Core.Game.Player;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class ClassSettings
{
    private const int GearsetRefreshMs = 2000;
    private const float CapSliderWidth = 140f;

    private static readonly SettingsControls.Choices.Choice[] afterDoneChoices =
    [
        new(L.Settings.DoneKeepName, L.Settings.DoneKeepDetail),
        new(L.Settings.DoneStopName, L.Settings.DoneStopDetail),
    ];

    private static readonly SearchablePicker gearsetPicker = new(true);

    private static List<ClassSwitcher.GearsetOption> gearsets = [];
    private static string[] gearsetLabels = [];
    private static long gearsetsBuiltAtMs;
    private static int gearsetsQueueCount = -1;
    private static int classPickerSelection;

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
        if (Gw.BeginSection(Loc.T(L.Settings.ClassesSwitching)))
        {
            var applyOnStart = cfg.ApplyClassOnStart;
            if (SettingsControls.ToggleRow(L.Settings.SwitchOnStart, L.Settings.SwitchOnStartHelp, ref applyOnStart))
            {
                cfg.ApplyClassOnStart = applyOnStart;
                cfg.SaveDebounced();
            }

            if (!cfg.ApplyClassOnStart)
            {
                SettingsControls.Note(Loc.T(L.Settings.SwitchingOff));
            }
        }

        Gw.EndSection();
    }

    private static void DrawDoneGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.ClassesDone)))
        {
            var selected = cfg.AfterClassQueueDone == AfterClassQueueDone.StopRun ? 1 : 0;
            if (SettingsControls.Choices.DrawRow(L.Settings.AllCapped, L.Settings.AllCappedHelp, afterDoneChoices, ref selected))
            {
                cfg.AfterClassQueueDone = selected == 1 ? AfterClassQueueDone.StopRun : AfterClassQueueDone.KeepGrindingOnLast;
                cfg.SaveDebounced();
            }
        }

        Gw.EndSection();
    }

    private static void DrawQueueGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.ClassesQueue)))
        {
            DrawAddClassRow(cfg);
            SettingsControls.TitleRow(L.Settings.QueueOrder, L.Settings.QueueOrderHelp);
            DrawClassQueueList(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawAddClassRow(Configuration cfg)
    {
        RefreshGearsets(cfg);
        if (gearsets.Count == 0)
        {
            SettingsControls.TitleRow(L.Settings.AddGearset, L.Settings.AddGearsetHelp);
            SettingsControls.Note(Loc.T(L.Settings.NoGearsets));
            return;
        }

        classPickerSelection = Math.Clamp(classPickerSelection, 0, gearsets.Count - 1);
        gearsetPicker.Draw(L.Settings.AddGearset, L.Settings.AddGearsetHelp, gearsetLabels, ref classPickerSelection);
        var picked = gearsets[classPickerSelection];
        var duplicate = IsQueued(cfg, picked.UserIndex);

        ImGui.SameLine();
        ImGui.BeginDisabled(duplicate || !gearsetPicker.HasMatches);
        if (Gw.Button(Loc.T(L.Common.Add), ButtonStyle.Prominent, ButtonSize.Small))
        {
            AddToQueue(cfg, picked);
        }

        ImGui.EndDisabled();
        if (duplicate)
        {
            SettingsControls.InlineNote(Loc.T(L.Settings.AlreadyQueued));
        }
    }

    private static void AddToQueue(Configuration cfg, ClassSwitcher.GearsetOption picked)
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
        gearsetsBuiltAtMs = 0;

        var nextFree = gearsets.FindIndex(option => !IsQueued(cfg, option.UserIndex));
        if (nextFree >= 0)
        {
            classPickerSelection = nextFree;
        }
    }

    // Gearsets come from the game and carry their queue state in the label, so the list is rebuilt every couple of
    // seconds and whenever the queue changes rather than every frame.
    private static void RefreshGearsets(Configuration cfg)
    {
        var now = Environment.TickCount64;
        if (gearsetsBuiltAtMs > 0 && gearsetsQueueCount == cfg.ClassQueue.Count && now - gearsetsBuiltAtMs < GearsetRefreshMs)
        {
            return;
        }

        gearsetsBuiltAtMs = now;
        gearsetsQueueCount = cfg.ClassQueue.Count;
        gearsets = ClassSwitcher.EnumerateGearsets();
        var queuedSuffix = string.Concat("  ", Loc.T(L.Settings.Queued));
        var labels = new string[gearsets.Count];
        for (var index = 0; index < gearsets.Count; index++)
        {
            var gearset = gearsets[index];
            GameTextGlyphs.Add(gearset.Name);
            var job = ClassSwitcher.JobNameForJobId(gearset.JobId);
            var name = string.IsNullOrWhiteSpace(gearset.Name) ? string.Empty : string.Concat(" - ", gearset.Name);
            var taken = IsQueued(cfg, gearset.UserIndex) ? queuedSuffix : string.Empty;
            labels[index] = $"{gearset.UserIndex,3}. {job}{name}{taken}";
        }

        gearsetLabels = labels;
    }

    private static bool IsQueued(Configuration cfg, byte userIndex)
    {
        for (var index = 0; index < cfg.ClassQueue.Count; index++)
        {
            if (cfg.ClassQueue[index].GearsetIndex == userIndex)
            {
                return true;
            }
        }

        return false;
    }

    private static void DrawClassQueueList(Configuration cfg)
    {
        if (cfg.ClassQueue.Count == 0)
        {
            SettingsControls.Note(Loc.T(L.Settings.NoClassesQueued));
            return;
        }

        int? moveUp = null, moveDown = null, remove = null;
        var count = cfg.ClassQueue.Count;
        ImGui.BeginDisabled(Plugin.Instance.Controller.Running);
        for (var index = 0; index < count; index++)
        {
            ImGui.PushID(index);
            switch (DrawClassQueueRow(cfg, cfg.ClassQueue[index], index, count))
            {
                case SettingsControls.ListAction.MoveUp: moveUp = index; break;
                case SettingsControls.ListAction.MoveDown: moveDown = index; break;
                case SettingsControls.ListAction.Remove: remove = index; break;
            }

            ImGui.PopID();
        }

        ImGui.EndDisabled();
        if (ListReorder.Apply(cfg.ClassQueue, count, moveUp, moveDown, remove))
        {
            cfg.SaveDebounced();
        }
    }

    private static SettingsControls.ListAction DrawClassQueueRow(Configuration cfg, ClassQueueEntry entry, int index, int count)
    {
        var jobName = ClassSwitcher.JobNameForUserIndex(entry.GearsetIndex);
        var level = ClassSwitcher.UnsyncedLevelForJobId(ClassSwitcher.JobIdForUserIndex(entry.GearsetIndex));
        Gw.BeginRow($"{index + 1}. {Loc.T(L.Settings.QueueEntry, jobName, entry.GearsetIndex)}", Loc.T(L.Settings.EntryLevel, level));

        var cap = entry.StopAtLevel;
        if (SettingsControls.Slider("##cap"u8, ref cap, 0, ClassSwitcher.GameMaxLevel, Loc.T(L.Settings.StopAtLevel), CapSliderWidth, Loc.T(L.Settings.NoCap)))
        {
            entry.StopAtLevel = cap;
            cfg.SaveDebounced();
        }

        ImGui.SameLine();
        var action = SettingsControls.ReorderButtons(index > 0, index < count - 1);
        Gw.EndRow();
        return action;
    }
}
