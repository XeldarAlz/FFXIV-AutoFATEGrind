using clib.Utils;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;

namespace AutoFateGrind.Core.Game.Fates;

internal readonly record struct BlacklistedFate(FateType Type, uint Id);

internal readonly record struct BlacklistedFateGroup(string Name, BlacklistedFate[] Entries);

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

    public static void Add(Configuration cfg, FateType type, uint[] fateIds)
    {
        if (!cfg.BlacklistedTypeIds.TryGetValue((int)type, out var set))
        {
            cfg.BlacklistedTypeIds[(int)type] = set = [];
        }

        var added = false;
        for (var index = 0; index < fateIds.Length; index++)
        {
            added |= set.Add(fateIds[index]);
        }

        if (added)
        {
            cfg.SaveDebounced();
        }
    }

    public static IReadOnlyList<BlacklistedFateGroup> All(Configuration cfg)
    {
        var byName = new Dictionary<string, List<BlacklistedFate>>(StringComparer.OrdinalIgnoreCase);

        var legacyOverworldIds = cfg.BlacklistedFateIds;
        foreach (var fateId in legacyOverworldIds)
        {
            Collect(byName, new BlacklistedFate(FateType.Normal, fateId));
        }

        foreach (var (typeKey, set) in cfg.BlacklistedTypeIds)
        {
            foreach (var fateId in set)
            {
                Collect(byName, new BlacklistedFate((FateType)typeKey, fateId));
            }
        }

        var groups = new List<BlacklistedFateGroup>(byName.Count);
        foreach (var (name, entries) in byName)
        {
            groups.Add(new BlacklistedFateGroup(name, [.. entries]));
        }

        groups.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return groups;
    }

    private static void Collect(Dictionary<string, List<BlacklistedFate>> byName, BlacklistedFate entry)
    {
        var name = DisplayName(entry);
        if (!byName.TryGetValue(name, out var entries))
        {
            byName[name] = entries = [];
        }

        if (!entries.Contains(entry))
        {
            entries.Add(entry);
        }
    }

    public static void Remove(Configuration cfg, BlacklistedFateGroup group)
    {
        var changed = false;
        for (var index = 0; index < group.Entries.Length; index++)
        {
            changed |= RemoveEntry(cfg, group.Entries[index]);
        }

        if (changed)
        {
            cfg.SaveDebounced();
        }
    }

    private static bool RemoveEntry(Configuration cfg, BlacklistedFate entry)
    {
        var changed = entry.Type == FateType.Normal && cfg.BlacklistedFateIds.Remove(entry.Id);

        var typeKey = (int)entry.Type;
        if (cfg.BlacklistedTypeIds.TryGetValue(typeKey, out var set) && set.Remove(entry.Id))
        {
            changed = true;
            if (set.Count == 0)
            {
                cfg.BlacklistedTypeIds.Remove(typeKey);
            }
        }

        return changed;
    }

    private static string DisplayName(BlacklistedFate entry)
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
