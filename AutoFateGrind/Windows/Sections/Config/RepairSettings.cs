using AutoFateGrind.Core.Game;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class RepairSettings
{
    public static void Draw(Configuration cfg)
    {
        SettingsRow.Draw("Auto-repair when gear is damaged",
            "Between FATEs, when the lowest equipped item drops to or below the threshold, the plugin runs a repair. At 0% the gear stops working — keep some margin.",
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoRepair, v => cfg.AutoRepair = v, "##rp_on"));

        if (!cfg.AutoRepair)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("Auto-repair is off. Enable the toggle above to configure repair.");
            return;
        }

        SettingsRow.Draw("Repair threshold",
            "Trips when the worst equipped slot reaches this condition percentage. 20% leaves comfortable margin before the 0% breakdown.",
            () =>
            {
                var v = cfg.AutoRepairThresholdPct;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##rp_threshold", ref v, 5, 80, "%d%%"))
                { cfg.AutoRepairThresholdPct = Math.Clamp(v, 5, 80); cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Repair source",
            "How the repair is performed. Self-repair uses Dark Matter from your bag (no travel). NPC repair travels to your Grand Company mender.",
            () =>
            {
                if (ImGui.RadioButton("Self first, then NPC if no Dark Matter", cfg.RepairMode == RepairMode.SelfThenNpc))
                { cfg.RepairMode = RepairMode.SelfThenNpc; cfg.SaveDebounced(); }

                if (ImGui.RadioButton("Self only (Dark Matter)", cfg.RepairMode == RepairMode.SelfOnly))
                { cfg.RepairMode = RepairMode.SelfOnly; cfg.SaveDebounced(); }

                if (ImGui.RadioButton("NPC only (Grand Company mender)", cfg.RepairMode == RepairMode.NpcOnly))
                { cfg.RepairMode = RepairMode.NpcOnly; cfg.SaveDebounced(); }
            });

        if (cfg.RepairMode != RepairMode.SelfOnly)
            DrawCustomRepairNpcRow(cfg);

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextWrapped("NPC repair uses your custom repair NPC if set, otherwise your Grand Company mender (teleports there and pays in company seals). A custom NPC removes the Grand Company requirement.");
    }

    private static void DrawCustomRepairNpcRow(Configuration cfg)
    {
        SettingsRow.Draw("Custom repair NPC",
            "Optional. Travel to any repair NPC instead of the Grand Company mender. Target the NPC in-game, then click \"Set from target\". Clear to fall back to the GC mender.",
            () =>
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
                        ImGui.TextWrapped("None — using Grand Company mender.");
                }

                if (ImGui.Button("Set from target"))
                {
                    var captured = RepairOps.CaptureCurrentTargetAsRepairNpc();
                    if (captured is null)
                        Svc.Chat.PrintError("[AFG] No target — target a repair NPC first, then click again.");
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
            });
    }
}
