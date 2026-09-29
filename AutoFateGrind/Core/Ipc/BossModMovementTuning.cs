using AutoFateGrind.Core.Tasks;

namespace AutoFateGrind.Core.Ipc;

// BossMod dodges the instant an AoE is telegraphed and hugs its edge, so AFG characters sharing a FATE step out on
// the same frame to the same spot. Rolling NormalMovement's delay and cushion levels per FATE breaks that (issue #84).
internal static class BossModMovementTuning
{
    public const string Module = "BossMod.Autorotation.MiscAI.NormalMovement";

    private const string SeparateDodgeDelayTrack = "SeparateDodgeDelay";
    private const string DodgeDelayTrack = "DodgeDelayMovement";
    private const string MoveDelayTrack = "DelayMovement";
    private const string CushionTrack = "ForbiddenZoneCushion";
    private const string EnabledOption = "Enabled";

    // Option names in BossMod's order; the config stores indexes into these.
    public static readonly string[] DelayOptions = ["None", "Short", "Long"];
    public static readonly string[] CushionOptions = ["None", "Small", "Medium", "Large"];

    private static readonly string[] tracks = [SeparateDodgeDelayTrack, DodgeDelayTrack, MoveDelayTrack, CushionTrack];

    private static string? overridePreset;
    private static bool failureLogged;

    // Null when BossMod can't hand the preset back, so the caller doesn't warn about a preset it never saw.
    public static bool? PresetHasModule(string preset)
        => BossModIPC.Instance.GetPreset(preset) is { } serialized
            ? serialized.Contains(Module, StringComparison.Ordinal)
            : null;

    public static void Apply(string preset, uint fateId)
    {
        var cfg = Plugin.Cfg;
        if (!cfg.CombatMovementEnabled || !BossModIPC.Instance.CanClearTransientStrategy)
        {
            ClearOverrides();
            return;
        }

        if (overridePreset != preset)
        {
            ClearOverrides();
        }

        var dodgeDelay = Roll(DelayOptions, cfg.CombatDodgeDelayMin, cfg.CombatDodgeDelayMax);
        var moveDelay = Roll(DelayOptions, cfg.CombatMoveDelayMin, cfg.CombatMoveDelayMax);
        var cushion = Roll(CushionOptions, cfg.CombatCushionMin, cfg.CombatCushionMax);

        var ipc = BossModIPC.Instance;
        var applied = ipc.AddTransientStrategy(preset, Module, SeparateDodgeDelayTrack, EnabledOption)
                   && ipc.AddTransientStrategy(preset, Module, DodgeDelayTrack, dodgeDelay)
                   && ipc.AddTransientStrategy(preset, Module, MoveDelayTrack, moveDelay)
                   && ipc.AddTransientStrategy(preset, Module, CushionTrack, cushion);
        if (!applied)
        {
            // A partial set would leave the dodge delay split without the rolled values; take back whatever landed.
            foreach (var track in tracks)
            {
                ipc.ClearTransientStrategy(preset, Module, track);
            }
            overridePreset = null;
            if (!failureLogged)
            {
                failureLogged = true;
                RunLog.Warning($"Combat movement variation not applied to '{preset}' (no NormalMovement module in the preset, or a BossMod build without these options).");
            }
            return;
        }

        overridePreset = preset;
        RunLog.Info($"Combat movement for FATE {fateId} on '{preset}': dodge delay {dodgeDelay}, movement delay {moveDelay}, cushion {cushion}.");
    }

    // Transients live on BossMod's in-memory preset and outlast AFG, so a run end or unload has to take them back.
    public static void Release()
    {
        ClearOverrides();
        failureLogged = false;
    }

    private static void ClearOverrides()
    {
        if (overridePreset is null)
        {
            return;
        }

        foreach (var track in tracks)
        {
            BossModIPC.Instance.ClearTransientStrategy(overridePreset, Module, track);
        }
        RunLog.Info($"Combat movement overrides cleared from '{overridePreset}'.");
        overridePreset = null;
    }

    private static string Roll(string[] options, int minimum, int maximum)
    {
        var last = options.Length - 1;
        return options[Pacing.RollBetween(Math.Clamp(minimum, 0, last), Math.Clamp(maximum, 0, last))];
    }
}
