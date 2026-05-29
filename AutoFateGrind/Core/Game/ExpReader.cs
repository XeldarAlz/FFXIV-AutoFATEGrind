using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Game;

// Reads current-job level/experience through Dalamud's IPlayerState (a stable managed API) plus the
// ParamGrow sheet for the per-level curve — no raw struct offsets, so a patch can't silently corrupt it.
// Every accessor returns null on any failure; callers treat "exp unavailable" as a soft, optional metric.
internal static class ExpReader
{
    public readonly record struct Snapshot(uint JobId, string JobAbbr, int Level, long ExpIntoLevel, bool IsMax);

    // cumulativeByLevel[L] = total experience required to reach level L from level 1 (so [1] == 0). Built
    // once from the ParamGrow curve and reused — the per-frame "time to cap" readout would otherwise re-sum
    // the whole sheet on every frame.
    private static long[]? cumulativeByLevel;

    public static Snapshot? Read()
    {
        try
        {
            if (Svc.Objects.LocalPlayer is null) return null;

            var ps = Svc.PlayerState;
            var cj = ps.ClassJob;
            if (cj.RowId == 0) return null;
            if (cj.ValueNullable is not { } jobRow) return null;

            var level = ps.Level;
            if (level <= 0) return null;

            var maxLevel = ClassSwitcher.GameMaxLevel;
            var isMax = level >= maxLevel;

            long expIntoLevel = isMax ? 0 : ps.GetClassJobExperience(jobRow);
            var abbr = jobRow.Abbreviation.ExtractText() ?? string.Empty;

            return new Snapshot(cj.RowId, abbr, level, expIntoLevel, isMax);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[AFG] ExpReader.Read failed");
            return null;
        }
    }

    // Total experience the current job has accrued from level 1 — lets callers diff two samples into an
    // "earned" total that correctly spans level-ups.
    public static long Cumulative(Snapshot s) => CumulativeAtLevelStart(s.Level) + s.ExpIntoLevel;

    public static long? CumulativeExp() => Read() is { } s ? Cumulative(s) : null;

    // Experience remaining for the current job to reach the level cap, or null at cap / when unreadable.
    public static long? ExpToCap()
    {
        if (Read() is not { } s || s.IsMax) return null;
        var capTotal = CumulativeAtLevelStart(ClassSwitcher.GameMaxLevel);
        return Math.Max(0, capTotal - Cumulative(s));
    }

    private static long CumulativeAtLevelStart(int level)
    {
        var table = EnsureTable();
        if (table is null || level < 1) return 0;
        if (level >= table.Length) level = table.Length - 1;
        return table[level];
    }

    private static long[]? EnsureTable()
    {
        if (cumulativeByLevel is not null) return cumulativeByLevel;

        var sheet = Svc.Data.GetExcelSheet<ParamGrow>();
        if (sheet is null) return null;

        var max = Math.Max(1, ClassSwitcher.GameMaxLevel);
        var table = new long[max + 1];   // index by level; table[1] == 0
        long total = 0;
        for (var l = 1; l < max; l++)
        {
            var row = sheet.GetRowOrDefault((uint)l);
            var toNext = row is { } r ? r.ExpToNext : 0;
            if (toNext > 0) total += toNext;
            table[l + 1] = total;   // cumulative needed to reach level l+1
        }
        cumulativeByLevel = table;
        return table;
    }
}
