namespace AutoFateGrind;

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
