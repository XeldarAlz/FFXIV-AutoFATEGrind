using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Game;

internal static unsafe class ClassSwitcher
{
    // 0xFF = no gearset equipped.
    private const byte InvalidGearsetIndex = 0xFF;

    public static int CurrentGearsetIndex
    {
        get
        {
            var mod = RaptureGearsetModule.Instance();
            if (mod is null) return -1;
            var idx = mod->CurrentGearsetIndex;
            return idx == InvalidGearsetIndex ? -1 : idx;
        }
    }

    public static bool IsValidGearset(int index)
        => RaptureGearsetModule.Instance()->IsValidGearset(index);

    // UI is 1-based (matches in-game gearset list); API is 0-based.
    public static bool IsValidUserIndex(byte userIndex)
        => userIndex >= 1 && IsValidGearset(userIndex - 1);

    public static byte JobIdForUserIndex(byte userIndex)
    {
        if (!IsValidUserIndex(userIndex)) return 0;
        var entry = RaptureGearsetModule.Instance()->GetGearset(userIndex - 1);
        return entry is null ? (byte)0 : entry->ClassJob;
    }

    public static string JobNameForUserIndex(byte userIndex)
    {
        var jobId = JobIdForUserIndex(userIndex);
        return JobNameForJobId(jobId);
    }

    public static string JobNameForJobId(byte jobId)
    {
        if (jobId == 0) return "—";
        var sheet = Svc.Data.GetExcelSheet<ClassJob>();
        var row = sheet?.GetRowOrDefault(jobId);
        var abbr = row?.Abbreviation.ExtractText();
        return string.IsNullOrEmpty(abbr) ? $"#{jobId}" : abbr!;
    }

    public static int UnsyncedLevelForJobId(byte jobId)
    {
        if (jobId == 0) return 0;
        var sheet = Svc.Data.GetExcelSheet<ClassJob>();
        var row = sheet?.GetRowOrDefault(jobId);
        if (row is null) return 0;
        return Svc.PlayerState.GetClassJobLevel(row.Value);
    }

    public static int GameMaxLevel
    {
        get
        {
            var state = PlayerState.Instance();
            return state is null ? 100 : state->MaxLevel;
        }
    }

    public readonly record struct GearsetOption(byte UserIndex, byte JobId, string Name);

    // Crafters/gatherers can't engage FATEs, so they're filtered from the picker.
    public static bool IsCombatJob(byte jobId)
    {
        if (jobId == 0) return false;
        var sheet = Svc.Data.GetExcelSheet<ClassJob>();
        var row = sheet?.GetRowOrDefault(jobId);
        return row?.Role > 0;
    }

    public static List<GearsetOption> EnumerateGearsets()
    {
        var result = new List<GearsetOption>();
        var mod = RaptureGearsetModule.Instance();
        if (mod is null) return result;

        for (var i = 0; i < 100; i++)
        {
            if (!mod->IsValidGearset(i)) continue;
            var entry = mod->GetGearset(i);
            if (entry is null) continue;
            var jobId = entry->ClassJob;
            if (!IsCombatJob(jobId)) continue;
            var name = entry->NameString ?? string.Empty;
            result.Add(new GearsetOption((byte)(i + 1), jobId, name));
        }
        return result;
    }

    public static int FindActiveEntryIndex(IReadOnlyList<ClassQueueEntry> queue)
    {
        for (var i = 0; i < queue.Count; i++)
        {
            var e = queue[i];
            if (!IsValidUserIndex(e.GearsetIndex)) continue;
            var jobId = JobIdForUserIndex(e.GearsetIndex);
            if (jobId == 0) continue;
            if (e.StopAtLevel <= 0) return i;
            if (UnsyncedLevelForJobId(jobId) < e.StopAtLevel) return i;
        }
        return -1;
    }

    // Fire-and-forget: the actual class change is async game-side; callers shouldn't block.
    public static bool TryEquip(ClassQueueEntry entry)
    {
        if (!IsValidUserIndex(entry.GearsetIndex)) return false;
        var apiIndex = entry.GearsetIndex - 1;
        var mod = RaptureGearsetModule.Instance();
        if (mod is null) return false;

        if (mod->CurrentGearsetIndex == apiIndex) return true;

        // 2nd arg = glamour plate id; 0 keeps the gearset's linked plate.
        var result = mod->EquipGearset(apiIndex, 0);
        if (result != 0)
        {
            Svc.Log.Warning($"[AFG] EquipGearset({apiIndex}) returned {result}");
            return false;
        }
        return true;
    }
}
