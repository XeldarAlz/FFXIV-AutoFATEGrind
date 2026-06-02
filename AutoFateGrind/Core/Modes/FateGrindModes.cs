namespace AutoFateGrind.Core.Modes;

// Built-ins are listed in UI display order; Register() lets future modes bolt on without UI churn.
public static class FateGrindModes
{
    private static readonly List<IFateGrindMode> registered =
    [
        new MaxGemstonesMode(),
        new RunCountMode(),
        new TimeBoxedMode(),
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

    public static string IdForLegacy(GrindMode legacy) => legacy switch
    {
        GrindMode.MaxGemstones => MaxGemstonesMode.ModeId,
        GrindMode.RunCount     => RunCountMode.ModeId,
        GrindMode.Endless      => EndlessMode.ModeId,
        _                      => MaxGemstonesMode.ModeId,
    };
}
