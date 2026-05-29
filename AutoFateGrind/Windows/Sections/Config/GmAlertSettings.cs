using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class GmAlertSettings
{
    private static string gmCommandDraft = string.Empty;

    public static void Draw(Configuration cfg)
    {
        SettingsRow.Draw("Stop the run",
            "Halt automation immediately when a GM appears in your zone. Strongly recommended — the rest of the alerts are useless if the bot keeps grinding.",
            () => SettingsControls.DrawToggle(cfg, () => cfg.GmAlertStopRun, v => cfg.GmAlertStopRun = v, "##gm_stop"));

        SettingsRow.Draw("Toast notification",
            "Pop a Dalamud toast: \"GM <name> is nearby!\"",
            () => SettingsControls.DrawToggle(cfg, () => cfg.GmAlertToast, v => cfg.GmAlertToast = v, "##gm_toast"));

        SettingsRow.Draw("Chat alert",
            "Print a red chat warning into your local log.",
            () => SettingsControls.DrawToggle(cfg, () => cfg.GmAlertChat, v => cfg.GmAlertChat = v, "##gm_chat"));

        SettingsRow.Draw("Sound beeps",
            "Plays a series of system beeps through your speakers. Loud enough to grab your attention if you're tabbed away.",
            () =>
            {
                SettingsControls.DrawToggle(cfg, () => cfg.GmAlertSound, v => cfg.GmAlertSound = v, "##gm_sound");

                if (!cfg.GmAlertSound) return;

                ImGui.Indent(20f);

                var count = cfg.GmAlertBeepCount;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##gm_beep_count", ref count, 1, 20, "%d beeps"))
                { cfg.GmAlertBeepCount = Math.Clamp(count, 1, 20); cfg.SaveDebounced(); }

                var dur = cfg.GmAlertBeepDurationMs;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##gm_beep_dur", ref dur, 50, 1000, "%d ms each"))
                { cfg.GmAlertBeepDurationMs = Math.Clamp(dur, 50, 1000); cfg.SaveDebounced(); }

                var freq = cfg.GmAlertBeepFrequencyHz;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##gm_beep_freq", ref freq, 100, 5000, "%d Hz"))
                { cfg.GmAlertBeepFrequencyHz = Math.Clamp(freq, 100, 5000); cfg.SaveDebounced(); }

                using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
                    if (ImGui.SmallButton("Preview##gm_beep_preview"))
                        Core.Game.GmAlertWatcher.PlayBeeps(cfg.GmAlertBeepCount, cfg.GmAlertBeepFrequencyHz, cfg.GmAlertBeepDurationMs);

                ImGui.Unindent(20f);
            });

        SettingsRow.Draw("Custom commands",
            "Chat commands to run when a GM is spotted. Useful for things like /logout, /sh stay calm, or a macro.",
            () => DrawGmCommandList(cfg));

        SettingsRow.Draw("Kill the game",
            "Hard-terminate the game process via /xlkill. The last-resort option — no goodbyes, no cutscene, no logout. You'll get a disconnect.",
            () => SettingsControls.DrawToggle(cfg, () => cfg.GmAlertKillGame, v => cfg.GmAlertKillGame = v, "##gm_kill"));
    }

    private static void DrawGmCommandList(Configuration cfg)
    {
        ImGui.SetNextItemWidth(360);
        var input = gmCommandDraft;
        if (ImGui.InputTextWithHint("##gm_cmd_in", "/logout", ref input, 200, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            var trimmed = input.Trim();
            if (trimmed.Length > 0)
            {
                var cmd = trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
                if (!cfg.GmAlertCommands.Contains(cmd))
                {
                    cfg.GmAlertCommands.Add(cmd);
                    cfg.SaveDebounced();
                }
            }
            gmCommandDraft = string.Empty;
        }
        else
        {
            gmCommandDraft = input;
        }

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
            if (ImGui.SmallButton("Add##gm_cmd_add"))
            {
                var trimmed = gmCommandDraft.Trim();
                if (trimmed.Length > 0)
                {
                    var cmd = trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
                    if (!cfg.GmAlertCommands.Contains(cmd))
                    {
                        cfg.GmAlertCommands.Add(cmd);
                        cfg.SaveDebounced();
                    }
                    gmCommandDraft = string.Empty;
                }
            }

        if (cfg.GmAlertCommands.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted("No commands queued.");
            return;
        }

        int? remove = null;
        var btnSize = ImGui.GetFrameHeight();
        for (var i = 0; i < cfg.GmAlertCommands.Count; i++)
        {
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted($"{i + 1}.");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                ImGui.TextUnformatted(cfg.GmAlertCommands[i]);

            var rightStart = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - btnSize - 8f * ImGuiHelpers.GlobalScale;
            ImGui.SameLine(rightStart);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
            using (ImRaii.PushFont(UiBuilder.IconFont))
                if (ImGui.Button(FontAwesomeIcon.Times.ToIconString() + $"##gm_cmd_rm_{i}", new Vector2(btnSize, btnSize)))
                    remove = i;
        }

        if (remove is int r)
        {
            cfg.GmAlertCommands.RemoveAt(r);
            cfg.SaveDebounced();
        }
    }
}
