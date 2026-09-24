using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Stats;
using AutoFateGrind.Core.Trading;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using System.Diagnostics;

namespace AutoFateGrind.Core.Localization;

// Game text follows the client language, which is picked independently of the plugin language, so its
// glyphs come from the sheets and game strings the UI draws rather than from the active catalog.
internal static class GameTextGlyphs
{
    private static readonly bool[] present = new bool[GlyphRanges.CodepointCount];

    public static int Version { get; private set; }

    public static ReadOnlySpan<bool> Present => present;

    public static void Collect(Configuration configuration, RunHistory history)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            ScanSheets();
            ScanCatalogs();
            ScanSaved(configuration, history);
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Failed to collect game text glyphs; some game names may render as missing glyphs");
            return;
        }

        RunLog.Info($"Collected {CountGlyphs()} game text glyphs in {watch.ElapsedMilliseconds} ms");
    }

    public static void Add(string? text)
    {
        if (GlyphRanges.MarkText(present, text))
        {
            Version++;
        }
    }

    private static void ScanSheets()
    {
        ScanSheet<PlaceName>(static row => row.Name);
        ScanSheet<Fate>(static row => row.Name);
        ScanSheet<DynamicEvent>(static row => row.Name);
        ScanSheet<WKSMechaEventData>(static row => row.Name);
        ScanSheet<ClassJob>(static row => row.Abbreviation);
        // Gearsets are named after their job by default, so job names are baked up front, not on first sight.
        ScanSheet<ClassJob>(static row => row.Name);
    }

    private static void ScanSheet<T>(Func<T, ReadOnlySeString> text) where T : struct, IExcelRow<T>
    {
        var sheet = Svc.Data.GetExcelSheet<T>();
        for (var index = 0; index < sheet.Count; index++)
        {
            Add(text(sheet.GetRowAt(index)).ExtractText());
        }
    }

    private static void ScanCatalogs()
    {
        var consumables = FoodOps.Catalog;
        for (var index = 0; index < consumables.Count; index++)
        {
            Add(consumables[index].Name);
        }

        var tradeItems = GemstoneCatalog.All;
        for (var index = 0; index < tradeItems.Length; index++)
        {
            Add(tradeItems[index].ItemName);
        }

        for (var index = 0; index < YokaiCatalog.Entries.Length; index++)
        {
            Add(YokaiProgress.MinionName(index));
        }
    }

    private static void ScanSaved(Configuration configuration, RunHistory history)
    {
        Add(configuration.PreferredRepairNpc?.Name);

        var consumables = configuration.AutoConsumeItems;
        for (var index = 0; index < consumables.Count; index++)
        {
            Add(consumables[index].Name);
        }

        var runs = history.Records;
        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];
            Add(run.JobAbbr);
            for (var zoneIndex = 0; zoneIndex < run.ZoneNames.Count; zoneIndex++)
            {
                Add(run.ZoneNames[zoneIndex]);
            }
        }
    }

    private static int CountGlyphs()
    {
        var count = 0;
        for (var codepoint = 0; codepoint < present.Length; codepoint++)
        {
            if (present[codepoint])
            {
                count++;
            }
        }

        return count;
    }
}
