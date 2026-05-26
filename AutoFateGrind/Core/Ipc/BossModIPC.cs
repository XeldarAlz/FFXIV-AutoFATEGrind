using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Ipc;

internal sealed class BossModIPC
{
    private static BossModIPC? instance;
    public static BossModIPC Instance => instance ??= new BossModIPC();

    private readonly ICallGateSubscriber<string, bool> setActive;
    private readonly ICallGateSubscriber<bool>         clearActive;
    private readonly ICallGateSubscriber<string>       getActive;
    private readonly ICallGateSubscriber<string, string?> getPreset;
    private readonly ICallGateSubscriber<string, string, string, string, bool> addTransient;
    private readonly ICallGateSubscriber<string, bool, bool> createPreset;
    private readonly ICallGateSubscriber<Vector3, float, bool, bool> obstacleGenerate;
    private readonly ICallGateSubscriber<TaskStatus>                 obstacleGetStatus;
    private readonly ICallGateSubscriber<bool>                       obstacleHasTempMap;
    private readonly ICallGateSubscriber<bool>                       obstacleClearTempMap;

    private BossModIPC()
    {
        setActive            = Svc.PluginInterface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive");
        clearActive          = Svc.PluginInterface.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive");
        getActive            = Svc.PluginInterface.GetIpcSubscriber<string>("BossMod.Presets.GetActive");
        getPreset            = Svc.PluginInterface.GetIpcSubscriber<string, string?>("BossMod.Presets.Get");
        addTransient         = Svc.PluginInterface.GetIpcSubscriber<string, string, string, string, bool>("BossMod.Presets.AddTransientStrategy");
        createPreset         = Svc.PluginInterface.GetIpcSubscriber<string, bool, bool>("BossMod.Presets.Create");
        obstacleGenerate     = Svc.PluginInterface.GetIpcSubscriber<Vector3, float, bool, bool>("BossMod.ObstacleMap.Generate");
        obstacleGetStatus    = Svc.PluginInterface.GetIpcSubscriber<TaskStatus>("BossMod.ObstacleMap.GetGenerationStatus");
        obstacleHasTempMap   = Svc.PluginInterface.GetIpcSubscriber<bool>("BossMod.ObstacleMap.HasTempMap");
        obstacleClearTempMap = Svc.PluginInterface.GetIpcSubscriber<bool>("BossMod.ObstacleMap.ClearTempMap");
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

    public string? GetActive()
    {
        if (!getActive.HasFunction) return null;
        try { return getActive.InvokeFunc(); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] GetActive failed"); return null; }
    }

    public string? GetPreset(string name)
    {
        if (!getPreset.HasFunction) return null;
        try { return getPreset.InvokeFunc(name); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] GetPreset failed"); return null; }
    }

    public bool AddTransientStrategy(string preset, string module, string track, string option)
    {
        if (!addTransient.HasFunction) return false;
        try { return addTransient.InvokeFunc(preset, module, track, option); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] AddTransientStrategy failed"); return false; }
    }

    public bool CreatePreset(string serialized, bool overwrite)
    {
        if (!createPreset.HasFunction) return false;
        try { return createPreset.InvokeFunc(serialized, overwrite); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] CreatePreset failed"); return false; }
    }

    public bool GenerateObstacleMap(Vector3 center, float radius, bool writeToFile = false)
    {
        if (!obstacleGenerate.HasFunction) return false;
        try { return obstacleGenerate.InvokeFunc(center, radius, writeToFile); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] GenerateObstacleMap failed"); return false; }
    }

    public TaskStatus? GetObstacleMapStatus()
    {
        if (!obstacleGetStatus.HasFunction) return null;
        try { return obstacleGetStatus.InvokeFunc(); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] GetObstacleMapStatus failed"); return null; }
    }

    public bool HasTempObstacleMap()
    {
        if (!obstacleHasTempMap.HasFunction) return false;
        try { return obstacleHasTempMap.InvokeFunc(); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] HasTempObstacleMap failed"); return false; }
    }

    public bool ClearTempObstacleMap()
    {
        if (!obstacleClearTempMap.HasFunction) return false;
        try { return obstacleClearTempMap.InvokeFunc(); }
        catch (Exception ex) { Svc.Log.Warning(ex, "[BossModIPC] ClearTempObstacleMap failed"); return false; }
    }
}
