using AutoFateGrind.Core.Modes;

namespace AutoFateGrind.Core.Tasks;

// Per-run overrides captured at run start. A null field means that category was not overridden.
internal sealed record RunSnapshot
{
    public IFateGrindMode? Mode { get; init; }
    public int? TargetGemstoneCount { get; init; }
    public int? TargetFateCount { get; init; }
    public int? TargetMinutes { get; init; }
    public bool? ApplyClassOnStart { get; init; }
    public IReadOnlyList<ClassQueueEntry>? ClassQueue { get; init; }
    public IReadOnlySet<uint>? AvoidFateIds { get; init; }
}

// Effective settings for the categories IPC can override. Reads the active snapshot during a run and live
// config otherwise. Mutated only on the framework thread, from the controller's start/stop.
internal static class RunContext
{
    private static readonly HashSet<uint> noAvoid = [];

    private static RunSnapshot? active;

    public static void Begin(RunSnapshot snapshot) => active = snapshot;
    public static void End() => active = null;

    public static IFateGrindMode ActiveMode => active?.Mode ?? Plugin.Cfg.ActiveMode;
    public static int TargetGemstoneCount => active?.TargetGemstoneCount ?? Plugin.Cfg.TargetGemstoneCount;
    public static int TargetFateCount => active?.TargetFateCount ?? Plugin.Cfg.TargetFateCount;
    public static int TargetMinutes => active?.TargetMinutes ?? Plugin.Cfg.TargetMinutes;

    public static bool ApplyClassOnStart => active?.ApplyClassOnStart ?? Plugin.Cfg.ApplyClassOnStart;
    public static IReadOnlyList<ClassQueueEntry> ClassQueue => active?.ClassQueue ?? Plugin.Cfg.ClassQueue;

    // Extra ids the caller asked to skip. Additive only, so the saved blacklist is still read live.
    public static IReadOnlySet<uint> AvoidedFateIds => active?.AvoidFateIds ?? noAvoid;
}
