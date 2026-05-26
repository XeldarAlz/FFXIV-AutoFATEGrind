namespace AutoFateGrind.Core.Modes;

// Registry of grind goals. Built-ins are listed in UI display order; Register() lets future
// modes (relics, Yo-kai) bolt on without touching the enum or the UI grid wiring.
public static class FateGrindModes
{
    private static readonly List<IFateGrindMode> registered =
    [
        new MaxGemstonesMode(),
        new MaxFatesMode(),
        new RunCountMode(),
        new EndlessMode(),
    ];

    public static IReadOnlyList<IFateGrindMode> All => registered;

    public static IFateGrindMode Default => registered[0];

    public static IFateGrindMode? GetById(string? id)
        => id is null ? null : registered.FirstOrDefault(m => m.Id == id);

    public static void Register(IFateGrindMode mode)
    {
        if (registered.Any(m => m.Id == mode.Id)) return;
        registered.Add(mode);
    }

    // Migration from the legacy GrindMode enum to a stable string id.
    public static string IdForLegacy(GrindMode legacy) => legacy switch
    {
        GrindMode.MaxGemstones => "maxgemstones",
        GrindMode.MaxFates     => "maxfates",
        GrindMode.RunCount     => "runcount",
        GrindMode.Endless      => "endless",
        _                      => "maxgemstones",
    };
}
