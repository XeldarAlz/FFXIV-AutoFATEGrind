using AutoFateGrind.Core.Zones;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using CabinetSheet = Lumina.Excel.Sheets.Cabinet;

namespace AutoFateGrind.Core.Game.Yokai;

internal readonly record struct YokaiStatus(int Medals, bool MinionUnlocked, bool WeaponOwned, byte UnlockedZoneMask)
{
    public bool Reachable => UnlockedZoneMask != 0;
}

internal static unsafe class YokaiProgress
{
    private const int RefreshIntervalMs = 500;

    private static readonly YokaiStatus[] statuses = new YokaiStatus[YokaiCatalog.Entries.Length];
    private static readonly ZoneInfo[]?[] zoneCache = new ZoneInfo[YokaiCatalog.Entries.Length][];
    private static readonly string?[] minionNames = new string[YokaiCatalog.Entries.Length];
    private static readonly string?[] zoneNames = new string[YokaiCatalog.Entries.Length];

    private static uint[]? weaponCabinetIds;
    private static ZoneInfo[]? registrySnapshot;
    private static long refreshedAtMs;

    public static bool Ready { get; private set; }

    public static ReadOnlySpan<YokaiStatus> Statuses
    {
        get
        {
            Refresh();
            return statuses;
        }
    }

    public static void Invalidate() => refreshedAtMs = 0;

    public static string MinionName(int entryIndex)
        => minionNames[entryIndex] ??= ResolveMinionName(YokaiCatalog.Entries[entryIndex].MinionId);

    public static string ZoneNames(int entryIndex)
    {
        DropZoneCacheIfRegistryChanged();
        return zoneNames[entryIndex] ??= BuildZoneNames(entryIndex);
    }

    public static bool IsEnabled(Configuration configuration, int entryIndex)
        => !configuration.YokaiSkippedMinionIds.Contains(YokaiCatalog.Entries[entryIndex].MinionId);

    public static bool IsFarmable(Configuration configuration, int entryIndex)
    {
        var status = Statuses[entryIndex];
        return IsEnabled(configuration, entryIndex)
            && status.MinionUnlocked
            && !status.WeaponOwned
            && status.Reachable
            && status.Medals < MedalTarget(configuration);
    }

    public static int MedalTarget(Configuration configuration) => Math.Max(1, configuration.TargetYokaiMedals);

    public static int ResolveTargetIndex(Configuration configuration, uint preferredMinionId)
    {
        var summonedIndex = YokaiCatalog.IndexOfMinion(YokaiOps.SummonedMinionId());
        if (summonedIndex >= 0 && IsFarmable(configuration, summonedIndex))
        {
            return summonedIndex;
        }

        var preferredIndex = YokaiCatalog.IndexOfMinion(preferredMinionId);
        if (preferredIndex >= 0 && IsFarmable(configuration, preferredIndex))
        {
            return preferredIndex;
        }

        for (var index = 0; index < statuses.Length; index++)
        {
            if (IsFarmable(configuration, index))
            {
                return index;
            }
        }

        return -1;
    }

    public static uint ResolveTargetMinionId(Configuration configuration, uint preferredMinionId)
    {
        var index = ResolveTargetIndex(configuration, preferredMinionId);
        return index < 0 ? 0 : YokaiCatalog.Entries[index].MinionId;
    }

    public static bool IsComplete(Configuration configuration)
        => Ready && ResolveTargetIndex(configuration, 0) < 0;

    public static IReadOnlyList<ZoneInfo> ZonesFor(int entryIndex)
    {
        if (entryIndex < 0)
        {
            return [];
        }

        Refresh();
        return zoneCache[entryIndex] ??= BuildZones(entryIndex);
    }

    public static (int Collected, int Needed) Totals(Configuration configuration)
    {
        var target = MedalTarget(configuration);
        var current = Statuses;
        var collected = 0;
        var needed = 0;
        for (var index = 0; index < current.Length; index++)
        {
            var status = current[index];
            if (!IsEnabled(configuration, index) || !status.MinionUnlocked || status.WeaponOwned)
            {
                continue;
            }

            needed += target;
            collected += Math.Min(status.Medals, target);
        }

        return (collected, needed);
    }

    private static void Refresh()
    {
        var now = Environment.TickCount64;
        if (now - refreshedAtMs < RefreshIntervalMs)
        {
            return;
        }

        var inventory = InventoryManager.Instance();
        var uiState = UIState.Instance();
        if (inventory is null || uiState is null || Svc.Objects.LocalPlayer is null)
        {
            return;
        }

        refreshedAtMs = now;
        DropZoneCacheIfRegistryChanged();

        var cabinetIds = weaponCabinetIds ??= ResolveWeaponCabinetIds();
        var cabinetLoaded = uiState->Cabinet.IsCabinetLoaded();
        var filterByAttunement = ZoneStateReader.AnyAetheryteAttuned();
        var entries = YokaiCatalog.Entries;

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var weaponOwned = inventory->GetInventoryItemCount(entry.WeaponItemId) > 0
                || (cabinetLoaded && cabinetIds[index] != 0 && uiState->Cabinet.IsItemInCabinet(cabinetIds[index]));
            var zoneMask = UnlockedZoneMask(entry.ZoneIds, filterByAttunement);
            if (zoneMask != statuses[index].UnlockedZoneMask)
            {
                zoneCache[index] = null;
            }

            statuses[index] = new YokaiStatus(
                inventory->GetInventoryItemCount(entry.MedalItemId),
                uiState->IsCompanionUnlocked(entry.MinionId),
                weaponOwned,
                zoneMask);
        }

        Ready = true;
    }

    // An empty attuned list means the game has not published it yet, so nothing is filtered on it.
    private static byte UnlockedZoneMask(uint[] zoneIds, bool filterByAttunement)
    {
        byte mask = 0;
        for (var index = 0; index < zoneIds.Length; index++)
        {
            if (!filterByAttunement || ZoneStateReader.IsTerritoryUnlocked(zoneIds[index]))
            {
                mask |= (byte)(1 << index);
            }
        }

        return mask;
    }

    private static void DropZoneCacheIfRegistryChanged()
    {
        var registry = ZoneRegistry.Zones;
        if (ReferenceEquals(registry, registrySnapshot))
        {
            return;
        }

        registrySnapshot = registry;
        Array.Clear(zoneCache);
        Array.Clear(zoneNames);
    }

    private static string BuildZoneNames(int entryIndex)
    {
        var zoneIds = YokaiCatalog.Entries[entryIndex].ZoneIds;
        var names = new List<string>(zoneIds.Length);
        for (var index = 0; index < zoneIds.Length; index++)
        {
            if (FindZone(zoneIds[index]) is { } zone)
            {
                names.Add(zone.Name);
            }
        }

        return string.Join(", ", names);
    }

    private static ZoneInfo[] BuildZones(int entryIndex)
    {
        var zoneIds = YokaiCatalog.Entries[entryIndex].ZoneIds;
        var mask = statuses[entryIndex].UnlockedZoneMask;
        var zones = new List<ZoneInfo>(zoneIds.Length);
        for (var index = 0; index < zoneIds.Length; index++)
        {
            if ((mask & (1 << index)) != 0 && FindZone(zoneIds[index]) is { } zone)
            {
                zones.Add(zone);
            }
        }

        return [.. zones];
    }

    private static ZoneInfo? FindZone(uint territoryId)
    {
        var registry = ZoneRegistry.Zones;
        for (var index = 0; index < registry.Length; index++)
        {
            if (registry[index].TerritoryId == territoryId)
            {
                return registry[index];
            }
        }

        return null;
    }

    private static uint[] ResolveWeaponCabinetIds()
    {
        var entries = YokaiCatalog.Entries;
        var cabinetIds = new uint[entries.Length];
        var sheet = Svc.Data.GetExcelSheet<CabinetSheet>();
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var row = sheet.GetRowAt(rowIndex);
            for (var index = 0; index < entries.Length; index++)
            {
                if (row.Item.RowId == entries[index].WeaponItemId)
                {
                    cabinetIds[index] = row.RowId;
                }
            }
        }

        return cabinetIds;
    }

    private static string ResolveMinionName(uint minionId)
    {
        var name = Svc.Data.GetExcelSheet<Companion>().GetRowOrDefault(minionId)?.Singular.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? minionId.ToString() : name;
    }
}
