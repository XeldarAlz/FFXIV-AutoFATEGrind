using AutoFateGrind.Core.Modes;
using Dalamud.Configuration;
using ECommons.Throttlers;

namespace AutoFateGrind;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; }

    public bool AutoShowOnLogin { get; set; } = false;

    public List<uint> SelectedZones { get; set; } = [];

    // Legacy enum for migration; ModeId is the source of truth.
    public GrindMode Mode { get; set; } = GrindMode.MaxGemstones;
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
    public int TargetMinutes { get; set; } = 60;

    public string CombatPresetName { get; set; } = Core.AfgConstants.BundledCombatPresetName;

    public int MinTimeRemainingSec { get; set; } = 120;
    public int MaxProgressPct { get; set; } = 90;

    public bool SwapZonesWhenEmpty { get; set; } = true;
    public bool ShowLivePopout { get; set; } = false;

    // Auto-restart on fault, bounded by MaxConsecutiveStateErrors.
    public bool AutoResumeOnFault { get; set; } = true;

    public HashSet<uint> BlacklistedFateIds { get; set; } = [1831, 1832, 1914, 1915];

    // Per-FateType blacklist (augments BlacklistedFateIds); key is (int)FateType for stability.
    public Dictionary<int, HashSet<uint>> BlacklistedTypeIds { get; set; } = [];

    public HashSet<int> SkippedFateRules { get; set; } = [];
    public List<FateSortEntry> FateSortOrder { get; set; } = [];

    // Runtime-only; FATEs with bad obstacle maps mid-run (cleared on reload).
    [Newtonsoft.Json.JsonIgnore]
    public HashSet<uint> RuntimeBadObstacleMaps { get; set; } = [];

    public uint TargetTradeItemId { get; set; } = 0;
    public bool TradeOnCap { get; set; } = false;
    // Game-imposed Bicolor cap is 1500.
    public int TradeThreshold { get; set; } = 1500;
    public AfterTradeAction AfterTrade { get; set; } = AfterTradeAction.Resume;

    // What to do once the run's stop condition is met (never fires on manual Stop or a fault).
    public AfterRunAction AfterRun { get; set; } = AfterRunAction.StayLoggedIn;

    public GemstoneSpendMode SpendMode { get; set; } = GemstoneSpendMode.SpendAll;
    public int SpendGemsAmount { get; set; } = 1000;
    public int BuyQuantityAmount { get; set; } = 10;
    public int KeepGemstonesReserve { get; set; } = 0;

    public bool ApplyClassOnStart { get; set; } = false;
    public List<ClassQueueEntry> ClassQueue { get; set; } = [];
    public AfterClassQueueDone AfterClassQueueDone { get; set; } = AfterClassQueueDone.KeepGrindingOnLast;

    public bool AutoRepair { get; set; } = false;
    public int AutoRepairThresholdPct { get; set; } = 20;
    public RepairMode RepairMode { get; set; } = RepairMode.SelfThenNpc;
    // Preferred repair NPC; null uses the GC mender.
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

    public bool AutoConsume { get; set; } = false;
    // 0 = only re-eat once Well Fed has fully worn off.
    public int AutoConsumeMinMinutes { get; set; } = 3;
    public List<ConsumableEntry> AutoConsumeItems { get; set; } = [];

    public bool DeclinePartyInvites { get; set; } = false;
    public int DeclineInviteDelayMinSec { get; set; } = 2;
    public int DeclineInviteDelayMaxSec { get; set; } = 6;
    public bool DeclineInviteReply { get; set; } = false;
    public PartyInviteReplyChannel DeclineInviteReplyChannel { get; set; } = PartyInviteReplyChannel.Tell;
    public string DeclineInviteReplyMessage { get; set; } = "";

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

public enum AfterTradeAction
{
    Resume,
    Stop,
}

public enum AfterRunAction
{
    StayLoggedIn,
    Logout,
    ReturnToInn,
    CloseGame,
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

public enum PartyInviteReplyChannel
{
    Tell,
    Say,
    Yell,
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
public sealed class ConsumableEntry
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = "";
    // Status ID granted (Well Fed = 48, Medicated = 49).
    public uint StatusId { get; set; }
    public bool CanBeHq { get; set; }
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
