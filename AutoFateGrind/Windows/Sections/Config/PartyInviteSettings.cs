using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class PartyInviteSettings
{
    private static readonly SettingsControls.Choices.Choice[] replyChannelChoices =
    [
        new(L.Settings.ChannelTellName, L.Settings.ChannelTellDetail),
        new(L.Settings.ChannelSayName, L.Settings.ChannelSayDetail),
        new(L.Settings.ChannelYellName, L.Settings.ChannelYellDetail),
    ];

    public static void Draw(Configuration cfg)
    {
        DrawDeclineGroup(cfg);
        using var reply = Motion.PushSection("##pi_reply", cfg.DeclinePartyInvites);
        if (reply is null)
        {
            return;
        }

        DrawReplyGroup(cfg);
    }

    private static void DrawDeclineGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.InvitesDecline));

        SettingsRow.Draw(Loc.T(L.Settings.AutoDecline),
            Loc.T(L.Settings.AutoDeclineHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.DeclinePartyInvites, v => cfg.DeclinePartyInvites = v, "##pi_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##pi_body", cfg.DeclinePartyInvites);
        if (!cfg.DeclinePartyInvites)
        {
            SettingsRow.Note(Loc.T(L.Settings.AutoDeclineOff));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.DeclineDelay),
            Loc.T(L.Settings.DeclineDelayHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##pi_delay_min", "##pi_delay_max",
                () => cfg.DeclineInviteDelayMinSec, v => cfg.DeclineInviteDelayMinSec = v,
                () => cfg.DeclineInviteDelayMaxSec, v => cfg.DeclineInviteDelayMaxSec = v, 30, 0, Loc.T(L.Settings.SecondsFormat)));
    }

    private static void DrawReplyGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.InvitesReply));

        SettingsRow.Draw(Loc.T(L.Settings.SendReply),
            Loc.T(L.Settings.SendReplyHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.DeclineInviteReply, v => cfg.DeclineInviteReply = v, "##pi_reply_on"),
            SettingsRow.ToggleHeight);

        using var channel = Motion.PushSection("##pi_channel", cfg.DeclineInviteReply);
        if (channel is null)
        {
            return;
        }

        var selected = Math.Clamp((int)cfg.DeclineInviteReplyChannel, 0, replyChannelChoices.Length - 1);
        SettingsRow.Draw(Loc.T(L.Settings.ReplyChannel),
            Loc.T(L.Settings.ReplyChannelHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##pi_channel", replyChannelChoices, selected, choice =>
            {
                cfg.DeclineInviteReplyChannel = (PartyInviteReplyChannel)choice;
                cfg.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(replyChannelChoices[selected].Detail));

        SettingsRow.DrawBlock(Loc.T(L.Settings.ReplyMessage),
            Loc.T(L.Settings.ReplyMessageHelp),
            () =>
            {
                var msg = cfg.DeclineInviteReplyMessage;
                ImGui.SetNextItemWidth(360f * ImGuiHelpers.GlobalScale);
                if (ImGui.InputTextWithHint("##pi_msg", Loc.T(L.Settings.ReplyMessageHint), ref msg, 480))
                { cfg.DeclineInviteReplyMessage = msg; cfg.SaveDebounced(); }
            });
    }
}
