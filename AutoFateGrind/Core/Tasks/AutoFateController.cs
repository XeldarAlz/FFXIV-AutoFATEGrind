using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game.Player;
using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;
using clib.Services;

namespace AutoFateGrind.Core.Tasks;

internal sealed partial class AutoFateController
{
    public bool Running => Svc.Automation.Running || Paused;

    public string Status => PauseReason switch
    {
        PauseReason.InContent => "Paused while you are in content",
        PauseReason.Manual    => "Paused",
        _                     => Svc.Automation.CurrentTask?.Status ?? "Idle",
    };

    public AutoPhase Phase { get; private set; } = AutoPhase.Idle;

    private AutoFateSession? session;
    private IReadOnlyList<ZoneInfo> activeZones = [];
    public AutoFateSession? SessionSnapshot => session;
    public int ActiveZoneCount => activeZones.Count;

    private static readonly Random rng = new();

    private static void Diag(string message)
        => RunLog.Info(message);

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

        PauseReason = PauseReason.None;

        var startWallet = GemstoneCatalog.CurrentWalletCount();
        var s = new AutoFateSession
        {
            GemstoneCurrent = startWallet,
        };
        s.CaptureStartExp();

        var startIndex = 0;
        if (ZoneSelection.GoalPlansZones(Plugin.Cfg))
        {
            if (!PlanYokaiZones(s))
            {
                Diag("Start aborted: no yo-kai is left to farm.");
                activeZones = [];
                return;
            }
            startIndex = CurrentTerritoryIndex();
        }

        session = s;
        Diag($"Run starting: {activeZones.Count} zone(s), mode {Plugin.Cfg.ActiveMode.DisplayName}, wallet {startWallet}g, threshold {Plugin.Cfg.TradeThreshold}g, trade-on-cap {(Plugin.Cfg.TradeOnCap ? "on" : "off")}.");

        ApplyStartingClass();
        StartFateGrind(startIndex, s);
    }

    private bool PlanYokaiZones(AutoFateSession owningSession)
    {
        var targetIndex = YokaiProgress.ResolveTargetIndex(Plugin.Cfg, owningSession.YokaiTargetMinionId);
        var zones = YokaiProgress.ZonesFor(targetIndex);
        Diag($"Yo-kai roster: {YokaiProgress.DescribeRoster(Plugin.Cfg)}");
        if (zones.Count == 0)
        {
            return false;
        }

        owningSession.YokaiTargetMinionId = YokaiCatalog.Entries[targetIndex].MinionId;
        activeZones = zones;
        Diag($"Yo-kai target: {YokaiProgress.MinionName(targetIndex)} ({YokaiProgress.Statuses[targetIndex].Medals}/{YokaiProgress.MedalTarget(Plugin.Cfg)} medals), {zones.Count} zone(s).");
        return true;
    }

    private int CurrentTerritoryIndex()
    {
        var territory = ECommons.DalamudServices.Svc.ClientState.TerritoryType;
        for (var index = 0; index < activeZones.Count; index++)
        {
            if (activeZones[index].TerritoryId == territory)
            {
                return index;
            }
        }

        return 0;
    }

    private static void ApplyStartingClass()
    {
        var cfg = Plugin.Cfg;
        if (!cfg.ApplyClassOnStart) return;
        if (cfg.ClassQueue.Count == 0) return;

        var idx = ClassSwitcher.FindActiveEntryIndex(cfg.ClassQueue);
        if (idx < 0)
        {
            ECommons.DalamudServices.Svc.Chat.Print("[AFG] Class queue: every entry is at its level cap, staying on current class.");
            return;
        }
        var entry = cfg.ClassQueue[idx];
        var label = $"gearset {entry.GearsetIndex} ({ClassSwitcher.JobNameForUserIndex(entry.GearsetIndex)})";
        if (ClassSwitcher.TryEquip(entry))
            ECommons.DalamudServices.Svc.Chat.Print($"[AFG] Switching to {label}.");
        else
            ECommons.DalamudServices.Svc.Chat.PrintError($"[AFG] Could not equip {label} (game refused — combat, mount, or transient lock?). See /xllog for details.");
    }

    public void Stop()
    {
        var ending = session;
        currentTask = null;
        grindTask = null;
        PauseReason = PauseReason.None;
        Svc.Automation.Stop();
        FinalizeRun(ending);
        session = null;
        activeZones = [];
        Phase = AutoPhase.Idle;
        if (ending is not null) Diag("Stop requested; session cleared.");
    }

}

internal enum AutoPhase { Idle, Grinding, Trading, Repairing, Humanizing, Finishing, Paused }
