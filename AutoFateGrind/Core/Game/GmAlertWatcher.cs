using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Game;

// Fires alerts when a player with a GM OnlineStatus enters the object table.
// "fired" latches on the rising edge so each appearance triggers exactly once;
// it resets when no GM is in range, so a re-entry re-fires.
internal sealed class GmAlertWatcher : IDisposable
{
    // OnlineStatus row IDs 1..3 are the GM tiers (GameMaster, GameMaster Sentry, GameMaster Officer).
    private const uint GmStatusMin = 1;
    private const uint GmStatusMax = 3;

    private bool fired;

    public GmAlertWatcher()
    {
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework _)
    {
        var cfg = Plugin.Cfg;
        if (!AnyActionEnabled(cfg))
        {
            fired = false;
            return;
        }

        var local = Svc.Objects.LocalPlayer;
        if (local is null)
        {
            fired = false;
            return;
        }

        IPlayerCharacter? gm = null;
        foreach (var obj in Svc.Objects)
        {
            if (obj is not IPlayerCharacter pc) continue;
            if (pc.EntityId == local.EntityId) continue;
            var status = pc.OnlineStatus.RowId;
            if (status >= GmStatusMin && status <= GmStatusMax)
            {
                gm = pc;
                break;
            }
        }

        if (gm is null)
        {
            fired = false;
            return;
        }

        if (fired) return;
        fired = true;

        FireAlerts(cfg, gm);
    }

    private static bool AnyActionEnabled(Configuration cfg)
        => cfg.GmAlertStopRun
        || cfg.GmAlertToast
        || cfg.GmAlertChat
        || cfg.GmAlertSound
        || cfg.GmAlertKillGame
        || cfg.GmAlertCommands.Count > 0;

    private static void FireAlerts(Configuration cfg, IPlayerCharacter gm)
    {
        var name = gm.Name.TextValue;
        Svc.Log.Warning($"[AFG] GM detected nearby: {name} (OnlineStatus {gm.OnlineStatus.RowId}). Firing GM alert.");

        if (cfg.GmAlertStopRun)
        {
            try { Plugin.Instance.Controller.Stop(); }
            catch (Exception ex) { Svc.Log.Warning(ex, "[AFG] GM alert: Controller.Stop threw."); }
        }

        if (cfg.GmAlertToast)
        {
            try { Svc.Toasts.ShowNormal($"GM {name} is nearby!"); }
            catch (Exception ex) { Svc.Log.Warning(ex, "[AFG] GM alert: toast threw."); }
        }

        if (cfg.GmAlertChat)
        {
            try { Svc.Chat.PrintError($"[AFG] GM {name} is nearby!"); }
            catch (Exception ex) { Svc.Log.Warning(ex, "[AFG] GM alert: chat print threw."); }
        }

        if (cfg.GmAlertSound)
            PlayBeeps(cfg.GmAlertBeepCount, cfg.GmAlertBeepFrequencyHz, cfg.GmAlertBeepDurationMs);

        foreach (var cmd in cfg.GmAlertCommands)
        {
            try { Chat.ExecuteCommand(cmd); }
            catch (Exception ex) { Svc.Log.Warning(ex, $"[AFG] GM alert: command '{cmd}' threw."); }
        }

        if (cfg.GmAlertKillGame)
        {
            try { Chat.ExecuteCommand("/xlkill"); }
            catch (Exception ex) { Svc.Log.Warning(ex, "[AFG] GM alert: /xlkill threw."); }
        }
    }

    // Console.Beep blocks the calling thread for `duration` ms, so each beep runs on the threadpool.
    public static void PlayBeeps(int count, int freqHz, int durationMs)
    {
        var n = Math.Clamp(count, 1, 100);
        var f = Math.Clamp(freqHz, 37, 32767);
        var d = Math.Clamp(durationMs, 1, 5000);
        Task.Run(() =>
        {
            for (var i = 0; i < n; i++)
            {
                try { Console.Beep(f, d); }
                catch (Exception ex) { Svc.Log.Debug($"[AFG] Console.Beep failed: {ex.Message}"); break; }
            }
        });
    }
}
