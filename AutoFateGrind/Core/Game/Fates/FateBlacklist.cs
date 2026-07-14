using clib.Utils;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;

namespace AutoFateGrind.Core.Game.Fates;

internal readonly record struct BlacklistedFate(FateType Type, uint Id);

internal static class FateBlacklist
{
    private static readonly Dictionary<BlacklistedFate, string> nameCache = new();

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

    public static IReadOnlyList<BlacklistedFate> All(Configuration cfg)
    {
        var seen = new HashSet<BlacklistedFate>();
        var entries = new List<BlacklistedFate>();

        // The flat id set predates the per-type sets and stores overworld FATE ids, so it reads as Normal.
        foreach (var fateId in cfg.BlacklistedFateIds)
        {
            var entry = new BlacklistedFate(FateType.Normal, fateId);
            if (seen.Add(entry))
                entries.Add(entry);
        }

        foreach (var (typeKey, set) in cfg.BlacklistedTypeIds)
        {
            foreach (var fateId in set)
            {
                var entry = new BlacklistedFate((FateType)typeKey, fateId);
                if (seen.Add(entry))
                    entries.Add(entry);
            }
        }

        entries.Sort(static (left, right) =>
            string.Compare(DisplayName(left), DisplayName(right), StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    public static void Remove(Configuration cfg, BlacklistedFate entry)
    {
        var changed = entry.Type == FateType.Normal && cfg.BlacklistedFateIds.Remove(entry.Id);

        var typeKey = (int)entry.Type;
        if (cfg.BlacklistedTypeIds.TryGetValue(typeKey, out var set) && set.Remove(entry.Id))
        {
            changed = true;
            if (set.Count == 0)
                cfg.BlacklistedTypeIds.Remove(typeKey);
        }

        if (changed)
            cfg.SaveDebounced();
    }

    public static string DisplayName(BlacklistedFate entry)
    {
        if (nameCache.TryGetValue(entry, out var cached))
            return cached;

        var name = ResolveName(entry);
        var resolved = string.IsNullOrWhiteSpace(name) ? FallbackName(entry) : name;
        nameCache[entry] = resolved;
        return resolved;
    }

    private static string? ResolveName(BlacklistedFate entry) => entry.Type switch
    {
        FateType.Normal => Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>()
            ?.GetRowOrDefault(entry.Id)?.Name.ExtractText(),
        FateType.DynamicEvent => Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.DynamicEvent>()
            ?.GetRowOrDefault(entry.Id)?.Name.ExtractText(),
        FateType.MechaEvent => Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.WKSMechaEventData>()
            ?.GetRowOrDefault(entry.Id)?.Name.ExtractText(),
        _ => null,
    };

    private static string FallbackName(BlacklistedFate entry)
        => entry.Type == FateType.Normal ? $"FATE #{entry.Id}" : $"{entry.Type} #{entry.Id}";
}
