using AutoFateGrind.Core.Zones;

namespace AutoFateGrind.Core.Modes;

// Snapshot passed to a mode when it evaluates completion / progress. Modes read live game state
// (gemstone wallet, etc.) and Plugin.Cfg targets directly; this only carries what's run-scoped.
public readonly struct ModeContext
{
    public int CompletedCount { get; init; }
    public IReadOnlyList<ZoneInfo> Zones { get; init; }
}

// A grind goal. Built-ins are stop-condition oriented; the interface is shaped so future
// item-collection modes (relics, Yo-kai medals) can plug in by adding zone scoping + targets later.
public interface IFateGrindMode
{
    // Stable serialization key — never change once shipped (saved in config as ModeId).
    string Id { get; }

    string DisplayName { get; }
    string Description { get; }

    // True for goals that rotate the ShB+ Shared-FATE achievement zones and drive auto zone-selection.
    bool RotatesSharedFateZones => false;

    // Stop condition, evaluated each tick.
    bool IsComplete(ModeContext ctx);

    // Optional short progress chip for the UI (e.g. "740 / 1500 gems"); null hides it.
    string? GetRemainingDisplay(ModeContext ctx) => null;
}
