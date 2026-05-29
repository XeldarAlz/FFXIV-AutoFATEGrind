using AutoFateGrind.Core.Modes;
using Dalamud.Configuration;
using ECommons.Throttlers;

namespace AutoFateGrind;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool AutoShowOnLogin { get; set; } = false;

    public List<uint> SelectedZones { get; set; } = [];

    // Legacy enum kept for migration from pre-mode-registry saves; ModeId is the source of truth now.
    public GrindMode Mode { get; set; } = GrindMode.MaxGemstones;

    // Stable id into FateGrindModes. Empty means "not yet migrated" — resolved from Mode on first access.
    public string ModeId { get; set; } = "";

    [Newtonsoft.Json.JsonIgnore]
    public IFateGrindMode ActiveMode
    {
        get
        {
            if (string.IsNullOrEmpty(ModeId))
                ModeId = FateGrindModes.IdForLegacy(Mode);
            return FateGrindModes.GetById(ModeId) ?? FateGrindModes.Default;
        }
    }

    public int TargetFateCount { get; set; } = 30;
    public int TargetGemstoneCount { get; set; } = 1500;

    // Kept so old saved configs deserialize.
    public bool ShowAllZonesOverride { get; set; } = false;

    // Kept so old saved configs deserialize.
    public ExpansionFilter RegionFilter { get; set; } = ExpansionFilter.All;

    public string CombatPresetName { get; set; } = Core.AfgConstants.BundledCombatPresetName;

    public int MinTimeRemainingSec { get; set; } = 120;
    public int MaxProgressPct { get; set; } = 90;

    public bool SwapZonesWhenEmpty { get; set; } = true;
    public bool ShowLivePopout { get; set; } = false;

    // When the grind task ends by throwing (the MaxConsecutiveStateErrors backstop), auto-restart it a
    // bounded number of times instead of stopping the run.
    public bool AutoResumeOnFault { get; set; } = true;

    // Flat blacklist for FATEs with broken obstacle maps that pathfinding always fails on.
    public HashSet<uint> BlacklistedFateIds { get; set; } = [1831, 1832, 1914, 1915];

    // Per-FateType blacklist (key is (int)FateType for serialization stability). Augments, not replaces,
    // BlacklistedFateIds.
    public Dictionary<int, HashSet<uint>> BlacklistedTypeIds { get; set; } = [];

    // Stored as int so saved configs survive clib's FateRule enum reordering.
    public HashSet<int> SkippedFateRules { get; set; } = [];

    // Reorderable sort criteria. Empty list falls back to the default order baked into FateScanner.
    public List<FateSortEntry> FateSortOrder { get; set; } = [];

    // Runtime-only — never persists; FATEs whose obstacle map evaluated as bad mid-run, so the next
    // attempt won't re-generate and stall again. Cleared when the plugin reloads.
    [Newtonsoft.Json.JsonIgnore]
    public HashSet<uint> RuntimeBadObstacleMaps { get; set; } = [];

    public uint TargetTradeItemId { get; set; } = 0;
    public bool TradeOnCap { get; set; } = false;
    // Game-imposed Bicolor cap is 1500.
    public int TradeThreshold { get; set; } = 1500;
    public AfterTradeAction AfterTrade { get; set; } = AfterTradeAction.Resume;

    public GemstoneSpendMode SpendMode { get; set; } = GemstoneSpendMode.SpendAll;
    public int SpendGemsAmount { get; set; } = 1000;
    public int BuyQuantityAmount { get; set; } = 10;
    public int KeepGemstonesReserve { get; set; } = 0;

    public bool ApplyClassOnStart { get; set; } = false;
    public List<ClassQueueEntry> ClassQueue { get; set; } = [];
    public AfterClassQueueDone AfterClassQueueDone { get; set; } = AfterClassQueueDone.KeepGrindingOnLast;

    public bool AutoRepair { get; set; } = false;
    // Triggers when the lowest-condition equipped item drops to or below this percentage.
    public int AutoRepairThresholdPct { get; set; } = 20;
    public RepairMode RepairMode { get; set; } = RepairMode.SelfThenNpc;
    // When set, the NPC repair branch travels to this NPC instead of the Grand Company mender.
    public RepairNpc? PreferredRepairNpc { get; set; } = null;

    public bool GmAlertStopRun { get; set; } = true;
    public bool GmAlertToast { get; set; } = false;
    public bool GmAlertChat { get; set; } = false;
    public bool GmAlertSound { get; set; } = false;
    public int GmAlertBeepCount { get; set; } = 3;
    public int GmAlertBeepDurationMs { get; set; } = 250;
    public int GmAlertBeepFrequencyHz { get; set; } = 900;
    public bool GmAlertKillGame { get; set; } = false;
    public List<string> GmAlertCommands { get; set; } = [];

    public bool HumanizerEnabled { get; set; } = false;
    public int HumanizerFatesBeforeBreak { get; set; } = 20;
    public int HumanizerBreakMinMinutes { get; set; } = 5;
    public int HumanizerBreakMaxMinutes { get; set; } = 10;
    public int HumanizerPauseMinSec { get; set; } = 3;
    public int HumanizerPauseMaxSec { get; set; } = 8;
    public int HumanizerWanderMinMeters { get; set; } = 25;
    public int HumanizerWanderMaxMeters { get; set; } = 80;
    // TerritoryIds from Core.Zones.CityCatalog; defaults to every expansion's main hub.
    public HashSet<uint> HumanizerCities { get; set; } = [129, 132, 1185, 1205];

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

public enum AfterClassQueueDone
{
    KeepGrindingOnLast,
    StopRun,
}

public enum RepairMode
{
    SelfThenNpc,
    SelfOnly,
    NpcOnly,
}

// A user-chosen repair NPC captured from the current target. Coordinates are stored as scalars so the
// config serializes without a Vector3 converter.
public sealed class RepairNpc
{
    public uint TerritoryId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public uint DataId { get; set; }
    public string Name { get; set; } = "";
    // Fallback talk-menu index used only if the repair entry can't be matched by text (non-English clients).
    public int RepairIndex { get; set; } = 0;
}

public enum FateSortCriterion
{
    HasBonusWithTwist,
    Progress,
    HasBonus,
    TimeRemainingUrgent,
    Distance,
    TimeRemaining,
    Level,
    Name,
}

[Serializable]
public sealed class FateSortEntry
{
    public FateSortCriterion Criterion { get; set; }
    public bool Descending { get; set; }
}

[Serializable]
public sealed class ClassQueueEntry
{
    // 1-based, matches in-game Gear Set list.
    public byte GearsetIndex { get; set; }
    public byte JobId { get; set; }
    // 0 = no cap; otherwise advance when unsynced level >= cap.
    public int StopAtLevel { get; set; }
}
