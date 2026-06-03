using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Ipc;

// Structural copy of TextAdvance's config type — its documented contract is "copy
// ExternalTerritoryConfig to your plugin". Null fields keep the user's setting; true/false force it.
public class ExternalTerritoryConfig
{
    public bool? EnableTalkSkip = null;
    public bool? EnableRequestFill = null;
    public bool? EnableRequestHandin = null;
}

internal static class TextAdvanceIPC
{
    private static ICallGateSubscriber<bool>? isEnabled;
    private static ICallGateSubscriber<string, ExternalTerritoryConfig, bool>? enableExternalControl;
    private static ICallGateSubscriber<string, bool>? disableExternalControl;
    private static bool initialized;

    private static void EnsureInit()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            isEnabled              = Svc.PluginInterface.GetIpcSubscriber<bool>("TextAdvance.IsEnabled");
            enableExternalControl  = Svc.PluginInterface.GetIpcSubscriber<string, ExternalTerritoryConfig, bool>("TextAdvance.EnableExternalControl");
            disableExternalControl = Svc.PluginInterface.GetIpcSubscriber<string, bool>("TextAdvance.DisableExternalControl");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[TextAdvanceIPC] subscribe failed");
        }
    }

    // TextAdvance's own "Enable plugin" toggle. AFG drives talk-skip via EnableExternalControl,
    // so this is advisory — returns true when the gate is absent/errors to avoid false warnings.
    public static bool IsPluginEnabled()
    {
        EnsureInit();
        try { return isEnabled?.HasFunction != true || isEnabled.InvokeFunc(); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[TextAdvanceIPC] IsEnabled failed"); return true; }
    }

    public static bool EnableExternalControl(string callerName, bool talkSkip, bool requestFill, bool requestHandin)
    {
        EnsureInit();
        if (enableExternalControl?.HasFunction != true) return false;
        try
        {
            var cfg = new ExternalTerritoryConfig
            {
                EnableTalkSkip = talkSkip,
                EnableRequestFill = requestFill,
                EnableRequestHandin = requestHandin,
            };
            return enableExternalControl.InvokeFunc(callerName, cfg);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[TextAdvanceIPC] EnableExternalControl failed");
            return false;
        }
    }

    public static void DisableExternalControl(string callerName)
    {
        EnsureInit();
        try { disableExternalControl?.InvokeFunc(callerName); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[TextAdvanceIPC] DisableExternalControl failed"); }
    }
}
