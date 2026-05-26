using Dalamud.Configuration;
using ECommons.Throttlers;

namespace AutoFateGrind;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool AutoShowOnLogin { get; set; } = false;

    public List<uint> SelectedZones { get; set; } = [];

    public GrindMode Mode { get; set; } = GrindMode.MaxGemstones;
    public int TargetFateCount { get; set; } = 30;

    public bool ShowAllZonesOverride { get; set; } = false;
    public bool ShowCompletedZones { get; set; } = false;

    // Legacy. Kept so old saved configs deserialize; no longer used in the UI.
    public ExpansionFilter RegionFilter { get; set; } = ExpansionFilter.All;

    public string CombatPresetName { get; set; } = Core.AfgConstants.BundledCombatPresetName;

    public int MinTimeRemainingSec { get; set; } = 120;
    public int MaxProgressPct { get; set; } = 90;

    public bool SwapZonesWhenEmpty { get; set; } = true;
    public bool ShowLivePopout { get; set; } = false;

    // Hardcoded blacklist: FATEs with broken obstacle maps that pathfinding fails on.
    public HashSet<uint> BlacklistedFateIds { get; set; } = [1831, 1832, 1914, 1915];

    // Stored as the underlying int of clib.Utils.PublicEvent.FateRule so the saved config
    // survives a clib enum reordering. The scanner casts back when checking.
    public HashSet<int> SkippedFateRules { get; set; } = [];

    public uint TargetTradeItemId { get; set; } = 0;
    public bool TradeOnCap { get; set; } = true;
    // Game-imposed Bicolor cap is 1500; user can set a lower trigger to trade earlier.
    public int TradeThreshold { get; set; } = 1500;
    public AfterTradeAction AfterTrade { get; set; } = AfterTradeAction.Resume;

    public GemstoneSpendMode SpendMode { get; set; } = GemstoneSpendMode.SpendAll;
    public int SpendGemsAmount { get; set; } = 1000;
    public int BuyQuantityAmount { get; set; } = 10;
    // Gems to leave in the wallet untouched on every trade. Useful when saving up for a
    // pricier item without disabling auto-trade entirely.
    public int KeepGemstonesReserve { get; set; } = 0;

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

public enum AfterTradeAction
{
    Resume,
    Stop,
}

public enum GemstoneSpendMode
{
    SpendAll,
    SpendGems,
    BuyQuantity,
}
