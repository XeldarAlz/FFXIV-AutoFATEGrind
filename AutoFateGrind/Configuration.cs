using Dalamud.Configuration;
using ECommons.Throttlers;

namespace AutoFateGrind;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool AutoShowOnLogin { get; set; } = false;

    public List<uint> SelectedZones { get; set; } = [];

    public GrindMode Mode { get; set; } = GrindMode.Endless;
    public int TargetFateCount { get; set; } = 30;

    // Legacy. Kept so old saved configs deserialize; no longer used in the UI.
    public ExpansionFilter RegionFilter { get; set; } = ExpansionFilter.All;

    public string CombatPresetName { get; set; } = "AFG - Default";

    public int MinTimeRemainingSec { get; set; } = 120;
    public int MaxProgressPct { get; set; } = 90;

    public bool SwapZonesWhenEmpty { get; set; } = true;
    public bool ShowLivePopout { get; set; } = false;

    public HashSet<uint> BlacklistedFateIds { get; set; } = [1831, 1832, 1914, 1915];

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    public void SaveDebounced()
    {
        if (EzThrottler.Throttle(Core.AfgConstants.ThrottleKeys.Save, Core.AfgConstants.SaveThrottleMs))
            Save();
    }
}

public enum GrindMode
{
    Endless,
    MaxFates,
    MaxGemstones,
    RunCount,
}

public enum ExpansionFilter
{
    All,
    ARR,
    HW,
    SB,
    ShB,
    EW,
    DT,
}
