using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Ipc;

// IPC channel names verified against VBM source.
internal sealed class BossModIPC
{
    private static BossModIPC? instance;
    public static BossModIPC Instance => instance ??= new BossModIPC();

    private readonly ICallGateSubscriber<string, bool> setActive;
    private readonly ICallGateSubscriber<bool>         clearActive;
    private readonly ICallGateSubscriber<string, string, string, string, bool> addTransient;
    private readonly ICallGateSubscriber<string, string, bool> createPreset;

    private BossModIPC()
    {
        setActive    = Svc.PluginInterface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive");
        clearActive  = Svc.PluginInterface.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive");
        addTransient = Svc.PluginInterface.GetIpcSubscriber<string, string, string, string, bool>("BossMod.Presets.AddTransientStrategy");
        createPreset = Svc.PluginInterface.GetIpcSubscriber<string, string, bool>("BossMod.Presets.Create");
    }

    public bool IsAvailable => setActive.HasFunction;

    public bool SetActive(string presetName)
    {
        if (!setActive.HasFunction) return false;
        try { return setActive.InvokeFunc(presetName); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] SetActive failed"); return false; }
    }

    public bool ClearActive()
    {
        if (!clearActive.HasFunction) return false;
        try { return clearActive.InvokeFunc(); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] ClearActive failed"); return false; }
    }

    public bool AddTransientStrategy(string preset, string module, string track, string option)
    {
        if (!addTransient.HasFunction) return false;
        try { return addTransient.InvokeFunc(preset, module, track, option); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] AddTransientStrategy failed"); return false; }
    }

    public bool CreatePreset(string name, string serialized)
    {
        if (!createPreset.HasFunction) return false;
        try { return createPreset.InvokeFunc(name, serialized); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] CreatePreset failed"); return false; }
    }
}
