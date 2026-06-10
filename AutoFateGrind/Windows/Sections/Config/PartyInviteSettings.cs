using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class PartyInviteSettings
{
    private static readonly SettingsControls.Choices.Choice[] replyChannelChoices =
    [
        new("Tell inviter", "Whisper the person who invited you."),
        new("Say", "Local /say, heard by players near you."),
        new("Yell", "Zone-wide /yell."),
    ];

    public static void Draw(Configuration cfg)
    {
        DrawDeclineGroup(cfg);
        if (!cfg.DeclinePartyInvites)
        {
            return;
        }

        DrawReplyGroup(cfg);
    }

    private static void DrawDeclineGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Decline");

        SettingsRow.Draw("Auto-decline party invites",
            "While a grind run is active, automatically decline incoming party invites after a short random delay. Invites that arrive while idle or playing manually are left alone for you to handle.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.DeclinePartyInvites, v => cfg.DeclinePartyInvites = v, "##pi_on"),
            SettingsRow.ToggleHeight);

        if (!cfg.DeclinePartyInvites)
        {
            SettingsRow.Note("Auto-decline is off. Enable it to configure it.");
            return;
        }

        SettingsRow.Draw("Decline delay",
            "Wait a random time in this range before declining, so it looks like you noticed the popup and dismissed it yourself.",
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##pi_delay_min", "##pi_delay_max",
                () => cfg.DeclineInviteDelayMinSec, v => cfg.DeclineInviteDelayMinSec = v,
                () => cfg.DeclineInviteDelayMaxSec, v => cfg.DeclineInviteDelayMaxSec = v, 30, 0, "%d s"));
    }

    private static void DrawReplyGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Reply");

        SettingsRow.Draw("Send a reply",
            "After declining, send a chat message so it reads like a polite human brush-off rather than an instant silent decline.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.DeclineInviteReply, v => cfg.DeclineInviteReply = v, "##pi_reply_on"),
            SettingsRow.ToggleHeight);

        if (!cfg.DeclineInviteReply)
        {
            return;
        }

        var selected = Math.Clamp((int)cfg.DeclineInviteReplyChannel, 0, replyChannelChoices.Length - 1);
        SettingsRow.Draw("Reply channel",
            "Where the message goes. \"Tell inviter\" whispers the person who invited you. Ignored when your message starts with a slash command.",
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##pi_channel", replyChannelChoices, selected, choice =>
            {
                cfg.DeclineInviteReplyChannel = (PartyInviteReplyChannel)choice;
                cfg.SaveDebounced();
            }));
        SettingsRow.Caption(replyChannelChoices[selected].Detail);

        SettingsRow.DrawBlock("Reply message",
            "Use {name} for the inviter's character name and {world} for their home world. If the message begins with \"/\", it's sent verbatim as a command (e.g. /tell {name}@{world} busy right now!).",
            () =>
            {
                var msg = cfg.DeclineInviteReplyMessage;
                ImGui.SetNextItemWidth(360f * ImGuiHelpers.GlobalScale);
                if (ImGui.InputTextWithHint("##pi_msg", "Sorry {name}, I'm busy right now!", ref msg, 480))
                { cfg.DeclineInviteReplyMessage = msg; cfg.SaveDebounced(); }
            });
    }
}
