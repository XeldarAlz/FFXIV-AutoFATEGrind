namespace AutoFateGrind;

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
