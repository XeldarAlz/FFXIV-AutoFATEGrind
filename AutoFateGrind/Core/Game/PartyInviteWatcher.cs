using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Game;

// Auto-declines incoming player party invites while a grind run is active. An invite shows as a stock
// SelectYesno dialog, so we hook its PostSetup and gate on AgentPartyInvite being active — that agent is
// only live for a genuine incoming invite, which keeps us off Party-Finder self-joins and our own
// shop/repair yes/no prompts. The decline itself is deferred to a later frame to fake a human reaction
// time, and re-validated against the agent right before clicking in case the invite already cleared.
internal sealed unsafe class PartyInviteWatcher : IDisposable
{
    private const string SelectYesnoAddon = "SelectYesno";

    private static readonly Random rng = new();

    private bool pending;
    private long declineAtTick;
    private uint confirmAddonId;
    private string inviterName = "";
    private string inviterWorld = "";

    public PartyInviteWatcher()
    {
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, SelectYesnoAddon, OnSelectYesnoSetup);
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, SelectYesnoAddon, OnSelectYesnoSetup);
    }

    private void OnSelectYesnoSetup(AddonEvent type, AddonArgs args)
    {
        if (pending) return;

        var cfg = Plugin.Cfg;
        if (!cfg.DeclinePartyInvites) return;
        if (!Plugin.Instance.Controller.Running) return;

        var agent = AgentPartyInvite.Instance();
        if (agent is null) return;

        // An incoming invite spawns this SelectYesno as the agent's confirm dialog and records its addon id
        // in ConfirmAddonId. Matching that against the addon that just opened pins us to a genuine invite
        // without touching our own shop/repair yes/no prompts. We do NOT gate on IsAgentActive(): the agent
        // doesn't reliably report active for an incoming invite, which silently broke auto-decline.
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon is null) return;
        if (agent->ConfirmAddonId == 0 || agent->ConfirmAddonId != addon->Id)
        {
            Svc.Log.Debug($"[AFG] SelectYesno opened (id={addon->Id}) but not a party invite (ConfirmAddonId={agent->ConfirmAddonId}); ignoring.");
            return;
        }

        CaptureInviter();

        var lo = Math.Max(0, cfg.DeclineInviteDelayMinSec);
        var hi = Math.Max(lo, cfg.DeclineInviteDelayMaxSec);
        var delayMs = (lo == hi ? lo : rng.Next(lo, hi + 1)) * 1000;

        pending = true;
        confirmAddonId = addon->Id;
        declineAtTick = Environment.TickCount64 + delayMs;
        Svc.Log.Info($"[AFG] Party invite from {DisplayName()} detected; declining in ~{delayMs / 1000}s.");
    }

    private void OnUpdate(IFramework _)
    {
        if (!pending) return;

        var agent = AgentPartyInvite.Instance();
        if (agent is null || agent->ConfirmAddonId != confirmAddonId)
        {
            // Invite expired or was handled (manually/elsewhere) before our delay elapsed: the agent
            // no longer points at the confirm dialog we captured.
            pending = false;
            return;
        }

        if (Environment.TickCount64 < declineAtTick) return;

        pending = false;
        Decline();
    }

    private void Decline()
    {
        var who = DisplayName();

        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(SelectYesnoAddon, out var addon) || !GenericHelpers.IsAddonReady(addon))
        {
            Svc.Log.Debug("[AFG] Party invite: SelectYesno gone before decline; skipping.");
            return;
        }

        try
        {
            new AddonMaster.SelectYesno((nint)addon).No();
            Svc.Log.Info($"[AFG] Declined party invite from {who}.");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[AFG] Party invite: decline click threw.");
            return;
        }

        if (Plugin.Cfg.DeclineInviteReply)
            SendReply();
    }

    private void SendReply()
    {
        var body = (Plugin.Cfg.DeclineInviteReplyMessage ?? "").Trim();
        if (body.Length == 0) return;

        body = body.Replace("{name}", inviterName).Replace("{world}", inviterWorld);

        var line = body.StartsWith('/')
            ? body
            : Plugin.Cfg.DeclineInviteReplyChannel switch
            {
                PartyInviteReplyChannel.Tell => BuildTell(body),
                PartyInviteReplyChannel.Yell => $"/yell {body}",
                _ => $"/say {body}",
            };

        try { Chat.SendMessage(line); }
        catch (Exception ex) { Svc.Log.Warning(ex, $"[AFG] Party invite: reply send threw for '{line}'."); }
    }

    private string BuildTell(string body)
    {
        if (string.IsNullOrEmpty(inviterName)) return $"/say {body}";
        var target = string.IsNullOrEmpty(inviterWorld) ? inviterName : $"{inviterName}@{inviterWorld}";
        return $"/tell {target} {body}";
    }

    private void CaptureInviter()
    {
        inviterName = "";
        inviterWorld = "";
        try
        {
            var proxy = InfoProxyPartyInvite.Instance();
            if (proxy is null) return;
            inviterName = proxy->InviterName.ToString();
            var world = Svc.Data.GetExcelSheet<World>().GetRowOrDefault(proxy->InviterWorldId);
            inviterWorld = world?.Name.ExtractText() ?? "";
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[AFG] Party invite: failed to read inviter info: {ex.Message}");
        }
    }

    private string DisplayName()
        => string.IsNullOrEmpty(inviterName) ? "a player"
         : string.IsNullOrEmpty(inviterWorld) ? inviterName
         : $"{inviterName}@{inviterWorld}";
}
