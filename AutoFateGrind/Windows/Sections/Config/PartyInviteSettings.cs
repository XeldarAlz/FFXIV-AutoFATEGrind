using AutoFateGrind.Core.Localization;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class PartyInviteSettings
{
    private const int DeclineDelayMaxSec = 30;
    private const int ReplyMaxLength = 480;

    private static readonly SettingsControls.Choices.Choice[] replyChannelChoices =
    [
        new(L.Settings.ChannelTellName, L.Settings.ChannelTellDetail),
        new(L.Settings.ChannelSayName, L.Settings.ChannelSayDetail),
        new(L.Settings.ChannelYellName, L.Settings.ChannelYellDetail),
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
        if (Gw.BeginSection(Loc.T(L.Settings.InvitesDecline)))
        {
            DrawDeclineRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawDeclineRows(Configuration cfg)
    {
        var decline = cfg.DeclinePartyInvites;
        if (SettingsControls.ToggleRow(L.Settings.AutoDecline, L.Settings.AutoDeclineHelp, ref decline))
        {
            cfg.DeclinePartyInvites = decline;
            cfg.SaveDebounced();
        }

        if (!cfg.DeclinePartyInvites)
        {
            SettingsControls.Note(Loc.T(L.Settings.AutoDeclineOff));
            return;
        }

        var delayMin = cfg.DeclineInviteDelayMinSec;
        var delayMax = cfg.DeclineInviteDelayMaxSec;
        if (SettingsControls.RangeRow(L.Settings.DeclineDelay, L.Settings.DeclineDelayHelp, ref delayMin, ref delayMax, 0, DeclineDelayMaxSec, Loc.T(L.Settings.SecondsFormat)))
        {
            cfg.DeclineInviteDelayMinSec = delayMin;
            cfg.DeclineInviteDelayMaxSec = delayMax;
            cfg.SaveDebounced();
        }
    }

    private static void DrawReplyGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.InvitesReply)))
        {
            DrawReplyRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawReplyRows(Configuration cfg)
    {
        var sendReply = cfg.DeclineInviteReply;
        if (SettingsControls.ToggleRow(L.Settings.SendReply, L.Settings.SendReplyHelp, ref sendReply))
        {
            cfg.DeclineInviteReply = sendReply;
            cfg.SaveDebounced();
        }

        if (!cfg.DeclineInviteReply)
        {
            return;
        }

        var channel = Math.Clamp((int)cfg.DeclineInviteReplyChannel, 0, replyChannelChoices.Length - 1);
        if (SettingsControls.Choices.DrawRow(L.Settings.ReplyChannel, L.Settings.ReplyChannelHelp, replyChannelChoices, ref channel))
        {
            cfg.DeclineInviteReplyChannel = (PartyInviteReplyChannel)channel;
            cfg.SaveDebounced();
        }

        var message = cfg.DeclineInviteReplyMessage;
        if (SettingsControls.TextFieldRow(L.Settings.ReplyMessage, L.Settings.ReplyMessageHelp, ref message, ReplyMaxLength, Loc.T(L.Settings.ReplyMessageHint)))
        {
            cfg.DeclineInviteReplyMessage = message;
            cfg.SaveDebounced();
        }
    }
}
