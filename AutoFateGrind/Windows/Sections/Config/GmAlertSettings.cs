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
        DrawAlertsGroup(cfg);
        DrawActionsGroup(cfg);
    }

    private static void DrawAlertsGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Alerts");

        SettingsRow.Draw("Stop the run",
            "Halt automation immediately when a GM appears in your zone. Strongly recommended; the rest of the alerts are useless if the bot keeps grinding.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.GmAlertStopRun, v => cfg.GmAlertStopRun = v, "##gm_stop"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw("Toast notification",
            "Pop a Dalamud toast: \"GM <name> is nearby!\"",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.GmAlertToast, v => cfg.GmAlertToast = v, "##gm_toast"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw("Chat alert",
            "Print a red chat warning into your local log.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.GmAlertChat, v => cfg.GmAlertChat = v, "##gm_chat"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw("Sound beeps",
            "Plays a series of system beeps through your speakers. Loud enough to grab your attention if you're tabbed away.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.GmAlertSound, v => cfg.GmAlertSound = v, "##gm_sound"),
            SettingsRow.ToggleHeight);

        if (cfg.GmAlertSound)
        {
            DrawBeepRows(cfg);
        }
    }

    private static void DrawBeepRows(Configuration cfg)
    {
        SettingsRow.Draw("Beep count",
            "How many beeps to play in the burst.",
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##gm_beep_count",
                () => cfg.GmAlertBeepCount, v => cfg.GmAlertBeepCount = Math.Clamp(v, 1, 20), 1, 20, "%d beeps"));

        SettingsRow.Draw("Beep length",
            "How long each beep lasts.",
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##gm_beep_dur",
                () => cfg.GmAlertBeepDurationMs, v => cfg.GmAlertBeepDurationMs = Math.Clamp(v, 50, 1000), 50, 1000, "%d ms each"));

        SettingsRow.Draw("Beep pitch",
            "Tone frequency of each beep.",
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##gm_beep_freq",
                () => cfg.GmAlertBeepFrequencyHz, v => cfg.GmAlertBeepFrequencyHz = Math.Clamp(v, 100, 5000), 100, 5000, "%d Hz"));

        SettingsRow.DrawBlock("Test", null, () =>
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
                if (ImGui.SmallButton("Preview##gm_beep_preview"))
                    Core.Game.GmAlertWatcher.PlayBeeps(cfg.GmAlertBeepCount, cfg.GmAlertBeepFrequencyHz, cfg.GmAlertBeepDurationMs);
        });
    }

    private static void DrawActionsGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Actions");

        SettingsRow.DrawBlock("Custom commands",
            "Chat commands to run when a GM is spotted. Useful for things like /logout, /sh stay calm, or a macro.",
            () => DrawGmCommandList(cfg));

        SettingsRow.Draw("Kill the game",
            "Hard-terminate the game process via /xlkill. The last-resort option; no goodbyes, no cutscene, no logout. You'll get a disconnect.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.GmAlertKillGame, v => cfg.GmAlertKillGame = v, "##gm_kill"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawGmCommandList(Configuration cfg)
    {
        var input = gmCommandDraft;
        bool entered;
        using (SettingsControls.PushFrameColors())
        {
            ImGui.SetNextItemWidth(360f * ImGuiHelpers.GlobalScale);
            entered = ImGui.InputTextWithHint("##gm_cmd_in", "/logout", ref input, 200, ImGuiInputTextFlags.EnterReturnsTrue);
        }

        if (entered)
        {
            AddCommand(cfg, input);
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
                AddCommand(cfg, gmCommandDraft);
                gmCommandDraft = string.Empty;
            }

        if (cfg.GmAlertCommands.Count == 0)
        {
            SettingsRow.Note("No commands queued.");
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

            ImGui.SameLine(SettingsGroup.InnerRightLocalX() - btnSize);
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

    private static void AddCommand(Configuration cfg, string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return;

        var cmd = trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
        if (!cfg.GmAlertCommands.Contains(cmd))
        {
            cfg.GmAlertCommands.Add(cmd);
            cfg.SaveDebounced();
        }
    }
}
