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

    public static bool IsInstalled(ExternalPlugin plugin)
    {
        var info = Catalog[plugin];
        return Svc.PluginInterface.InstalledPlugins.Any(p =>
            p.IsLoaded
            && (p.InternalName == info.InternalName
                || (info.Aliases is not null && Array.IndexOf(info.Aliases, p.InternalName) >= 0)));
    }

    public static bool AllRequiredInstalled()
        => All.Where(p => Catalog[p].Required).All(IsInstalled);

    // Loaded in Dalamud but its own in-plugin toggle is off. Advisory only — AFG drives
    // TextAdvance via external control, so this never gates the grind, it only warns.
    public static bool IsInstalledButDisabled(ExternalPlugin plugin)
        => plugin == ExternalPlugin.TextAdvance
           && IsInstalled(plugin)
           && !TextAdvanceIPC.IsPluginEnabled();
}
