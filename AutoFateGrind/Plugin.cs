using AutoFateGrind.Core;
using AutoFateGrind.Core.Debug;
using AutoFateGrind.Core.Game.Watchers;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Stats;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Windows;
using AutoFateGrind.Windows.Shell;
using clib;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace AutoFateGrind;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    internal static Plugin Instance { get; private set; } = null!;

    internal Configuration Configuration { get; }
    internal static Configuration Cfg { get; private set; } = null!;
    internal WindowSystem WindowSystem { get; } = new("AutoFateGrind");
    internal RunHistory History { get; }
    internal AutoFateController Controller { get; }
    private readonly GmAlertWatcher gmAlertWatcher;
    private readonly PartyInviteWatcher partyInviteWatcher;
    private readonly DutyWatcher dutyWatcher;

    private readonly AppWindow appWindow;
    private readonly CommandInfo primaryCommand;
    private readonly CommandInfo aliasCommand;
    internal LiveFateWindow LiveFateWindow { get; }

    private readonly EventHandler<UnobservedTaskExceptionEventArgs> unobservedTaskHandler;

    public Plugin()
    {
        Instance = this;

        ECommonsMain.Init(PluginInterface, this);
        CLibMain.Init(PluginInterface, this, CLibModule.Automation);
        Core.Game.SharedFates.SharedFateProgress.Initialize();

        unobservedTaskHandler = OnUnobservedTaskException;
        TaskScheduler.UnobservedTaskException += unobservedTaskHandler;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Cfg = Configuration;
        if (Configuration.MigrateGoal()) Configuration.Save();
        if (Core.Zones.CityCatalog.MigrateSelection(Configuration.HumanizerCities)) Configuration.Save();
        History = new RunHistory();
        Controller = new AutoFateController();
        gmAlertWatcher = new GmAlertWatcher();
        partyInviteWatcher = new PartyInviteWatcher();
        dutyWatcher = new DutyWatcher();

        InitializeLocalization();
        GameTextGlyphs.Collect(Configuration, History);
        Fonts.Initialize(PluginInterface.UiBuilder, PluginDirectory);
        appWindow = new AppWindow(this);
        LiveFateWindow = new LiveFateWindow(this) { IsOpen = Configuration.ShowLivePopout };

        WindowSystem.AddWindow(appWindow);
        WindowSystem.AddWindow(LiveFateWindow);

        primaryCommand = new CommandInfo(OnCommand) { HelpMessage = Loc.T(L.Plugin.CommandHelp) };
        aliasCommand = new CommandInfo(OnCommand) { HelpMessage = Loc.T(L.Plugin.CommandHelpAlias) };
        CommandManager.AddHandler(AfgConstants.PrimaryCommand, primaryCommand);
        CommandManager.AddHandler(AfgConstants.AliasCommand, aliasCommand);

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Svc.ClientState.Login += OnLogin;
        if (Svc.ClientState.IsLoggedIn) OnLogin();
    }

    // vnavmesh/BossMod run their obstacle-map and pathfind IPC on fire-and-forget Tasks we never get a
    // handle to (we only see a TaskStatus), so we can't ObserveLeak them. When one faults — e.g. a bitmap
    // build issued while the zone navmesh is still creating — its exception reaches the finalizer as
    // unobserved and gets rethrown as log noise. Mark only those (matched by the vnavmesh stack) observed.
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (e.Observed) return;
        if (e.Exception.ToString().Contains("Navmesh.IPCProvider"))
        {
            e.SetObserved();
            RunLog.Debug($"Observed vnavmesh IPC task fault: {e.Exception.GetBaseException().Message}");
        }
    }

    public void Dispose()
    {
        TaskScheduler.UnobservedTaskException -= unobservedTaskHandler;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Svc.ClientState.Login -= OnLogin;

        Core.Ipc.BossModFateHelper.ReleaseChocobo();
        Core.Ipc.BossModMovementTuning.Release();

        WindowSystem.RemoveAllWindows();
        appWindow.Dispose();
        LiveFateWindow.Dispose();
        Fonts.Dispose();

        CommandManager.RemoveHandler(AfgConstants.PrimaryCommand);
        CommandManager.RemoveHandler(AfgConstants.AliasCommand);

        gmAlertWatcher.Dispose();
        partyInviteWatcher.Dispose();
        dutyWatcher.Dispose();

        Core.Game.SharedFates.SharedFateProgress.Shutdown();
        CLibMain.Dispose();
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        var space = trimmed.IndexOf(' ');
        var verb = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        var rest = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();
        switch (verb)
        {
            case "config": ToggleConfigUi(); break;
            case "about": ToggleAboutUi(); break;
            case "deps": case "dependencies": ToggleDependenciesUi(); break;
            case "stats": case "history": ToggleHistoryUi(); break;
            case "log": ToggleLogUi(); break;
            case "changelog": ToggleChangelogUi(); break;
            case "pause": case "resume": Controller.TogglePause(); break;
            case "target": TargetDumper.Dump(); break;
            case "start": StartFromCommand(); break;
            case "stop": if (rest.Equals("soft", StringComparison.OrdinalIgnoreCase)) Controller.StopWhenSafe(); else Controller.Stop(); break;
            case "softstop": Controller.StopWhenSafe(); break;
            case "run": RunCountFromCommand(rest); break;
            default: ToggleMainUi(); break;
        }
    }

    private void StartFromCommand()
    {
        if (Controller.Running)
        {
            Svc.Chat.Print("[AFG] A run is already going. Use /afg stop or /afg stop soft first.");
            return;
        }

        var zones = Core.Zones.ZoneSelection.ResolveStartList(Configuration);
        if (zones.Count == 0)
        {
            Svc.Chat.PrintError("[AFG] Nothing to start: pick zones in the window first.");
            return;
        }

        Controller.RunAll(zones);
    }

    // Switches on the FATE cap for the current goal; the count is the session total, so it can raise or lower a running cap too.
    private void RunCountFromCommand(string countText)
    {
        if (!int.TryParse(countText, out var count) || count <= 0)
        {
            Svc.Chat.PrintError("[AFG] Usage: /afg run <count>, for example /afg run 30.");
            return;
        }

        Configuration.StopAfterFatesEnabled = true;
        Configuration.TargetFateCount = Math.Clamp(count, 1, Core.Modes.RunLimits.MaxFates);
        Configuration.Save();
        if (Controller.Running)
        {
            Svc.Chat.Print($"[AFG] Limit set: this run stops after {Configuration.TargetFateCount} FATEs.");
            return;
        }

        StartFromCommand();
    }

    public void OnLanguageChanged()
    {
        primaryCommand.HelpMessage = Loc.T(L.Plugin.CommandHelp);
        aliasCommand.HelpMessage = Loc.T(L.Plugin.CommandHelpAlias);
    }

    private static string PluginDirectory => PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;

    private static void InitializeLocalization()
    {
        var directory = Path.Combine(PluginDirectory, "Localization");
        if (string.IsNullOrEmpty(Cfg.Language))
        {
            Cfg.Language = DetectLanguage();
            Cfg.Save();
        }

        Loc.Initialize(Cfg.Language, directory);
    }

    private static string DetectLanguage()
    {
        var dalamudLanguage = PluginInterface.UiLanguage;
        if (Languages.IsKnown(dalamudLanguage)) return Languages.Resolve(dalamudLanguage).Code;

        switch (Svc.ClientState.ClientLanguage)
        {
            case Dalamud.Game.ClientLanguage.German: return Languages.German.Code;
            case Dalamud.Game.ClientLanguage.French: return Languages.French.Code;
            case Dalamud.Game.ClientLanguage.Japanese: return Languages.Japanese.Code;
        }

        var osLanguage = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        return Languages.IsKnown(osLanguage) ? Languages.Resolve(osLanguage).Code : Languages.English.Code;
    }

    private void OnLogin()
    {
        if (!Configuration.AutoShowOnLogin) return;
        appWindow.Show(AppWindow.Page.Grind);
    }

    public void ToggleMainUi() => appWindow.Toggle();
    public void ToggleConfigUi() => appWindow.TogglePage(AppWindow.Page.Settings);
    public void ToggleAboutUi() => appWindow.TogglePage(AppWindow.Page.About);
    public void ToggleDependenciesUi() => appWindow.TogglePage(AppWindow.Page.Plugins);
    public void ToggleHistoryUi() => appWindow.TogglePage(AppWindow.Page.History);
    public void ToggleLogUi() => appWindow.TogglePage(AppWindow.Page.Log);
    public void ToggleChangelogUi() => appWindow.TogglePage(AppWindow.Page.Changelog);
}
