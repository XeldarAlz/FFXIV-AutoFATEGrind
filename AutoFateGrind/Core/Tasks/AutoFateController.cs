using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game.Player;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;
using clib.Services;

namespace AutoFateGrind.Core.Tasks;

internal sealed partial class AutoFateController
{
    public bool Running => Svc.Automation.Running;
    public string Status => Svc.Automation.CurrentTask?.Status ?? "Idle";
    public AutoPhase Phase { get; private set; } = AutoPhase.Idle;

    private AutoFateSession? session;
    private IReadOnlyList<ZoneInfo> activeZones = [];
    public AutoFateSession? SessionSnapshot => session;
    public IReadOnlyList<ZoneInfo> ActiveZones => activeZones;

    private static readonly Random rng = new();

    private static void Diag(string message)
        => ECommons.DalamudServices.Svc.Log.Info($"{AfgConstants.LogPrefix} {message}");

    // First active-zone index whose territory matches origin (first match wins), or fallback when origin is
    // null / not in the current selection.
    private int ResumeIndexFor(ZoneInfo? origin, int fallback = 0)
    {
        if (origin is null) return fallback;
        for (var i = 0; i < activeZones.Count; i++)
            if (activeZones[i].TerritoryId == origin.TerritoryId) return i;
        return fallback;
    }

    public void RunAll(IEnumerable<ZoneInfo> zones)
    {
        activeZones = zones.ToList();
        if (activeZones.Count == 0)
        {
            Diag("Start aborted: no zones selected.");
            // Reachable from /afg toggle and IPC, which have no on-screen gate like the Start button does.
            ECommons.DalamudServices.Svc.Chat.PrintError("[AFG] Cannot start: pick at least one zone first.");
            return;
        }

        if (!ExternalPlugins.AllRequiredInstalled())
        {
            var missing = string.Join(", ", ExternalPlugins.All
                .Where(p => ExternalPlugins.Catalog[p].Required && !ExternalPlugins.IsInstalled(p))
                .Select(p => ExternalPlugins.Catalog[p].DisplayName));
            Diag($"Start aborted: required plugins missing ({missing}).");
            ECommons.DalamudServices.Svc.Chat.PrintError($"[AFG] Cannot start — install all required plugins first: {missing}.");
            return;
        }

        var startWallet = GemstoneCatalog.CurrentWalletCount();
        var s = new AutoFateSession
        {
            GemstoneCurrent = startWallet,
        };
        s.CaptureStartExp();
        session = s;
        Diag($"Run starting: {activeZones.Count} zone(s), mode {RunContext.ActiveMode.DisplayName}, wallet {startWallet}g, threshold {Plugin.Cfg.TradeThreshold}g, trade-on-cap {(Plugin.Cfg.TradeOnCap ? "on" : "off")}.");

        ApplyStartingClass();
        StartFateGrind(0, s);
    }

    private static void ApplyStartingClass()
    {
        if (!RunContext.ApplyClassOnStart) return;
        if (RunContext.ClassQueue.Count == 0) return;

        var idx = ClassSwitcher.FindActiveEntryIndex(RunContext.ClassQueue);
        if (idx < 0)
        {
            ECommons.DalamudServices.Svc.Chat.Print("[AFG] Class queue: every entry is at its level cap, staying on current class.");
            return;
        }
        var entry = RunContext.ClassQueue[idx];
        var label = $"gearset {entry.GearsetIndex} ({ClassSwitcher.JobNameForUserIndex(entry.GearsetIndex)})";
        if (ClassSwitcher.TryEquip(entry))
            ECommons.DalamudServices.Svc.Chat.Print($"[AFG] Switching to {label}.");
        else
            ECommons.DalamudServices.Svc.Chat.PrintError($"[AFG] Could not equip {label} (game refused — combat, mount, or transient lock?). See /xllog for details.");
    }

    public void Stop()
    {
        var ending = session;
        try
        {
            Svc.Automation.Stop();
        }
        finally
        {
            // Clear run state even if cancellation threw, so a fault cannot leave the snapshot armed.
            FinalizeRun(ending);
            session = null;
            activeZones = [];
            RunContext.End();
            Phase = AutoPhase.Idle;
            if (ending is not null) Diag("Stop requested; session cleared.");
        }
    }

    // Starts a run with per-run overrides. A null argument keeps the saved config for that category, and
    // overrides are never persisted. Framework thread only. Returns true only if a run began.
    public bool IpcStartWith(List<uint>? zones, string? modeId, int? stopValue, int? gearsetIndex, List<uint>? avoidedFates)
    {
        if (Running)
        {
            Diag("IPC start ignored: a run is already active.");
            return false;
        }

        IFateGrindMode? overrideMode = null;
        if (modeId is not null)
        {
            overrideMode = FateGrindModes.GetById(modeId);
            if (overrideMode is null)
            {
                Diag($"IPC start aborted: unknown grind mode '{modeId}'.");
                return false;
            }
        }

        var snapshot = BuildSnapshot(overrideMode, modeId, stopValue, gearsetIndex, avoidedFates);
        var startList = zones is null
            ? ZoneSelection.ResolveStartList(Plugin.Cfg)
            : ZoneSelection.Resolve(zones.Distinct());

        RunContext.Begin(snapshot);
        try
        {
            RunAll(startList);
        }
        catch
        {
            RunContext.End();
            throw;
        }

        if (!Running)
        {
            RunContext.End();
            return false;
        }
        return true;
    }

    public bool IpcStart() => IpcStartWith(null, null, null, null, null);

    public void IpcStop() => Stop();

    public bool IpcToggle()
    {
        if (Running)
        {
            Stop();
            return false;
        }
        return IpcStart();
    }

    // Resolves the IPC arguments into a run snapshot. Reads live gearset state, so framework thread only.
    private static RunSnapshot BuildSnapshot(IFateGrindMode? overrideMode, string? modeId, int? stopValue, int? gearsetIndex, List<uint>? avoidedFates)
    {
        // stopValue targets the effective mode, so a caller can retarget the saved mode without restating it.
        var effectiveModeId = modeId ?? Plugin.Cfg.ActiveMode.Id;
        int? gemTarget = null, fateTarget = null, minuteTarget = null;
        if (stopValue is int value)
        {
            switch (effectiveModeId)
            {
                case MaxGemstonesMode.ModeId: gemTarget = Math.Clamp(value, 1, AfgConstants.BicolorCap); break;
                case RunCountMode.ModeId: fateTarget = Math.Max(1, value); break;
                case TimeBoxedMode.ModeId: minuteTarget = Math.Max(1, value); break;
            }
        }

        bool? applyClass = null;
        IReadOnlyList<ClassQueueEntry>? classQueue = null;
        if (gearsetIndex is int requested)
        {
            var userIndex = requested is >= 1 and <= 100 ? (byte)requested : (byte)0;
            var jobId = userIndex == 0 ? (byte)0 : ClassSwitcher.JobIdForUserIndex(userIndex);
            if (jobId != 0 && ClassSwitcher.IsCombatJob(jobId))
            {
                applyClass = true;
                classQueue = new List<ClassQueueEntry> { new() { GearsetIndex = userIndex, JobId = jobId, StopAtLevel = 0 } };
            }
            else
            {
                Diag($"IPC start: gearset {requested} is not a usable combat gearset; keeping the current class.");
                applyClass = false;
                classQueue = [];
            }
        }

        var avoidIds = avoidedFates is { Count: > 0 } ? new HashSet<uint>(avoidedFates) : null;

        return new RunSnapshot
        {
            Mode = overrideMode,
            TargetGemstoneCount = gemTarget,
            TargetFateCount = fateTarget,
            TargetMinutes = minuteTarget,
            ApplyClassOnStart = applyClass,
            ClassQueue = classQueue,
            AvoidFateIds = avoidIds,
        };
    }

}

internal enum AutoPhase { Idle, Grinding, Trading, Repairing, Humanizing, Finishing }
