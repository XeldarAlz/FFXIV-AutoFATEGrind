using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class PartyInviteSettings
{
    private static readonly string[] replyChannelLabels = ["Tell inviter", "Say", "Yell"];

    public static void Draw(Configuration cfg)
    {
        SettingsRow.Draw("Auto-decline party invites",
            "While a grind run is active, automatically decline incoming party invites after a short random delay. Invites that arrive while idle or playing manually are left alone for you to handle.",
            () => SettingsControls.DrawToggle(cfg, () => cfg.DeclinePartyInvites, v => cfg.DeclinePartyInvites = v, "##pi_on"));

        if (!cfg.DeclinePartyInvites)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("Auto-decline is off. Enable the toggle above to configure it.");
            return;
        }

        SettingsRow.Draw("Decline delay",
            "Wait a random time in this range before declining, so it looks like you noticed the popup and dismissed it yourself.",
            () =>
            {
                var lo = cfg.DeclineInviteDelayMinSec;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##pi_delay_min", ref lo, 0, 30, "Min %d sec"))
                {
                    cfg.DeclineInviteDelayMinSec = Math.Clamp(lo, 0, 30);
                    if (cfg.DeclineInviteDelayMaxSec < cfg.DeclineInviteDelayMinSec)
                        cfg.DeclineInviteDelayMaxSec = cfg.DeclineInviteDelayMinSec;
                    cfg.SaveDebounced();
                }

                var hi = cfg.DeclineInviteDelayMaxSec;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##pi_delay_max", ref hi, 0, 30, "Max %d sec"))
                {
                    cfg.DeclineInviteDelayMaxSec = Math.Clamp(hi, cfg.DeclineInviteDelayMinSec, 30);
                    cfg.SaveDebounced();
                }
            });

        SettingsRow.Draw("Send a reply",
            "After declining, send a chat message so it reads like a polite human brush-off rather than an instant silent decline.",
            () => SettingsControls.DrawToggle(cfg, () => cfg.DeclineInviteReply, v => cfg.DeclineInviteReply = v, "##pi_reply_on"));

        if (!cfg.DeclineInviteReply) return;

        SettingsRow.Draw("Reply channel",
            "Where the message goes. \"Tell inviter\" whispers the person who invited you. Ignored when your message starts with a slash command.",
            () =>
            {
                var ch = (int)cfg.DeclineInviteReplyChannel;
                ImGui.SetNextItemWidth(220);
                if (ImGui.Combo("##pi_channel", ref ch, replyChannelLabels, replyChannelLabels.Length))
                { cfg.DeclineInviteReplyChannel = (PartyInviteReplyChannel)ch; cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Reply message",
            "Use {name} for the inviter's character name and {world} for their home world. If the message begins with \"/\", it's sent verbatim as a command (e.g. /tell {name}@{world} busy right now!).",
            () =>
            {
                var msg = cfg.DeclineInviteReplyMessage;
                ImGui.SetNextItemWidth(360);
                if (ImGui.InputTextWithHint("##pi_msg", "Sorry {name}, I'm busy right now!", ref msg, 480))
                { cfg.DeclineInviteReplyMessage = msg; cfg.SaveDebounced(); }
            });
    }
}
