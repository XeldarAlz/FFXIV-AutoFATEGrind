namespace AutoFateGrind.Core.Localization;

internal static class L
{
    internal static class Common
    {
        public static readonly LocString Close = new("common.close", "Close");
        public static readonly LocString Cancel = new("common.cancel", "Cancel");
        public static readonly LocString Remove = new("common.remove", "Remove");
        public static readonly LocString Add = new("common.add", "Add");
        public static readonly LocString Clear = new("common.clear", "Clear");
        public static readonly LocString SelectAll = new("common.selectAll", "Select all");
        public static readonly LocString MoveUp = new("common.moveUp", "Move up");
        public static readonly LocString MoveDown = new("common.moveDown", "Move down");
        public static readonly LocString Ascending = new("common.ascending", "Ascending");
        public static readonly LocString Descending = new("common.descending", "Descending");
        public static readonly LocString Pause = new("common.pause", "Pause");
        public static readonly LocString Resume = new("common.resume", "Resume");
        public static readonly LocString StopRun = new("common.stopRun", "Stop the run");
        public static readonly LocString Working = new("common.working", "Working…");
        public static readonly LocString PlayerNotLoaded = new("common.playerNotLoaded", "Player not loaded.");
        public static readonly LocString DragAdjustHint = new("common.dragAdjustHint", "Drag to adjust · Ctrl+click to type");
        public static readonly LocString Preview = new("common.preview", "Preview");
        public static readonly LocString Test = new("common.test", "Test");
        public static readonly LocString NoMatches = new("common.noMatches", "Nothing matches “{0}”.");
    }

    internal static class Shell
    {
        public static readonly LocString NavGrind = new("shell.nav.grind", "Grind");
        public static readonly LocString NavSettings = new("shell.nav.settings", "Settings");
        public static readonly LocString NavHistory = new("shell.nav.history", "History");
        public static readonly LocString NavPlugins = new("shell.nav.plugins", "Plugins");
        public static readonly LocString NavAbout = new("shell.nav.about", "About");
        public static readonly LocString StatusRunning = new("shell.status.running", "Running");
        public static readonly LocString StatusPaused = new("shell.status.paused", "Paused");
        public static readonly LocString StatusReady = new("shell.status.ready", "Ready");
        public static readonly LocString StatusPickZones = new("shell.status.pickZones", "Pick zones");
        public static readonly LocString StatusSetupNeeded = new("shell.status.setupNeeded", "Setup needed");
        public static readonly LocString StatusIdle = new("shell.status.idle", "Idle");
        public static readonly LocString Minimize = new("shell.minimize", "Minimize to the title strip");
        public static readonly LocString Restore = new("shell.restore", "Restore the window");
        public static readonly LocString ShowLiveTracker = new("shell.showLiveTracker", "Show the live FATE tracker");
        public static readonly LocString HideLiveTracker = new("shell.hideLiveTracker", "Hide the live FATE tracker");
        public static readonly LocString ResumeBlocked = new("shell.resumeBlocked", "Resumes automatically once you leave the duty");
        public static readonly LocString GreetingMorning = new("shell.greeting.morning", "Good morning");
        public static readonly LocString GreetingAfternoon = new("shell.greeting.afternoon", "Good afternoon");
        public static readonly LocString GreetingEvening = new("shell.greeting.evening", "Good evening");
        public static readonly LocString GreetingNight = new("shell.greeting.night", "Late night");
    }

    internal static class Grind
    {
        public static readonly LocString TitleSetupNeeded = new("grind.title.setupNeeded", "Setup needed");
        public static readonly LocString DetailSetupNeeded = new("grind.detail.setupNeeded", "Install the required plugins before your first run.");
        public static readonly LocString TitlePickZones = new("grind.title.pickZones", "Pick a zone to begin");
        public static readonly LocString DetailPickZones = new("grind.detail.pickZones", "Tick zones in the library and they'll appear in your plan.");
        public static readonly LocString TitleReady = new("grind.title.ready", "Ready to grind");
        public static readonly LocString DetailReady = new("grind.detail.ready", "Everything's set. Press Start whenever you're ready.");
        public static readonly LocString TitleRunning = new("grind.title.running", "Grinding");
        public static readonly LocString TitlePaused = new("grind.title.paused", "Paused");
        public static readonly LocString DetailPausedInContent = new("grind.detail.pausedInContent", "Resumes once you leave the duty");
        public static readonly LocString DetailPausedManual = new("grind.detail.pausedManual", "Resume whenever you're ready");
        public static readonly LocString OpenPlugins = new("grind.openPlugins", "Open plugins");
        public static readonly LocString NoRunsYet = new("grind.noRunsYet", "No runs yet");
        public static readonly LocString StatsAppearHere = new("grind.statsAppearHere", "your stats will appear here");
        public static readonly LocString LastRun = new("grind.lastRun", "Last run  ·  {0} FATEs");
        public static readonly LocString LastRunDetail = new("grind.lastRunDetail", "{0}  ·  {1} gems");

        public static readonly LocString Plan = new("grind.plan", "Plan");
        public static readonly LocString SentenceGrind = new("grind.sentence.grind", "Grind");
        public static readonly LocString SentenceUntil = new("grind.sentence.until", "until");
        public static readonly LocString SentenceThen = new("grind.sentence.then", "then");
        public static readonly LocString SentenceEnd = new("grind.sentence.end", ".");
        public static readonly LocString ZonesNone = new("grind.zonesNone", "no zones yet");
        public static readonly LocPlural ZonesCount = new("grind.zonesCount", "{0} zone", "{0} zones");
        public static readonly LocString GoalGemstones = new("grind.goal.gemstones", "{0} gemstones");
        public static readonly LocString GoalFates = new("grind.goal.fates", "{0} FATEs");
        public static readonly LocString GoalMinutes = new("grind.goal.minutes", "{0} minutes");
        public static readonly LocString GoalEndless = new("grind.goal.endless", "you stop it");
        public static readonly LocString PlanLocked = new("grind.planLocked", "Stop the run to change the plan.");
        public static readonly LocString AfterStayToken = new("grind.after.stay.token", "stay where you are");
        public static readonly LocString AfterStayName = new("grind.after.stay.name", "Stay where you are");
        public static readonly LocString AfterStayDetail = new("grind.after.stay.detail", "Just stop. You're left standing wherever the last FATE ended.");
        public static readonly LocString AfterInnToken = new("grind.after.inn.token", "return to the inn");
        public static readonly LocString AfterInnName = new("grind.after.inn.name", "Return to the inn");
        public static readonly LocString AfterInnDetail = new("grind.after.inn.detail", "Travel to your Grand Company city and enter the inn room.");
        public static readonly LocString AfterLogoutToken = new("grind.after.logout.token", "log out");
        public static readonly LocString AfterLogoutName = new("grind.after.logout.name", "Log out to title");
        public static readonly LocString AfterLogoutDetail = new("grind.after.logout.detail", "Log out to the title screen.");
        public static readonly LocString AfterCloseToken = new("grind.after.close.token", "close the game");
        public static readonly LocString AfterCloseName = new("grind.after.close.name", "Close the game");
        public static readonly LocString AfterCloseDetail = new("grind.after.close.detail", "Close FFXIV entirely (via XIVLauncher's /xlkill).");
        public static readonly LocString WhenGoalReached = new("grind.whenGoalReached", "When the goal is reached");
        public static readonly LocString ModeGemstones = new("grind.mode.gemstones", "Gemstones");
        public static readonly LocString ModeFates = new("grind.mode.fates", "FATEs");
        public static readonly LocString ModeTime = new("grind.mode.time", "Time");
        public static readonly LocString ModeEndless = new("grind.mode.endless", "Endless");
        public static readonly LocString EndlessNote = new("grind.endlessNote", "Rotates your zones until you press Stop.");
        public static readonly LocString StopAt = new("grind.stopAt", "Stop at");
        public static readonly LocString StopAfter = new("grind.stopAfter", "Stop after");
        public static readonly LocString UnitGemstones = new("grind.unit.gemstones", "gemstones");
        public static readonly LocString UnitFates = new("grind.unit.fates", "FATEs");
        public static readonly LocString UnitMinutes = new("grind.unit.minutes", "minutes");
        public static readonly LocString NoteGemstones = new("grind.note.gemstones", "You have {0} right now.");
        public static readonly LocString NoteFates = new("grind.note.fates", "Counts every FATE that hands out a reward.");
        public static readonly LocString NoteMinutes = new("grind.note.minutes", "Paused time does not count.");

        public static readonly LocString Zones = new("grind.library.zones", "Zones");
        public static readonly LocString SearchZones = new("grind.library.search", "Search zones");
        public static readonly LocString ClearSearch = new("grind.library.clearSearch", "Clear search");
        public static readonly LocString NoMatches = new("grind.library.noMatches", "No zones match “{0}”.");
        public static readonly LocPlural Matches = new("grind.library.matches", "{0} zone matches “{1}”  ·  every expansion", "{0} zones match “{1}”  ·  every expansion");
        public static readonly LocString SelectedSummary = new("grind.library.selected", "{0} of {1} selected");
        public static readonly LocPlural ActiveFates = new("grind.library.activeFates", "{0} FATE active here right now.", "{0} FATEs active here right now.");
        public static readonly LocString ZonesLockedRunning = new("grind.library.lockedRunning", "Stop the run to change your zones.");
        public static readonly LocString LockedGateway = new("grind.library.lockedGateway", "Locked: attune {0}; {1} is entered from there over the aethernet.");
        public static readonly LocString LockedAetheryte = new("grind.library.lockedAetheryte", "Locked: attune an aetheryte in this zone first.");
        public static readonly LocString OrderHint = new("grind.order.hint", "Your grind order will appear here once you pick a zone.");
        public static readonly LocString DragToReorder = new("grind.order.drag", "Drag to reorder");
        public static readonly LocString RemoveFromOrder = new("grind.order.remove", "Remove from grind order");

        public static readonly LocString ExpansionArr = new("grind.expansion.arr", "A Realm Reborn");
        public static readonly LocString ExpansionHw = new("grind.expansion.hw", "Heavensward");
        public static readonly LocString ExpansionSb = new("grind.expansion.sb", "Stormblood");
        public static readonly LocString ExpansionShb = new("grind.expansion.shb", "Shadowbringers");
        public static readonly LocString ExpansionEw = new("grind.expansion.ew", "Endwalker");
        public static readonly LocString ExpansionDt = new("grind.expansion.dt", "Dawntrail");

        public static readonly LocString Start = new("grind.start", "START");
        public static readonly LocString Stop = new("grind.stop", "STOP");
        public static readonly LocString PauseCaps = new("grind.pause", "PAUSE");
        public static readonly LocString ResumeCaps = new("grind.resume", "RESUME");
        public static readonly LocString InContent = new("grind.inContent", "in content");
        public static readonly LocString ReasonInstall = new("grind.reason.install", "install the required plugins");
        public static readonly LocString ReasonPickZone = new("grind.reason.pickZone", "pick at least one zone");
        public static readonly LocString StartSub = new("grind.startSub", "{0}  ·  {1}");
        public static readonly LocString StopsAtGems = new("grind.stops.gems", "stops at {0} gems");
        public static readonly LocString StopsAfterFates = new("grind.stops.fates", "stops after {0} FATEs");
        public static readonly LocString StopsAfterMinutes = new("grind.stops.minutes", "stops after {0} min");
        public static readonly LocString StopsWhenYouStop = new("grind.stops.endless", "runs until you stop");
        public static readonly LocString StateRunning = new("grind.state.running", "running");
        public static readonly LocString StatePaused = new("grind.state.paused", "paused");
        public static readonly LocString StopSub = new("grind.stopSub", "{0} · {1}");
    }

    internal static class Run
    {
        public static readonly LocString PhaseTrading = new("run.phase.trading", "Trading gemstones");
        public static readonly LocString PhaseRepairing = new("run.phase.repairing", "Repairing gear");
        public static readonly LocString PhaseBreak = new("run.phase.break", "Taking a break");
        public static readonly LocString PhaseFinishing = new("run.phase.finishing", "Finishing up");
        public static readonly LocString PhaseGrinding = new("run.phase.grinding", "Grinding FATEs");
        public static readonly LocString PhaseEngaging = new("run.phase.engaging", "Engaging FATE");
        public static readonly LocString PhaseStandingBy = new("run.phase.standingBy", "Standing by");
        public static readonly LocString PhaseReady = new("run.phase.ready", "Ready");
        public static readonly LocString PhasePaused = new("run.phase.paused", "Paused");
        public static readonly LocString PhasePausedInContent = new("run.phase.pausedInContent", "Paused (in content)");
        public static readonly LocString FateProgress = new("run.fateProgress", "{0}%   ·   {1} left");
        public static readonly LocString UpNext = new("run.upNext", "Up next");
        public static readonly LocString NoOtherFates = new("run.noOtherFates", "No other eligible FATEs in this zone.");
        public static readonly LocPlural Rotation = new("run.rotation", "{1}  ·  {0} zone in rotation", "{1}  ·  {0} zones in rotation");
        public static readonly LocString SomewhereElse = new("run.somewhereElse", "Somewhere else");
        public static readonly LocString TileFates = new("run.tile.fates", "FATEs");
        public static readonly LocString TileGems = new("run.tile.gems", "Gems");
        public static readonly LocString TileFatesPerHour = new("run.tile.fatesPerHour", "FATEs/h");
        public static readonly LocString TileElapsed = new("run.tile.elapsed", "Elapsed");
        public static readonly LocString PerHour = new("run.perHour", "{0} /h");
        public static readonly LocString QueueMeta = new("run.queueMeta", "{0}%   ·   {1}   ·   {2}y");
        public static readonly LocString GoalOf = new("run.goal.of", "/ {0}");
        public static readonly LocString GoalOfMinutes = new("run.goal.ofMinutes", "/ {0}m");
        public static readonly LocString GoalMinutes = new("run.goal.minutes", "{0}m");
        public static readonly LocString GemsToGo = new("run.goal.gemsToGo", "{0} gems to go");
        public static readonly LocString TargetReached = new("run.goal.targetReached", "target reached");
        public static readonly LocString FatesLeft = new("run.goal.fatesLeft", "{0} FATEs left");
        public static readonly LocString HoursLeft = new("run.goal.hoursLeft", "{0}h {1:00}m left");
        public static readonly LocString MinutesLeft = new("run.goal.minutesLeft", "{0}m {1:00}s left");
        public static readonly LocString TimeReached = new("run.goal.timeReached", "time reached");
        public static readonly LocString Done = new("run.goal.done", "done");
        public static readonly LocString UntilYouStop = new("run.goal.untilYouStop", "until you stop");
    }

    internal static class Live
    {
        public static readonly LocString Title = new("live.title", "Live FATEs");
        public static readonly LocString Hide = new("live.hide", "Hide the live tracker");
        public static readonly LocString Engaging = new("live.engaging", "Engaging");
        public static readonly LocString StandingBy = new("live.standingBy", "Standing by");
        public static readonly LocString Ready = new("live.ready", "Ready");
        public static readonly LocString Paused = new("live.paused", "Paused");
        public static readonly LocString NoFate = new("live.noFate", "No FATE engaged.");
        public static readonly LocString NoOtherFates = new("live.noOtherFates", "No other FATEs.");
        public static readonly LocString NoSession = new("live.noSession", "No session.");
        public static readonly LocString Session = new("live.session", "{0} FATEs · {1} gems · {2}");
        public static readonly LocString Exp = new("live.exp", "{0} exp · {1}/h");
        public static readonly LocString Ban = new("live.ban", "Blacklist this FATE for this character (skips it while grinding).");
        public static readonly LocString QueueMeta = new("live.queueMeta", "{0}%  {1}");
    }

    internal static class History
    {
        public static readonly LocString Title = new("history.title", "History");
        public static readonly LocString Empty = new("history.empty", "Your finished runs will show up here.");
        public static readonly LocPlural Summary = new("history.summary", "{0} recorded run  ·  {1} grinding  ·  {2} FATEs/h average", "{0} recorded runs  ·  {1} grinding  ·  {2} FATEs/h average");
        public static readonly LocString TileRuns = new("history.tile.runs", "Runs");
        public static readonly LocString TileFates = new("history.tile.fates", "FATEs");
        public static readonly LocString TileGems = new("history.tile.gems", "Gems");
        public static readonly LocString TileExp = new("history.tile.exp", "Exp");
        public static readonly LocString TileLevels = new("history.tile.levels", "Levels");
        public static readonly LocString NoRuns = new("history.noRuns", "No runs recorded yet. Finish (or stop) a grind and it'll show up here.");
        public static readonly LocString FatesPerRun = new("history.fatesPerRun", "FATEs per run");
        public static readonly LocString RecentRuns = new("history.recentRuns", "Recent runs");
        public static readonly LocPlural ChartRange = new("history.chartRange", "last {0} run  ·  oldest to newest", "last {0} runs  ·  oldest to newest");
        public static readonly LocString ChartPeak = new("history.chartPeak", "peak {0}");
        public static readonly LocString ChartTooltip = new("history.chartTooltip", "{0}  ·  {1} FATEs  ·  {2} gems  ·  {3}");
        public static readonly LocString RowDetail = new("history.rowDetail", "{0}  ·  {1}  ·  {2}");
        public static readonly LocString RowDetailNoMode = new("history.rowDetailNoMode", "{0}  ·  {1}");
        public static readonly LocString TooltipMode = new("history.tooltip.mode", "Mode: {0}");
        public static readonly LocString TooltipLevel = new("history.tooltip.level", "Level: {0} → {1}");
        public static readonly LocString TooltipRate = new("history.tooltip.rate", "Rate: {0} FATEs/h  ·  {1} exp/h");
        public static readonly LocString TooltipZones = new("history.tooltip.zones", "Zones: {0}");
        public static readonly LocString JustNow = new("history.time.justNow", "just now");
        public static readonly LocString MinutesAgo = new("history.time.minutesAgo", "{0}m ago");
        public static readonly LocString HoursAgo = new("history.time.hoursAgo", "{0}h ago");
        public static readonly LocString DaysAgo = new("history.time.daysAgo", "{0}d ago");
        public static readonly LocString ClearHistory = new("history.clear", "Clear history");
        public static readonly LocString ClearQuestion = new("history.clearQuestion", "Delete all recorded runs?");
        public static readonly LocString ClearYes = new("history.clearYes", "Yes, clear");
    }

    internal static class Plugins
    {
        public static readonly LocString Title = new("plugins.title", "Plugins");
        public static readonly LocString AllInstalled = new("plugins.allInstalled", "All required plugins are installed and loaded.");
        public static readonly LocPlural Missing = new("plugins.missing", "{0} required plugin is missing.", "{0} required plugins are missing.");
        public static readonly LocString Required = new("plugins.required", "Required");
        public static readonly LocString Optional = new("plugins.optional", "Optional");
        public static readonly LocString Installed = new("plugins.installed", "Installed");
        public static readonly LocString Disabled = new("plugins.disabled", "Disabled");
        public static readonly LocString Install = new("plugins.install", "Install");
        public static readonly LocString Installing = new("plugins.installing", "Installing…");
        public static readonly LocString RepoHint = new("plugins.repoHint", "Repo: {0}\nLeft-click to open repo URL · right-click to copy");
        public static readonly LocString TextAdvanceDisabled = new("plugins.textAdvanceDisabled",
            "Loaded, but TextAdvance's own \"Enable plugin\" toggle is off.\nFATE turn-ins still work (AFG drives them directly), but gemstone\nauto-trade relies on this toggle to clear the trader's dialogue.\nTurn it on in TextAdvance's settings window (/xlplugins -> TextAdvance).");
        public static readonly LocString Footer = new("plugins.footer",
            "Install adds the plugin's source repository to Dalamud and queues an install. If one-click install fails (URL drift, network), right-click a plugin name to copy its repo URL and add it manually via /xlsettings -> Experimental -> Custom Plugin Repositories.");
        public static readonly LocString PurposeVnavmesh = new("plugins.purpose.vnavmesh", "Pathfinding and movement to FATEs.");
        public static readonly LocString PurposeBossMod = new("plugins.purpose.bossMod", "Auto-rotation, targeting, and dodging during FATE combat.");
        public static readonly LocString PurposeTextAdvance = new("plugins.purpose.textAdvance", "Talk-skip during Collect FATE turn-ins (scoped, only enabled mid-Collect).");
    }

    internal static class About
    {
        public static readonly LocString Connect = new("about.connect", "Connect");
        public static readonly LocString SupportTitle = new("about.support.title", "Made with care");
        public static readonly LocString SupportBody = new("about.support.body", "I build and maintain this in my spare time. If it has helped you, a Patreon membership lets me keep improving it. No pressure, and thank you for being here.");
        public static readonly LocString SupportButton = new("about.support.button", "Support on Patreon");
        public static readonly LocString PatreonHint = new("about.support.hint", "Open Patreon · right-click to copy");
        public static readonly LocString LinkHint = new("about.linkHint", "Click to open · right-click to copy");
        public static readonly LocString MadeBy = new("about.madeBy", "Made by {0}");
        public static readonly LocString Version = new("about.version", "v {0}");
        public static readonly LocString LinkGitHub = new("about.link.github", "GitHub");
        public static readonly LocString LinkDiscord = new("about.link.discord", "Discord");
        public static readonly LocString LinkDiscussions = new("about.link.discussions", "Discussions");
        public static readonly LocString LinkBug = new("about.link.bug", "Report a bug");
        public static readonly LocString LinkMore = new("about.link.more", "More plugins");
        public static readonly LocString LinkSecurity = new("about.link.security", "Security");
        public static readonly LocString ReminderTitle = new("about.reminder.title", "A little reminder");
        public static readonly LocString FactsTitle = new("about.facts.title", "Did you know?");
        public static readonly LocString QuotesTitle = new("about.quotes.title", "Words to live by");
        public static readonly LocString JokesTitle = new("about.jokes.title", "Just for fun");

        public static readonly LocString[] Reminders =
        [
            new("about.reminder.1", "Been at it a while? Roll your shoulders and take one slow breath."),
            new("about.reminder.2", "Hydration check. When did you last drink some water?"),
            new("about.reminder.3", "Blink a few times and let your eyes rest for a moment."),
            new("about.reminder.4", "Stand up, stretch, and shake out your hands. Future you says thanks."),
            new("about.reminder.5", "Sit up and settle in comfortably. Your back will thank you later."),
            new("about.reminder.6", "Remember to eat something today. You matter more than any score."),
            new("about.reminder.7", "Eyes feel tired? Look at something far away for twenty seconds."),
            new("about.reminder.8", "Whatever you're chasing, you're allowed to take a break whenever."),
            new("about.reminder.9", "You're doing great. Be a little kinder to yourself today."),
            new("about.reminder.10", "A glass of water and a quick stretch can reset a long session."),
            new("about.reminder.11", "Unclench your jaw and drop your shoulders. There you go."),
            new("about.reminder.12", "Rest is part of the journey too. Step away whenever you need to."),
        ];

        public static readonly LocString[] Facts =
        [
            new("about.facts.1", "Honey never spoils. Jars over 3,000 years old have been found still edible."),
            new("about.facts.2", "Octopuses have three hearts and blue blood."),
            new("about.facts.3", "A day on Venus is longer than a whole year on Venus."),
            new("about.facts.4", "Bananas are berries, but strawberries aren't."),
            new("about.facts.5", "There are more possible chess games than atoms in the observable universe."),
            new("about.facts.6", "Sharks have been around longer than trees have."),
            new("about.facts.7", "A group of flamingos is called a flamboyance."),
            new("about.facts.8", "Honeybees can recognize individual human faces."),
            new("about.facts.9", "Wombat droppings are cube shaped."),
            new("about.facts.10", "The Eiffel Tower can grow over 15 cm taller on a hot day."),
            new("about.facts.11", "Hot water can sometimes freeze faster than cold water."),
            new("about.facts.12", "A bolt of lightning is roughly five times hotter than the surface of the Sun."),
        ];

        public static readonly LocString[] Quotes =
        [
            new("about.quotes.1", "Done is better than perfect. You can always polish later."),
            new("about.quotes.2", "Small steps every day add up to surprising distances."),
            new("about.quotes.3", "Comparison is the thief of joy. Run your own race."),
            new("about.quotes.4", "Progress, not perfection."),
            new("about.quotes.5", "You don't have to be great to start, but you have to start to be great."),
            new("about.quotes.6", "Be patient with yourself. Growth takes time."),
            new("about.quotes.7", "The best time to begin was yesterday. The second best is right now."),
            new("about.quotes.8", "Celebrate the small wins. They count too."),
            new("about.quotes.9", "Slow progress is still progress."),
            new("about.quotes.10", "Your only real competition is who you were yesterday."),
        ];

        public static readonly LocString[] Jokes =
        [
            new("about.jokes.1", "Why don't scientists trust atoms? Because they make up everything."),
            new("about.jokes.2", "I would tell you a chemistry joke, but I know I wouldn't get a reaction."),
            new("about.jokes.3", "Why did the scarecrow win an award? He was outstanding in his field."),
            new("about.jokes.4", "I'm reading a book about anti-gravity. It's impossible to put down."),
            new("about.jokes.5", "Why don't skeletons fight each other? They don't have the guts."),
            new("about.jokes.6", "What do you call fake spaghetti? An impasta."),
            new("about.jokes.7", "Why did the bicycle fall over? It was two tired."),
            new("about.jokes.8", "What do you call cheese that isn't yours? Nacho cheese."),
            new("about.jokes.9", "I'm on a seafood diet. I see food, and I eat it."),
            new("about.jokes.10", "I only know 25 letters of the alphabet. I don't know y."),
        ];
    }

    internal static class Settings
    {
        public static readonly LocString Title = new("settings.title", "Settings");
        public static readonly LocString Language = new("settings.language", "Language");
        public static readonly LocString LanguageHelp = new("settings.languageHelp", "The language of this plugin's windows. FATE and zone names always follow the game client.");

        public static readonly LocString CatGeneral = new("settings.cat.general", "General");
        public static readonly LocString CatGeneralSub = new("settings.cat.generalSub", "Window and behavior preferences.");
        public static readonly LocString CatFilters = new("settings.cat.filters", "FATE filters");
        public static readonly LocString CatFiltersSub = new("settings.cat.filtersSub", "Keeps the plugin off dying or late FATEs.");
        public static readonly LocString CatClasses = new("settings.cat.classes", "Class queue");
        public static readonly LocString CatClassesSub = new("settings.cat.classesSub", "Switch gearsets on start, and advance to the next class when one hits its level cap.");
        public static readonly LocString CatGemstones = new("settings.cat.gemstones", "Gemstones");
        public static readonly LocString CatGemstonesSub = new("settings.cat.gemstonesSub", "Auto-spend Bicolor Gemstones once the wallet hits your threshold.");
        public static readonly LocString CatRepair = new("settings.cat.repair", "Repair");
        public static readonly LocString CatRepairSub = new("settings.cat.repairSub", "Auto-repair gear when equipped item condition drops below the threshold.");
        public static readonly LocString CatConsumables = new("settings.cat.consumables", "Consumables");
        public static readonly LocString CatConsumablesSub = new("settings.cat.consumablesSub", "Keep food and medicine buffs up while grinding: Well Fed is a free +3% EXP.");
        public static readonly LocString CatHumanizer = new("settings.cat.humanizer", "Humanizer");
        public static readonly LocString CatHumanizerSub = new("settings.cat.humanizerSub", "Take periodic city breaks between FATEs: teleport to a random hub and wander around for a few minutes before resuming.");
        public static readonly LocString CatPartyInvites = new("settings.cat.partyInvites", "Party invites");
        public static readonly LocString CatPartyInvitesSub = new("settings.cat.partyInvitesSub", "Auto-decline incoming party invites during a run, after a human-like delay, with an optional reply.");
        public static readonly LocString CatGmAlert = new("settings.cat.gmAlert", "GM alert");
        public static readonly LocString CatGmAlertSub = new("settings.cat.gmAlertSub", "Detects nearby Game Masters and reacts: stop the bot, ping you, or take more drastic action.");

        public static readonly LocString GeneralWindow = new("settings.general.window", "Window");
        public static readonly LocString OpenOnLogin = new("settings.general.openOnLogin", "Open on login");
        public static readonly LocString OpenOnLoginHelp = new("settings.general.openOnLoginHelp", "Pop the main window automatically the next time you log in.");
        public static readonly LocString LivePopout = new("settings.general.livePopout", "Live FATE tracker popout");
        public static readonly LocString LivePopoutHelp = new("settings.general.livePopoutHelp", "Show the live FATE tracker as a small overlay window so you can keep it visible while the main window is closed.");
        public static readonly LocString GeneralBehavior = new("settings.general.behavior", "Behavior");
        public static readonly LocString SwapZones = new("settings.general.swapZones", "Swap zones when empty");
        public static readonly LocString SwapZonesHelp = new("settings.general.swapZonesHelp", "When the current zone runs out of eligible FATEs, jump to the next zone in your priority order.");
        public static readonly LocString AutoPause = new("settings.general.autoPause", "Auto-pause in content");
        public static readonly LocString AutoPauseHelp = new("settings.general.autoPauseHelp", "Pause the run while you are inside a duty, trial, raid, or any other instanced content, then resume it once you are back outside. Your zones, goal, and session stats are kept, and paused time does not count toward a time-based goal.");
        public static readonly LocString AutoResume = new("settings.general.autoResume", "Auto-resume on fault");
        public static readonly LocString AutoResumeHelp = new("settings.general.autoResumeHelp", "If the grind hits an unrecoverable error and stops, automatically restart it (up to 3 times in 5 minutes) instead of ending the run. Leave off if you want faults to surface.");

        public static readonly LocString FiltersEligibility = new("settings.filters.eligibility", "Eligibility");
        public static readonly LocString MinTime = new("settings.filters.minTime", "Minimum time remaining");
        public static readonly LocString MinTimeHelp = new("settings.filters.minTimeHelp", "Skip FATEs that have less than this many seconds left. Keeps you off corpse-FATEs other players are finishing.");
        public static readonly LocString MinTimeFormat = new("settings.filters.minTimeFormat", "%d s");
        public static readonly LocString MaxProgress = new("settings.filters.maxProgress", "Maximum progress");
        public static readonly LocString MaxProgressHelp = new("settings.filters.maxProgressHelp", "Skip FATEs already past this percent. Keeps you off near-finished FATEs others are clearing.");
        public static readonly LocString FiltersTypes = new("settings.filters.types", "FATE types");
        public static readonly LocString SkipTypes = new("settings.filters.skipTypes", "Skip FATE types");
        public static readonly LocString SkipTypesHelp = new("settings.filters.skipTypesHelp", "Toggle on a type to skip every FATE of that kind. Useful if you don't enjoy escorts or collect hand-ins.");
        public static readonly LocString RuleNormal = new("settings.filters.rule.normal", "Slay enemies");
        public static readonly LocString RuleNormalHelp = new("settings.filters.rule.normalHelp", "Kill the target mobs in the FATE ring.");
        public static readonly LocString RuleCollect = new("settings.filters.rule.collect", "Collect / hand-in");
        public static readonly LocString RuleCollectHelp = new("settings.filters.rule.collectHelp", "Gather items off mobs or nodes and turn them in.");
        public static readonly LocString RuleEscort = new("settings.filters.rule.escort", "Escort");
        public static readonly LocString RuleEscortHelp = new("settings.filters.rule.escortHelp", "Protect an NPC that walks a fixed path.");
        public static readonly LocString RuleDefend = new("settings.filters.rule.defend", "Defend");
        public static readonly LocString RuleDefendHelp = new("settings.filters.rule.defendHelp", "Hold a point or NPC against waves.");
        public static readonly LocString RuleEvent = new("settings.filters.rule.event", "Talk to NPC");
        public static readonly LocString RuleEventHelp = new("settings.filters.rule.eventHelp", "Dialogue-style FATE that starts by interacting with an NPC.");
        public static readonly LocString RuleChase = new("settings.filters.rule.chase", "Chase");
        public static readonly LocString RuleChaseHelp = new("settings.filters.rule.chaseHelp", "Pursue a moving enemy across the zone.");
        public static readonly LocString RuleBoss = new("settings.filters.rule.boss", "Boss");
        public static readonly LocString RuleBossHelp = new("settings.filters.rule.bossHelp", "Single-boss encounter (notorious monster style).");
        public static readonly LocString RuleFete = new("settings.filters.rule.fete", "Fete");
        public static readonly LocString RuleFeteHelp = new("settings.filters.rule.feteHelp", "Special seasonal / community FATE.");
        public static readonly LocString CollectTextAdvanceNote = new("settings.filters.collectTextAdvanceNote", "TextAdvance is installed but disabled. Collect hand-ins usually work anyway (AFG drives the turn-in directly), but enabling TextAdvance's \"Enable plugin\" toggle is the safe fallback if a hand-in stalls.");
        public static readonly LocString FiltersPriority = new("settings.filters.priority", "Priority");
        public static readonly LocString FatePriority = new("settings.filters.fatePriority", "FATE priority");
        public static readonly LocString FatePriorityHelp = new("settings.filters.fatePriorityHelp", "Order the rules used to pick the next FATE. Top rule wins; ties fall through to the next. Reset restores the recommended order.");
        public static readonly LocString SortBonusTwist = new("settings.filters.sort.bonusTwist", "Bonus FATE (skip while Twist active)");
        public static readonly LocString SortProgress = new("settings.filters.sort.progress", "Progress %");
        public static readonly LocString SortBonus = new("settings.filters.sort.bonus", "Bonus FATE");
        public static readonly LocString SortUrgent = new("settings.filters.sort.urgent", "About to expire");
        public static readonly LocString SortDistance = new("settings.filters.sort.distance", "Closest to me");
        public static readonly LocString SortTimeRemaining = new("settings.filters.sort.timeRemaining", "Time remaining");
        public static readonly LocString SortLevel = new("settings.filters.sort.level", "Level");
        public static readonly LocString SortName = new("settings.filters.sort.name", "Name");
        public static readonly LocString ResetRecommended = new("settings.filters.resetRecommended", "Reset to recommended");
        public static readonly LocString FiltersBlacklist = new("settings.filters.blacklist", "Blacklist");
        public static readonly LocString AddByName = new("settings.filters.addByName", "Add a FATE by name");
        public static readonly LocString AddByNameHelp = new("settings.filters.addByNameHelp", "Pulled live from game data, sorted A-Z. Type to search. Use this for FATEs you never want to trigger, like a world boss everyone is waiting on, without needing it to be up.");
        public static readonly LocString Blacklisted = new("settings.filters.blacklisted", "Blacklisted FATEs");
        public static readonly LocString BlacklistedHelp = new("settings.filters.blacklistedHelp", "Blacklisted FATEs are skipped while grinding. The ban button in the Live FATEs window adds to this list too. Remove one here to grind it again.");
        public static readonly LocString NoFatesInData = new("settings.filters.noFatesInData", "No FATEs found in game data.");
        public static readonly LocString NoBlacklisted = new("settings.filters.noBlacklisted", "No FATEs blacklisted.");
        public static readonly LocString RemoveFromBlacklist = new("settings.filters.removeFromBlacklist", "Remove from blacklist.");
        public static readonly LocString SearchHint = new("settings.searchHint", "Search...");

        public static readonly LocString ClassesSwitching = new("settings.classes.switching", "Class switching");
        public static readonly LocString SwitchOnStart = new("settings.classes.switchOnStart", "Switch class when run starts");
        public static readonly LocString SwitchOnStartHelp = new("settings.classes.switchOnStartHelp", "Equip the first eligible gearset below when you press Start. Disable to leave the run on whatever class you're currently on.");
        public static readonly LocString SwitchingOff = new("settings.classes.switchingOff", "Class switching is off. Enable it to configure the queue.");
        public static readonly LocString ClassesDone = new("settings.classes.done", "When the queue is done");
        public static readonly LocString AllCapped = new("settings.classes.allCapped", "All classes capped");
        public static readonly LocString AllCappedHelp = new("settings.classes.allCappedHelp", "What to do after every queued class has hit its level cap.");
        public static readonly LocString DoneKeepName = new("settings.classes.doneKeep.name", "Keep grinding on the last class");
        public static readonly LocString DoneKeepDetail = new("settings.classes.doneKeep.detail", "When every queued class is capped, keep going on the last one.");
        public static readonly LocString DoneStopName = new("settings.classes.doneStop.name", "Stop the run");
        public static readonly LocString DoneStopDetail = new("settings.classes.doneStop.detail", "When every queued class is capped, end the run.");
        public static readonly LocString ClassesQueue = new("settings.classes.queue", "Queue");
        public static readonly LocString AddGearset = new("settings.classes.addGearset", "Add a gearset");
        public static readonly LocString AddGearsetHelp = new("settings.classes.addGearsetHelp", "Use the gear-set number shown in your in-game Gear Set list (1-100). Class is resolved automatically.");
        public static readonly LocString QueueOrder = new("settings.classes.queueOrder", "Queue order");
        public static readonly LocString QueueOrderHelp = new("settings.classes.queueOrderHelp", "Order matters: top entry runs first, then advances when its level cap is hit.");
        public static readonly LocString NoGearsets = new("settings.classes.noGearsets", "No gearsets found. Save one in-game (Character -> Gear Set List) first.");
        public static readonly LocString Queued = new("settings.classes.queued", "(queued)");
        public static readonly LocString AlreadyQueued = new("settings.classes.alreadyQueued", "Already in the queue.");
        public static readonly LocString NoClassesQueued = new("settings.classes.noClassesQueued", "No classes queued. Automation will use whatever class you're on.");
        public static readonly LocString QueueEntry = new("settings.classes.queueEntry", "{0} - gearset {1}");
        public static readonly LocString EntryLevel = new("settings.classes.entryLevel", "(lvl {0})");
        public static readonly LocString NoCap = new("settings.classes.noCap", "no cap");
        public static readonly LocString StopAtLevel = new("settings.classes.stopAtLevel", "Stop at %d Level");

        public static readonly LocString GemsTrigger = new("settings.gems.trigger", "Trade trigger");
        public static readonly LocString AutoTrade = new("settings.gems.autoTrade", "Auto-trade at threshold");
        public static readonly LocString AutoTradeHelp = new("settings.gems.autoTradeHelp", "When your Bicolor Gemstone inventory reaches the threshold below, the plugin teleports to a trader and buys the item.");
        public static readonly LocString AutoTradeOff = new("settings.gems.autoTradeOff", "Auto-trade is off. Enable it to configure the trade.");
        public static readonly LocString TradeTextAdvanceNote = new("settings.gems.textAdvanceNote", "TextAdvance is installed but disabled. Auto-trade may stall at the trader's dialogue; turn on TextAdvance's \"Enable plugin\" toggle for reliable trading.");
        public static readonly LocString Threshold = new("settings.gems.threshold", "Trade threshold");
        public static readonly LocString ThresholdHelp = new("settings.gems.thresholdHelp", "Gem count that triggers the trade. Game cap is 1500. Lower values trade more often so fewer FATEs are wasted near cap.");
        public static readonly LocString GemsFormat = new("settings.gems.gemsFormat", "%d gems");
        public static readonly LocString GemsItem = new("settings.gems.item", "What to buy");
        public static readonly LocString ItemToBuy = new("settings.gems.itemToBuy", "Item to buy");
        public static readonly LocString ItemToBuyHelp = new("settings.gems.itemToBuyHelp", "Pulled live from game data, sorted A-Z. Type to search. Cost shown in gems per one.");
        public static readonly LocString NoShopItems = new("settings.gems.noShopItems", "No gem-shop items found.");
        public static readonly LocString GemsSpend = new("settings.gems.spend", "How much to spend");
        public static readonly LocString SpendStrategy = new("settings.gems.spendStrategy", "Spend strategy");
        public static readonly LocString SpendStrategyHelp = new("settings.gems.spendStrategyHelp", "How much each trade spends when it fires.");
        public static readonly LocString SpendAllName = new("settings.gems.spendAll.name", "Spend all gemstones");
        public static readonly LocString SpendAllDetail = new("settings.gems.spendAll.detail", "Spend everything above the reserve on each trade.");
        public static readonly LocString SpendUpToName = new("settings.gems.spendUpTo.name", "Spend up to a set amount");
        public static readonly LocString SpendUpToDetail = new("settings.gems.spendUpTo.detail", "Cap how many gems each trade is allowed to spend.");
        public static readonly LocString BuyFixedName = new("settings.gems.buyFixed.name", "Buy a fixed number");
        public static readonly LocString BuyFixedDetail = new("settings.gems.buyFixed.detail", "Buy a set quantity of the item on each trade.");
        public static readonly LocString SpendUpTo = new("settings.gems.spendUpToRow", "Spend up to");
        public static readonly LocString SpendUpToHelp = new("settings.gems.spendUpToRowHelp", "Maximum gems spent per trade.");
        public static readonly LocString BuyQuantity = new("settings.gems.buyQuantity", "Buy quantity");
        public static readonly LocString BuyQuantityHelp = new("settings.gems.buyQuantityHelp", "How many of the item to buy per trade.");
        public static readonly LocString BuyQuantityFormat = new("settings.gems.buyQuantityFormat", "%d x item");
        public static readonly LocString Reserve = new("settings.gems.reserve", "Keep in reserve");
        public static readonly LocString ReserveHelp = new("settings.gems.reserveHelp", "Gems left untouched on every trade. Use this when you want to save toward a pricier item without turning auto-trade off.");
        public static readonly LocString GemsAfter = new("settings.gems.after", "After the trade");
        public static readonly LocString WhenDone = new("settings.gems.whenDone", "When done");
        public static readonly LocString WhenDoneHelp = new("settings.gems.whenDoneHelp", "What to do once the buy succeeds.");
        public static readonly LocString AfterResumeName = new("settings.gems.afterResume.name", "Resume the grind");
        public static readonly LocString AfterResumeDetail = new("settings.gems.afterResume.detail", "Keep grinding FATEs in the same zone after the buy.");
        public static readonly LocString AfterStopName = new("settings.gems.afterStop.name", "Stop the run");
        public static readonly LocString AfterStopDetail = new("settings.gems.afterStop.detail", "End the run once the buy succeeds.");
        public static readonly LocString PreviewCannotAfford = new("settings.gems.previewCannotAfford", "Threshold/reserve won't afford any {0} at {1}g each.");
        public static readonly LocString PreviewBuy = new("settings.gems.previewBuy", "At threshold {0}g (keeping {1}g), next trade buys ~{2} x {3} for {4}g.");
        public static readonly LocString ItemCostLabel = new("settings.gems.itemCostLabel", "{0}  ({1}g)");

        public static readonly LocString RepairTrigger = new("settings.repair.trigger", "Repair trigger");
        public static readonly LocString AutoRepair = new("settings.repair.autoRepair", "Auto-repair gear");
        public static readonly LocString AutoRepairHelp = new("settings.repair.autoRepairHelp", "Between FATEs, when the lowest equipped item drops to or below the threshold, the plugin runs a repair. At 0% the gear stops working, so keep some margin.");
        public static readonly LocString AutoRepairOff = new("settings.repair.autoRepairOff", "Auto-repair is off. Enable it to configure repair.");
        public static readonly LocString RepairThreshold = new("settings.repair.threshold", "Repair threshold");
        public static readonly LocString RepairThresholdHelp = new("settings.repair.thresholdHelp", "Trips when the worst equipped slot reaches this condition percentage. 20% leaves comfortable margin before the 0% breakdown.");
        public static readonly LocString RepairSource = new("settings.repair.source", "Repair source");
        public static readonly LocString RepairSourceHelp = new("settings.repair.sourceHelp", "How the repair is performed. Self-repair uses Dark Matter from your bag (no travel). NPC repair travels to your Grand Company mender.");
        public static readonly LocString RepairSelfThenNpcName = new("settings.repair.selfThenNpc.name", "Self, then NPC");
        public static readonly LocString RepairSelfThenNpcDetail = new("settings.repair.selfThenNpc.detail", "Use Dark Matter from your bag first; fall back to the Grand Company mender when you run out.");
        public static readonly LocString RepairSelfOnlyName = new("settings.repair.selfOnly.name", "Self only");
        public static readonly LocString RepairSelfOnlyDetail = new("settings.repair.selfOnly.detail", "Repair with Dark Matter from your bag. No travel.");
        public static readonly LocString RepairNpcOnlyName = new("settings.repair.npcOnly.name", "NPC only");
        public static readonly LocString RepairNpcOnlyDetail = new("settings.repair.npcOnly.detail", "Travel to your Grand Company mender (or a custom NPC) and pay in seals.");
        public static readonly LocString CustomNpc = new("settings.repair.customNpc", "Custom repair NPC");
        public static readonly LocString CustomNpcHelp = new("settings.repair.customNpcHelp", "Optional. Travel to any repair NPC instead of the Grand Company mender. Target the NPC in-game, then click \"Set from target\". Clear to fall back to the GC mender.");
        public static readonly LocString NpcNote = new("settings.repair.npcNote", "NPC repair uses your custom repair NPC if set, otherwise your Grand Company mender (teleports there and pays in company seals). A custom NPC removes the Grand Company requirement.");
        public static readonly LocString NpcSet = new("settings.repair.npcSet", "{0}  (territory {1})");
        public static readonly LocString NpcNone = new("settings.repair.npcNone", "None - using Grand Company mender.");
        public static readonly LocString SetFromTarget = new("settings.repair.setFromTarget", "Set from target");
        public static readonly LocString NoTargetChat = new("settings.repair.noTargetChat", "[AFG] No target - target a repair NPC first, then click again.");
        public static readonly LocString NpcSetChat = new("settings.repair.npcSetChat", "[AFG] Custom repair NPC set: {0} (territory {1}).");

        public static readonly LocString ConsumablesGroup = new("settings.consumables.group", "Consumables");
        public static readonly LocString AutoConsume = new("settings.consumables.autoConsume", "Auto-consume food & medicine");
        public static readonly LocString AutoConsumeHelp = new("settings.consumables.autoConsumeHelp", "Use food and medicine between FATEs to keep their buffs up; Well Fed alone is a free +3% EXP. Items are consumed only when out of combat, and refreshed before the buff runs out.");
        public static readonly LocString AutoConsumeOff = new("settings.consumables.autoConsumeOff", "Auto-consume is off. Enable it to pick items.");
        public static readonly LocString RefreshUnder = new("settings.consumables.refreshUnder", "Refresh when under");
        public static readonly LocString RefreshUnderHelp = new("settings.consumables.refreshUnderHelp", "Re-consume once the buff has fewer than this many minutes left. 0 only re-applies after it fully wears off. Food and medicine last 30 minutes.");
        public static readonly LocString RefreshWornOff = new("settings.consumables.refreshWornOff", "only when worn off");
        public static readonly LocString RefreshFormat = new("settings.consumables.refreshFormat", "%d min left");
        public static readonly LocString ConsumablesItems = new("settings.consumables.items", "Items");
        public static readonly LocString AddItem = new("settings.consumables.addItem", "Add an item");
        public static readonly LocString AddItemHelp = new("settings.consumables.addItemHelp", "Pick from the food and medicine in your bag. HQ is used automatically when you have it.");
        public static readonly LocString ActiveItems = new("settings.consumables.activeItems", "Active items");
        public static readonly LocString ActiveItemsHelp = new("settings.consumables.activeItemsHelp", "Each is kept active in order; the next available one is consumed if the first runs out.");
        public static readonly LocString NoneInBag = new("settings.consumables.noneInBag", "No food or medicine in your bag. Stock some, then add it here.");
        public static readonly LocString KindFood = new("settings.consumables.kindFood", "Food");
        public static readonly LocString KindMedicine = new("settings.consumables.kindMedicine", "Medicine");
        public static readonly LocString Added = new("settings.consumables.added", "(added)");
        public static readonly LocString ItemLabel = new("settings.consumables.itemLabel", "{0}  [{1}]{2}");
        public static readonly LocString AlreadyAdded = new("settings.consumables.alreadyAdded", "Already added.");
        public static readonly LocString NoItemsAdded = new("settings.consumables.noItemsAdded", "No items added - nothing will be consumed.");
        public static readonly LocString WellFed = new("settings.consumables.wellFed", "Well Fed");
        public static readonly LocString Medicated = new("settings.consumables.medicated", "Medicated");
        public static readonly LocString NoneInBagShort = new("settings.consumables.noneInBagShort", "{0}, none in bag");

        public static readonly LocString HumanizerBreaks = new("settings.humanizer.breaks", "Breaks");
        public static readonly LocString HumanizerEnable = new("settings.humanizer.enable", "Take periodic city breaks");
        public static readonly LocString HumanizerEnableHelp = new("settings.humanizer.enableHelp", "Every N FATEs, teleport to a random selected city and wander around for a few minutes before resuming. Helps you avoid player reports by acting a little more human, useful when you leave the PC running for long sessions and don't want others noticing you grinding FATEs non-stop.");
        public static readonly LocString HumanizerOff = new("settings.humanizer.off", "Humanizer is off. Enable it to configure breaks.");
        public static readonly LocString FatesBetween = new("settings.humanizer.fatesBetween", "FATEs between breaks");
        public static readonly LocString FatesBetweenHelp = new("settings.humanizer.fatesBetweenHelp", "Take a break after this many completed FATEs. The counter resets after each break.");
        public static readonly LocString FatesFormat = new("settings.humanizer.fatesFormat", "%d FATEs");
        public static readonly LocString BreakLength = new("settings.humanizer.breakLength", "Break length");
        public static readonly LocString BreakLengthHelp = new("settings.humanizer.breakLengthHelp", "A random duration between these two values is rolled for each break.");
        public static readonly LocString MinutesFormat = new("settings.humanizer.minutesFormat", "%d min");
        public static readonly LocString HumanizerWandering = new("settings.humanizer.wandering", "Wandering");
        public static readonly LocString PauseBetween = new("settings.humanizer.pauseBetween", "Pause between walks");
        public static readonly LocString PauseBetweenHelp = new("settings.humanizer.pauseBetweenHelp", "After arriving at each random point, stand still for a random duration in this range before walking somewhere else.");
        public static readonly LocString SecondsFormat = new("settings.humanizer.secondsFormat", "%d s");
        public static readonly LocString WalkDistance = new("settings.humanizer.walkDistance", "Walk distance");
        public static readonly LocString WalkDistanceHelp = new("settings.humanizer.walkDistanceHelp", "Each random destination is rolled this many meters away from your current position. Larger ranges cover more of the city; smaller ranges keep you near the aetheryte.");
        public static readonly LocString MetersFormat = new("settings.humanizer.metersFormat", "%d m");
        public static readonly LocString HumanizerCities = new("settings.humanizer.cities", "Cities");
        public static readonly LocString AllowedCities = new("settings.humanizer.allowedCities", "Allowed cities");
        public static readonly LocString AllowedCitiesHelp = new("settings.humanizer.allowedCitiesHelp", "Tick the cities the plugin is allowed to teleport to. One is picked at random each break. Untick cities you haven't unlocked or don't want visited.");
        public static readonly LocString NoCities = new("settings.humanizer.noCities", "No cities selected - Humanizer will skip the break and keep grinding.");

        public static readonly LocString InvitesDecline = new("settings.invites.decline", "Decline");
        public static readonly LocString AutoDecline = new("settings.invites.autoDecline", "Auto-decline party invites");
        public static readonly LocString AutoDeclineHelp = new("settings.invites.autoDeclineHelp", "While a grind run is active, automatically decline incoming party invites after a short random delay. Invites that arrive while idle or playing manually are left alone for you to handle.");
        public static readonly LocString AutoDeclineOff = new("settings.invites.autoDeclineOff", "Auto-decline is off. Enable it to configure it.");
        public static readonly LocString DeclineDelay = new("settings.invites.delay", "Decline delay");
        public static readonly LocString DeclineDelayHelp = new("settings.invites.delayHelp", "Wait a random time in this range before declining, so it looks like you noticed the popup and dismissed it yourself.");
        public static readonly LocString InvitesReply = new("settings.invites.reply", "Reply");
        public static readonly LocString SendReply = new("settings.invites.sendReply", "Send a reply");
        public static readonly LocString SendReplyHelp = new("settings.invites.sendReplyHelp", "After declining, send a chat message so it reads like a polite human brush-off rather than an instant silent decline.");
        public static readonly LocString ReplyChannel = new("settings.invites.channel", "Reply channel");
        public static readonly LocString ReplyChannelHelp = new("settings.invites.channelHelp", "Where the message goes. \"Tell inviter\" whispers the person who invited you. Ignored when your message starts with a slash command.");
        public static readonly LocString ChannelTellName = new("settings.invites.channelTell.name", "Tell inviter");
        public static readonly LocString ChannelTellDetail = new("settings.invites.channelTell.detail", "Whisper the person who invited you.");
        public static readonly LocString ChannelSayName = new("settings.invites.channelSay.name", "Say");
        public static readonly LocString ChannelSayDetail = new("settings.invites.channelSay.detail", "Local /say, heard by players near you.");
        public static readonly LocString ChannelYellName = new("settings.invites.channelYell.name", "Yell");
        public static readonly LocString ChannelYellDetail = new("settings.invites.channelYell.detail", "Zone-wide /yell.");
        public static readonly LocString ReplyMessage = new("settings.invites.message", "Reply message");
        public static readonly LocString ReplyMessageHelp = new("settings.invites.messageHelp", "Use {name} for the inviter's character name and {world} for their home world. If the message begins with \"/\", it's sent verbatim as a command (e.g. /tell {name}@{world} busy right now!).");
        public static readonly LocString ReplyMessageHint = new("settings.invites.messageHint", "Sorry {name}, I'm busy right now!");

        public static readonly LocString GmAlerts = new("settings.gm.alerts", "Alerts");
        public static readonly LocString GmStopRun = new("settings.gm.stopRun", "Stop the run");
        public static readonly LocString GmStopRunHelp = new("settings.gm.stopRunHelp", "Halt automation immediately when a GM appears in your zone. Strongly recommended; the rest of the alerts are useless if the bot keeps grinding.");
        public static readonly LocString GmToast = new("settings.gm.toast", "Toast notification");
        public static readonly LocString GmToastHelp = new("settings.gm.toastHelp", "Pop a Dalamud toast: \"GM <name> is nearby!\"");
        public static readonly LocString GmChat = new("settings.gm.chat", "Chat alert");
        public static readonly LocString GmChatHelp = new("settings.gm.chatHelp", "Print a red chat warning into your local log.");
        public static readonly LocString GmSound = new("settings.gm.sound", "Sound beeps");
        public static readonly LocString GmSoundHelp = new("settings.gm.soundHelp", "Plays a series of system beeps through your speakers. Loud enough to grab your attention if you're tabbed away.");
        public static readonly LocString BeepCount = new("settings.gm.beepCount", "Beep count");
        public static readonly LocString BeepCountHelp = new("settings.gm.beepCountHelp", "How many beeps to play in the burst.");
        public static readonly LocString BeepCountFormat = new("settings.gm.beepCountFormat", "%d beeps");
        public static readonly LocString BeepLength = new("settings.gm.beepLength", "Beep length");
        public static readonly LocString BeepLengthHelp = new("settings.gm.beepLengthHelp", "How long each beep lasts.");
        public static readonly LocString BeepLengthFormat = new("settings.gm.beepLengthFormat", "%d ms each");
        public static readonly LocString BeepPitch = new("settings.gm.beepPitch", "Beep pitch");
        public static readonly LocString BeepPitchHelp = new("settings.gm.beepPitchHelp", "Tone frequency of each beep.");
        public static readonly LocString BeepPitchFormat = new("settings.gm.beepPitchFormat", "%d Hz");
        public static readonly LocString GmActions = new("settings.gm.actions", "Actions");
        public static readonly LocString GmCommands = new("settings.gm.commands", "Custom commands");
        public static readonly LocString GmCommandsHelp = new("settings.gm.commandsHelp", "Chat commands to run when a GM is spotted. Useful for things like /logout, /sh stay calm, or a macro.");
        public static readonly LocString GmKill = new("settings.gm.kill", "Kill the game");
        public static readonly LocString GmKillHelp = new("settings.gm.killHelp", "Hard-terminate the game process via /xlkill. The last-resort option; no goodbyes, no cutscene, no logout. You'll get a disconnect.");
        public static readonly LocString NoCommands = new("settings.gm.noCommands", "No commands queued.");
    }

    internal static class Plugin
    {
        public static readonly LocString CommandHelp = new("plugin.commandHelp", "Toggle the Auto FATE Grind window. /afg config | stats | deps | about | pause (pause or resume the run) | target (dump current target's BaseId).");
        public static readonly LocString CommandHelpAlias = new("plugin.commandHelpAlias", "Alias for /afg.");
    }
}
