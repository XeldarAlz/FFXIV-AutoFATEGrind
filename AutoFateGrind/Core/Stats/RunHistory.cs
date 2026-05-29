using ECommons.DalamudServices;
using Newtonsoft.Json;
using System.IO;

namespace AutoFateGrind.Core.Stats;

// Persists completed runs to a JSON file in the plugin config directory, separate from the main config so
// a large history can't bloat (or corrupt) the settings file. Owned and constructed by Plugin (see
// Plugin.History) so its lifetime tracks the plugin's — no self-managed static singleton. All disk access
// is best-effort and guarded; a read/write failure degrades to an empty/unsaved history rather than
// throwing into the grind.
internal sealed class RunHistory
{
    private const int MaxRecords = 500;
    private const string FileName = "run-history.json";

    public List<RunRecord> Records { get; private set; } = [];

    // Cached so the history window can render lifetime totals every frame without re-summing every record;
    // recomputed only when the record set actually changes (load / append / clear).
    public LifetimeTotals Lifetime { get; private set; }

    public RunHistory()
    {
        Load();
        RecomputeLifetime();
    }

    private static string FilePath
        => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, FileName);

    private void Load()
    {
        try
        {
            var path = FilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var records = JsonConvert.DeserializeObject<List<RunRecord>>(json);
                if (records is not null) Records = records;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[AFG] RunHistory load failed; starting empty");
        }
    }

    public void Append(RunRecord record)
    {
        Records.Add(record);
        // Newest first; trim the oldest beyond the cap.
        Records.Sort((a, b) => b.EndedAtUtc.CompareTo(a.EndedAtUtc));
        if (Records.Count > MaxRecords)
            Records.RemoveRange(MaxRecords, Records.Count - MaxRecords);
        RecomputeLifetime();
        Save();
    }

    public void Clear()
    {
        Records.Clear();
        RecomputeLifetime();
        Save();
    }

    private void RecomputeLifetime()
    {
        var totals = new LifetimeTotals { Runs = Records.Count };
        foreach (var r in Records)
        {
            totals.Fates += r.FatesCompleted;
            totals.Gemstones += r.GemstonesEarned;
            totals.Exp += r.ExpEarned;
            totals.Levels += r.LevelsGained;
            totals.Seconds += r.DurationSeconds;
        }
        Lifetime = totals;
    }

    private void Save()
    {
        try
        {
            var dir = Plugin.PluginInterface.ConfigDirectory;
            if (!dir.Exists) dir.Create();
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(Records, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[AFG] RunHistory save failed");
        }
    }

    public struct LifetimeTotals
    {
        public int Runs;
        public int Fates;
        public int Gemstones;
        public long Exp;
        public int Levels;
        public double Seconds;

        public readonly double FatesPerHour => Seconds > 0 ? Fates / (Seconds / 3600.0) : 0;
        public readonly TimeSpan Duration => TimeSpan.FromSeconds(Seconds);
    }
}
