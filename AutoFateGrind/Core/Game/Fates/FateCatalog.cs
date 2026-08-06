using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Game.Fates;

internal readonly record struct FateCatalogEntry(string Name, uint[] FateIds);

internal static class FateCatalog
{
    private static FateCatalogEntry[] entries = [];
    private static string[] labels = [];

    public static FateCatalogEntry[] All
    {
        get
        {
            EnsureLoaded();
            return entries;
        }
    }

    public static string[] Labels
    {
        get
        {
            EnsureLoaded();
            return labels;
        }
    }

    private static void EnsureLoaded()
    {
        if (entries.Length > 0)
        {
            return;
        }

        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>();
        if (sheet is null)
        {
            return;
        }

        var byName = new Dictionary<string, (byte Level, List<uint> FateIds)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in sheet)
        {
            if (row.EurekaFate != 0)
            {
                continue;
            }

            var name = row.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!byName.TryGetValue(name, out var grouped))
            {
                byName[name] = grouped = (row.ClassJobLevel, new List<uint>());
            }

            grouped.FateIds.Add(row.RowId);
        }

        var ordered = byName.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var loadedEntries = new FateCatalogEntry[ordered.Length];
        var loadedLabels = new string[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            var (name, grouped) = ordered[index];
            loadedEntries[index] = new FateCatalogEntry(name, [.. grouped.FateIds]);
            loadedLabels[index] = grouped.Level > 0 ? $"{name}  (Lv {grouped.Level})" : name;
        }

        entries = loadedEntries;
        labels = loadedLabels;
    }
}
