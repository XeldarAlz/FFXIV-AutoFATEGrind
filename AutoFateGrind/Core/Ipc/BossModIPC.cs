using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Ipc;

internal sealed class BossModIPC
{
    private static BossModIPC? instance;
    public static BossModIPC Instance => instance ??= new BossModIPC();

    // Reflected so a change to BossMod's quality struct can't break us.
    private const string QualityIsBadProperty = "IsBad";

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
    private readonly ICallGateSubscriber<object?>                    obstacleEvaluateQuality;

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
        obstacleEvaluateQuality = Svc.PluginInterface.GetIpcSubscriber<object?>("BossMod.ObstacleMap.EvaluateTempMapQuality");
    }

    public bool IsAvailable => setActive.HasFunction;

    public bool SetActive(string presetName)
        => IpcGate.Invoke(setActive.HasFunction, () => setActive.InvokeFunc(presetName), false, "[BossModIPC] SetActive failed");

    public bool ClearActive()
        => IpcGate.Invoke(clearActive.HasFunction, clearActive.InvokeFunc, false, "[BossModIPC] ClearActive failed");

    public string? GetActive()
        => IpcGate.Invoke<string?>(getActive.HasFunction, getActive.InvokeFunc, null, "[BossModIPC] GetActive failed");

    public string? GetPreset(string name)
        => IpcGate.Invoke<string?>(getPreset.HasFunction, () => getPreset.InvokeFunc(name), null, "[BossModIPC] GetPreset failed");

    public bool AddTransientStrategy(string preset, string module, string track, string option)
        => IpcGate.Invoke(addTransient.HasFunction, () => addTransient.InvokeFunc(preset, module, track, option), false, "[BossModIPC] AddTransientStrategy failed");

    public bool CreatePreset(string serialized, bool overwrite)
        => IpcGate.Invoke(createPreset.HasFunction, () => createPreset.InvokeFunc(serialized, overwrite), false, "[BossModIPC] CreatePreset failed");

    public bool GenerateObstacleMap(Vector3 center, float radius, bool writeToFile = false)
        => IpcGate.Invoke(obstacleGenerate.HasFunction, () => obstacleGenerate.InvokeFunc(center, radius, writeToFile), false, "[BossModIPC] GenerateObstacleMap failed");

    public TaskStatus? GetObstacleMapStatus()
        => IpcGate.Invoke<TaskStatus?>(obstacleGetStatus.HasFunction, () => obstacleGetStatus.InvokeFunc(), null, "[BossModIPC] GetObstacleMapStatus failed");

    public bool HasTempObstacleMap()
        => IpcGate.Invoke(obstacleHasTempMap.HasFunction, obstacleHasTempMap.InvokeFunc, false, "[BossModIPC] HasTempObstacleMap failed");

    public bool ClearTempObstacleMap()
        => IpcGate.Invoke(obstacleClearTempMap.HasFunction, obstacleClearTempMap.InvokeFunc, false, "[BossModIPC] ClearTempObstacleMap failed");

    // Reads IsBad by reflection; missing IPC or property is treated as "not bad" to preserve behavior.
    public bool EvaluateTempMapQualityIsBad()
        => IpcGate.Invoke(obstacleEvaluateQuality.HasFunction, () =>
        {
            var result = obstacleEvaluateQuality.InvokeFunc();
            if (result is null) return false;
            var prop = result.GetType().GetProperty(QualityIsBadProperty);
            if (prop is null) return false;
            return prop.GetValue(result) is bool b && b;
        }, false, "[BossModIPC] EvaluateTempMapQuality failed");
}
