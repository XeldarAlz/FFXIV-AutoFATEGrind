using AutoFateGrind.Core.Game.Watchers;
using AutoFateGrind.Core.Localization;
using Dalamud.Bindings.ImGui;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class GmAlertSettings
{
    private const int BeepCountMin = 1;
    private const int BeepCountMax = 20;
    private const int BeepDurationMinMs = 50;
    private const int BeepDurationMaxMs = 1000;
    private const int BeepFrequencyMinHz = 100;
    private const int BeepFrequencyMaxHz = 5000;
    private const int CommandMaxLength = 200;
    private const float CommandFieldWidth = 360f;
    private const string CommandHint = "/logout";

    private static string gmCommandDraft = string.Empty;

    public static void Draw(Configuration cfg)
    {
        DrawAlertsGroup(cfg);
        DrawActionsGroup(cfg);
    }

    private static void DrawAlertsGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.GmAlerts)))
        {
            DrawAlertRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawAlertRows(Configuration cfg)
    {
        var stopRun = cfg.GmAlertStopRun;
        if (SettingsControls.ToggleRow(L.Settings.GmStopRun, L.Settings.GmStopRunHelp, ref stopRun))
        {
            cfg.GmAlertStopRun = stopRun;
            cfg.SaveDebounced();
        }

        var toast = cfg.GmAlertToast;
        if (SettingsControls.ToggleRow(L.Settings.GmToast, L.Settings.GmToastHelp, ref toast))
        {
            cfg.GmAlertToast = toast;
            cfg.SaveDebounced();
        }

        var chat = cfg.GmAlertChat;
        if (SettingsControls.ToggleRow(L.Settings.GmChat, L.Settings.GmChatHelp, ref chat))
        {
            cfg.GmAlertChat = chat;
            cfg.SaveDebounced();
        }

        var sound = cfg.GmAlertSound;
        if (SettingsControls.ToggleRow(L.Settings.GmSound, L.Settings.GmSoundHelp, ref sound))
        {
            cfg.GmAlertSound = sound;
            cfg.SaveDebounced();
        }

        if (cfg.GmAlertSound)
        {
            DrawBeepRows(cfg);
        }
    }

    private static void DrawBeepRows(Configuration cfg)
    {
        var count = cfg.GmAlertBeepCount;
        if (SettingsControls.SliderRow(L.Settings.BeepCount, L.Settings.BeepCountHelp, ref count, BeepCountMin, BeepCountMax, Loc.T(L.Settings.BeepCountFormat)))
        {
            cfg.GmAlertBeepCount = count;
            cfg.SaveDebounced();
        }

        var duration = cfg.GmAlertBeepDurationMs;
        if (SettingsControls.SliderRow(L.Settings.BeepLength, L.Settings.BeepLengthHelp, ref duration, BeepDurationMinMs, BeepDurationMaxMs, Loc.T(L.Settings.BeepLengthFormat)))
        {
            cfg.GmAlertBeepDurationMs = duration;
            cfg.SaveDebounced();
        }

        var frequency = cfg.GmAlertBeepFrequencyHz;
        if (SettingsControls.SliderRow(L.Settings.BeepPitch, L.Settings.BeepPitchHelp, ref frequency, BeepFrequencyMinHz, BeepFrequencyMaxHz, Loc.T(L.Settings.BeepPitchFormat)))
        {
            cfg.GmAlertBeepFrequencyHz = frequency;
            cfg.SaveDebounced();
        }

        Gw.BeginRow(Loc.T(L.Common.Test));
        if (Gw.Button(Loc.T(L.Common.Preview), ButtonStyle.Default, ButtonSize.Small))
        {
            GmAlertWatcher.PlayBeeps(cfg.GmAlertBeepCount, cfg.GmAlertBeepFrequencyHz, cfg.GmAlertBeepDurationMs);
        }

        Gw.EndRow();
    }

    private static void DrawActionsGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.GmActions)))
        {
            SettingsControls.TitleRow(L.Settings.GmCommands, L.Settings.GmCommandsHelp);
            DrawGmCommandList(cfg);

            var killGame = cfg.GmAlertKillGame;
            if (SettingsControls.ToggleRow(L.Settings.GmKill, L.Settings.GmKillHelp, ref killGame))
            {
                cfg.GmAlertKillGame = killGame;
                cfg.SaveDebounced();
            }
        }

        Gw.EndSection();
    }

    private static void DrawGmCommandList(Configuration cfg)
    {
        var draft = gmCommandDraft;
        SettingsControls.TextField(ref draft, CommandMaxLength, CommandHint, CommandFieldWidth);
        var entered = ImGui.IsItemDeactivated() && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter));
        gmCommandDraft = draft;

        ImGui.SameLine();
        if (Gw.Button(Loc.T(L.Common.Add), ButtonStyle.Prominent, ButtonSize.Small) || entered)
        {
            AddCommand(cfg, gmCommandDraft);
            gmCommandDraft = string.Empty;
        }

        if (cfg.GmAlertCommands.Count == 0)
        {
            SettingsControls.Note(Loc.T(L.Settings.NoCommands));
            return;
        }

        var remove = -1;
        for (var index = 0; index < cfg.GmAlertCommands.Count; index++)
        {
            ImGui.PushID(index);
            Gw.BeginRow($"{index + 1}. {cfg.GmAlertCommands[index]}");
            if (SettingsControls.RemoveButton())
            {
                remove = index;
            }

            Gw.EndRow();
            ImGui.PopID();
        }

        if (remove < 0)
        {
            return;
        }

        cfg.GmAlertCommands.RemoveAt(remove);
        cfg.SaveDebounced();
    }

    private static void AddCommand(Configuration cfg, string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var command = trimmed.StartsWith('/') ? trimmed : string.Concat("/", trimmed);
        if (cfg.GmAlertCommands.Contains(command))
        {
            return;
        }

        cfg.GmAlertCommands.Add(command);
        cfg.SaveDebounced();
    }
}
