using AutoFateGrind.Core.Game.Ops;
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
        new("Self, then NPC", "Use Dark Matter from your bag first; fall back to the Grand Company mender when you run out."),
        new("Self only", "Repair with Dark Matter from your bag. No travel."),
        new("NPC only", "Travel to your Grand Company mender (or a custom NPC) and pay in seals."),
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
        using var group = SettingsGroup.Begin("Repair trigger");

        SettingsRow.Draw("Auto-repair gear",
            "Between FATEs, when the lowest equipped item drops to or below the threshold, the plugin runs a repair. At 0% the gear stops working, so keep some margin.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoRepair, v => cfg.AutoRepair = v, "##rp_on"),
            SettingsRow.ToggleHeight);

        if (!cfg.AutoRepair)
        {
            SettingsRow.Note("Auto-repair is off. Enable it to configure repair.");
            return;
        }

        SettingsRow.Draw("Repair threshold",
            "Trips when the worst equipped slot reaches this condition percentage. 20% leaves comfortable margin before the 0% breakdown.",
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##rp_threshold",
                () => cfg.AutoRepairThresholdPct, v => cfg.AutoRepairThresholdPct = Math.Clamp(v, 5, 80),
                5, 80, "%d%%"));
    }

    private static void DrawSourceGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Repair source");

        var selected = Math.Max(0, Array.IndexOf(repairModes, cfg.RepairMode));
        SettingsRow.Draw("Repair source",
            "How the repair is performed. Self-repair uses Dark Matter from your bag (no travel). NPC repair travels to your Grand Company mender.",
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##rp_mode", repairModeChoices, selected, choice =>
            {
                cfg.RepairMode = repairModes[choice];
                cfg.SaveDebounced();
            }));
        SettingsRow.Caption(repairModeChoices[selected].Detail);

        if (cfg.RepairMode != RepairMode.SelfOnly)
        {
            SettingsRow.DrawBlock("Custom repair NPC",
                "Optional. Travel to any repair NPC instead of the Grand Company mender. Target the NPC in-game, then click \"Set from target\". Clear to fall back to the GC mender.",
                () => DrawCustomRepairNpc(cfg));

            SettingsRow.Note("NPC repair uses your custom repair NPC if set, otherwise your Grand Company mender (teleports there and pays in company seals). A custom NPC removes the Grand Company requirement.");
        }
    }

    private static void DrawCustomRepairNpc(Configuration cfg)
    {
        var npc = cfg.PreferredRepairNpc;
        if (npc is not null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
                ImGui.TextWrapped($"{npc.Name}  (territory {npc.TerritoryId})");
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("None - using Grand Company mender.");
        }

        if (ImGui.Button("Set from target"))
        {
            var captured = RepairOps.CaptureCurrentTargetAsRepairNpc();
            if (captured is null)
                Svc.Chat.PrintError("[AFG] No target - target a repair NPC first, then click again.");
            else
            {
                cfg.PreferredRepairNpc = captured;
                cfg.SaveDebounced();
                Svc.Chat.Print($"[AFG] Custom repair NPC set: {captured.Name} (territory {captured.TerritoryId}).");
            }
        }

        if (npc is not null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                cfg.PreferredRepairNpc = null;
                cfg.SaveDebounced();
            }
        }
    }
}
