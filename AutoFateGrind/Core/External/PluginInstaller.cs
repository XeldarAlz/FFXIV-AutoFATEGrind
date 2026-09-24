using ECommons.Reflection;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.External;

public static class PluginInstaller
{
    private static readonly HashSet<ExternalPlugin> InFlight = [];

    public static bool IsInstalling(ExternalPlugin plugin) => InFlight.Contains(plugin);

    public static async Task<bool> Install(ExternalPlugin plugin)
    {
        if (!InFlight.Add(plugin)) return false;
        try
        {
            var info = ExternalPlugins.Catalog[plugin];
            RunLog.Info($"Installing {info.DisplayName} from {info.RepoUrl}");
            var ok = await DalamudReflector.AddPlugin(info.RepoUrl, info.InternalName);
            RunLog.Info(ok
                ? $"{info.DisplayName} installed."
                : $"{info.DisplayName} install reported failure; the repo may need to be added manually.");
            return ok;
        }
        catch (Exception ex)
        {
            RunLog.Warning(ex, $"{ExternalPlugins.Catalog[plugin].DisplayName} install threw");
            return false;
        }
        finally
        {
            InFlight.Remove(plugin);
        }
    }
}
