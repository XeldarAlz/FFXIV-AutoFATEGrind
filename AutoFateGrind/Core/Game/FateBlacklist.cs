using clib.Utils;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;

namespace AutoFateGrind.Core.Game;

internal static class FateBlacklist
{
    public static bool Contains(Configuration cfg, PublicEvent f)
    {
        if (cfg.BlacklistedFateIds.Contains(f.Id)) return true;
        if (cfg.BlacklistedTypeIds.TryGetValue((int)f.FateType, out var set) && set.Contains(f.Id)) return true;
        return false;
    }

    public static void ToggleId(Configuration cfg, PublicEvent f)
    {
        if (!cfg.BlacklistedTypeIds.TryGetValue((int)f.FateType, out var set))
            cfg.BlacklistedTypeIds[(int)f.FateType] = set = [];
        if (!set.Add(f.Id))
            set.Remove(f.Id);
        cfg.SaveDebounced();
    }
}
