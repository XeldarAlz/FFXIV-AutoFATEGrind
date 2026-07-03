using AutoFateGrind.Core.Ipc;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.External;

public enum ExternalPlugin
{
    Vnavmesh,
    BossMod,
    TextAdvance,
}

public sealed record ExternalPluginInfo(
    string InternalName,
    string DisplayName,
    string RepoUrl,
    string Purpose,
    bool Required,
    // Alternate InternalNames (community forks with the same IPC surface).
    string[]? Aliases = null);

public static class ExternalPlugins
{
    public static readonly IReadOnlyDictionary<ExternalPlugin, ExternalPluginInfo> Catalog
        = new Dictionary<ExternalPlugin, ExternalPluginInfo>
    {
        [ExternalPlugin.Vnavmesh] = new(
            InternalName: "vnavmesh",
            DisplayName: "vnavmesh",
            RepoUrl: "https://puni.sh/api/repository/veyn",
            Purpose: "Pathfinding and movement to FATEs.",
            Required: true),
        [ExternalPlugin.BossMod] = new(
            InternalName: "BossMod",
            DisplayName: "BossMod",
            RepoUrl: "https://puni.sh/api/repository/veyn",
            Purpose: "Auto-rotation, targeting, and dodging during FATE combat.",
            Required: true,
            Aliases: ["BossModReborn"]),
        [ExternalPlugin.TextAdvance] = new(
            InternalName: "TextAdvance",
            DisplayName: "TextAdvance",
            RepoUrl: "https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json",
            Purpose: "Talk-skip during Collect FATE turn-ins (scoped, only enabled mid-Collect).",
            Required: true),
    };

    public static IEnumerable<ExternalPlugin> All => Catalog.Keys;

    // The installed-plugin scan runs on many UI paths every frame; cache it and refresh on a short
    // throttle so an idle frame does not re-enumerate the whole installed-plugin list per lookup.
    private const int ScanThrottleMs = 1000;
    private static readonly Dictionary<ExternalPlugin, bool> installedCache = new();
    private static long lastScanMs;

    public static bool IsInstalled(ExternalPlugin plugin)
    {
        RefreshInstalledCache();
        return installedCache.TryGetValue(plugin, out var installed) && installed;
    }

    public static bool AllRequiredInstalled()
    {
        RefreshInstalledCache();
        foreach (var plugin in Catalog.Keys)
        {
            if (!Catalog[plugin].Required) continue;
            if (!installedCache.TryGetValue(plugin, out var installed) || !installed) return false;
        }
        return true;
    }

    private static void RefreshInstalledCache()
    {
        var now = Environment.TickCount64;
        if (installedCache.Count > 0 && now - lastScanMs < ScanThrottleMs) return;
        lastScanMs = now;
        foreach (var plugin in Catalog.Keys)
            installedCache[plugin] = ScanInstalled(Catalog[plugin]);
    }

    private static bool ScanInstalled(ExternalPluginInfo info)
    {
        foreach (var installed in Svc.PluginInterface.InstalledPlugins)
        {
            if (!installed.IsLoaded) continue;
            if (installed.InternalName == info.InternalName) return true;
            if (info.Aliases is not null && Array.IndexOf(info.Aliases, installed.InternalName) >= 0) return true;
        }
        return false;
    }

    // Loaded in Dalamud but its own in-plugin toggle is off. Advisory only — AFG drives
    // TextAdvance via external control, so this never gates the grind, it only warns.
    public static bool IsInstalledButDisabled(ExternalPlugin plugin)
        => plugin == ExternalPlugin.TextAdvance
           && IsInstalled(plugin)
           && !TextAdvanceIPC.IsPluginEnabled();
}
