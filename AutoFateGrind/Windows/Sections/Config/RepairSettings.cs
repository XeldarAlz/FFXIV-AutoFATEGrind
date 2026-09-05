using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class RepairSettings
{
    private static readonly RepairMode[] repairModes =
        [RepairMode.SelfThenNpc, RepairMode.SelfOnly, RepairMode.NpcOnly];

    private static readonly SettingsControls.Choices.Choice[] repairModeChoices =
    [
        new(L.Settings.RepairSelfThenNpcName, L.Settings.RepairSelfThenNpcDetail),
        new(L.Settings.RepairSelfOnlyName, L.Settings.RepairSelfOnlyDetail),
        new(L.Settings.RepairNpcOnlyName, L.Settings.RepairNpcOnlyDetail),
    ];

    public static void Draw(Configuration cfg)
    {
        DrawTriggerGroup(cfg);
        if (!cfg.AutoRepair)
        {
            return;
        }

        DrawSourceGroup(cfg);
    }

    private static void DrawTriggerGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.RepairTrigger));

        SettingsRow.Draw(Loc.T(L.Settings.AutoRepair),
            Loc.T(L.Settings.AutoRepairHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoRepair, v => cfg.AutoRepair = v, "##rp_on"),
            SettingsRow.ToggleHeight);

        if (!cfg.AutoRepair)
        {
            SettingsRow.Note(Loc.T(L.Settings.AutoRepairOff));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.RepairThreshold),
            Loc.T(L.Settings.RepairThresholdHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##rp_threshold",
                () => cfg.AutoRepairThresholdPct, v => cfg.AutoRepairThresholdPct = Math.Clamp(v, 5, 80),
                5, 80, "%d%%"));
    }

    private static void DrawSourceGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.RepairSource));

        var selected = Math.Max(0, Array.IndexOf(repairModes, cfg.RepairMode));
        SettingsRow.Draw(Loc.T(L.Settings.RepairSource),
            Loc.T(L.Settings.RepairSourceHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##rp_mode", repairModeChoices, selected, choice =>
            {
                cfg.RepairMode = repairModes[choice];
                cfg.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(repairModeChoices[selected].Detail));

        if (cfg.RepairMode != RepairMode.SelfOnly)
        {
            SettingsRow.DrawBlock(Loc.T(L.Settings.CustomNpc),
                Loc.T(L.Settings.CustomNpcHelp),
                () => DrawCustomRepairNpc(cfg));

            SettingsRow.Note(Loc.T(L.Settings.NpcNote));
        }
    }

    private static void DrawCustomRepairNpc(Configuration cfg)
    {
        var npc = cfg.PreferredRepairNpc;
        if (npc is not null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
                ImGui.TextWrapped(Loc.T(L.Settings.NpcSet, npc.Name, npc.TerritoryId));
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped(Loc.T(L.Settings.NpcNone));
        }

        if (ImGui.Button($"{Loc.T(L.Settings.SetFromTarget)}##rp_set"))
        {
            var captured = RepairOps.CaptureCurrentTargetAsRepairNpc();
            if (captured is null)
                Svc.Chat.PrintError(Loc.T(L.Settings.NoTargetChat));
            else
            {
                cfg.PreferredRepairNpc = captured;
                cfg.SaveDebounced();
                Svc.Chat.Print(Loc.T(L.Settings.NpcSetChat, captured.Name, captured.TerritoryId));
            }
        }

        if (npc is not null)
        {
            ImGui.SameLine();
            if (ImGui.Button($"{Loc.T(L.Common.Clear)}##rp_clear"))
            {
                cfg.PreferredRepairNpc = null;
                cfg.SaveDebounced();
            }
        }
    }
}
