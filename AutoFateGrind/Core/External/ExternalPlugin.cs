using ECommons.DalamudServices;

namespace AutoFateGrind.Core.External;

public enum ExternalPlugin
{
    Vnavmesh,
    BossMod,
    Lifestream,
    TextAdvance,
}

public sealed record ExternalPluginInfo(
    string InternalName,
    string DisplayName,
    string RepoUrl,
    string Purpose,
    bool Required);

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
            DisplayName: "BossMod / BossMod Reborn",
            RepoUrl: "https://puni.sh/api/repository/veyn",
            Purpose: "Auto-rotation, targeting, and dodging during FATE combat.",
            Required: true),
        [ExternalPlugin.Lifestream] = new(
            InternalName: "Lifestream",
            DisplayName: "Lifestream",
            RepoUrl: "https://puni.sh/api/plugins",
            Purpose: "Aethernet hops between FATEs in the same zone.",
            Required: false),
        [ExternalPlugin.TextAdvance] = new(
            InternalName: "TextAdvance",
            DisplayName: "TextAdvance",
            RepoUrl: "https://puni.sh/api/plugins",
            Purpose: "Talk-skip during Collect FATE turn-ins (scoped, only enabled mid-Collect).",
            Required: false),
    };

    public static IEnumerable<ExternalPlugin> All => Catalog.Keys;

    public static bool IsInstalled(ExternalPlugin plugin)
    {
        var info = Catalog[plugin];
        return Svc.PluginInterface.InstalledPlugins
            .Any(p => p.InternalName == info.InternalName && p.IsLoaded);
    }

    public static bool AllRequiredInstalled()
        => All.Where(p => Catalog[p].Required).All(IsInstalled);
}
