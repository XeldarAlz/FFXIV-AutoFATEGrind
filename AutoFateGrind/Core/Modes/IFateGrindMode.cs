using AutoFateGrind.Core.Zones;

namespace AutoFateGrind.Core.Modes;

public readonly struct ModeContext
{
    public int CompletedCount { get; init; }
    public IReadOnlyList<ZoneInfo> Zones { get; init; }
}

public interface IFateGrindMode
{
    // Stable serialization key — never change once shipped (persisted in config as ModeId).
    string Id { get; }

    string DisplayName { get; }
    string Description { get; }

    // True for goals that rotate the ShB+ Shared-FATE achievement zones and drive auto zone-selection.
    bool RotatesSharedFateZones => false;

    bool IsComplete(ModeContext ctx);

    string? GetRemainingDisplay(ModeContext ctx) => null;
}
