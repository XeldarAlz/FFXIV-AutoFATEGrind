using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Localization;
using Dalamud.Bindings.ImGui;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class ConsumableSettings
{
    private const int CatalogRefreshMs = 2000;
    private const int RefreshMinutesMax = 29;

    private static readonly SearchablePicker itemPicker = new(true);

    private static ConsumableEntry[] available = [];
    private static string[] availableLabels = [];
    private static long availableBuiltAtMs;
    private static int availableAddedCount = -1;
    private static int consumablePickerSelection;

    public static void Draw(Configuration cfg)
    {
        DrawConsumableGroup(cfg);
        if (!cfg.AutoConsume)
        {
            return;
        }

        DrawItemsGroup(cfg);
    }

    private static void DrawConsumableGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.ConsumablesGroup)))
        {
            DrawConsumableRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawConsumableRows(Configuration cfg)
    {
        var summonChocobo = cfg.AutoSummonChocobo;
        if (SettingsControls.ToggleRow(L.Settings.AutoSummonChocobo, L.Settings.AutoSummonChocoboHelp, ref summonChocobo))
        {
            cfg.AutoSummonChocobo = summonChocobo;
            cfg.SaveDebounced();
        }

        var autoConsume = cfg.AutoConsume;
        if (SettingsControls.ToggleRow(L.Settings.AutoConsume, L.Settings.AutoConsumeHelp, ref autoConsume))
        {
            cfg.AutoConsume = autoConsume;
            cfg.SaveDebounced();
        }

        if (!cfg.AutoConsume)
        {
            SettingsControls.Note(Loc.T(L.Settings.AutoConsumeOff));
            return;
        }

        Gw.BeginRow(Loc.T(L.Settings.RefreshUnder));
        Gw.Tooltip(Loc.T(L.Settings.RefreshUnderHelp));
        var minutes = cfg.AutoConsumeMinMinutes;
        if (SettingsControls.Slider("##value"u8, ref minutes, 0, RefreshMinutesMax, Loc.T(L.Settings.RefreshFormat), SettingsControls.RowSliderWidth, Loc.T(L.Settings.RefreshWornOff)))
        {
            cfg.AutoConsumeMinMinutes = minutes;
            cfg.SaveDebounced();
        }

        Gw.EndRow();
    }

    private static void DrawItemsGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.ConsumablesItems)))
        {
            DrawAddConsumableRow(cfg);
            SettingsControls.TitleRow(L.Settings.ActiveItems, L.Settings.ActiveItemsHelp);
            DrawConsumableList(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawAddConsumableRow(Configuration cfg)
    {
        RefreshAvailable(cfg);
        if (available.Length == 0)
        {
            SettingsControls.TitleRow(L.Settings.AddItem, L.Settings.AddItemHelp);
            SettingsControls.Note(Loc.T(L.Settings.NoneInBag));
            return;
        }

        consumablePickerSelection = Math.Clamp(consumablePickerSelection, 0, available.Length - 1);
        itemPicker.Draw(L.Settings.AddItem, L.Settings.AddItemHelp, availableLabels, ref consumablePickerSelection);
        var picked = available[consumablePickerSelection];
        var duplicate = IsAdded(cfg, picked.ItemId);

        ImGui.SameLine();
        ImGui.BeginDisabled(duplicate || !itemPicker.HasMatches);
        if (Gw.Button(Loc.T(L.Common.Add), ButtonStyle.Prominent, ButtonSize.Small))
        {
            cfg.AutoConsumeItems.Add(new ConsumableEntry
            {
                ItemId = picked.ItemId,
                Name = picked.Name,
                StatusId = picked.StatusId,
                CanBeHq = picked.CanBeHq,
            });
            cfg.SaveDebounced();
            availableBuiltAtMs = 0;
        }

        ImGui.EndDisabled();
        if (duplicate)
        {
            SettingsControls.InlineNote(Loc.T(L.Settings.AlreadyAdded));
        }
    }

    // Only items in the bag are offered, and the labels carry whether each is already active, so the list follows
    // the inventory on a timer and the active items on every change rather than every frame.
    private static void RefreshAvailable(Configuration cfg)
    {
        var now = Environment.TickCount64;
        if (availableBuiltAtMs > 0 && availableAddedCount == cfg.AutoConsumeItems.Count && now - availableBuiltAtMs < CatalogRefreshMs)
        {
            return;
        }

        availableBuiltAtMs = now;
        availableAddedCount = cfg.AutoConsumeItems.Count;
        var catalog = FoodOps.Catalog;
        var items = new List<ConsumableEntry>(catalog.Count);
        for (var index = 0; index < catalog.Count; index++)
        {
            if (FoodOps.IsAvailable(catalog[index]))
            {
                items.Add(catalog[index]);
            }
        }

        var addedSuffix = string.Concat("  ", Loc.T(L.Settings.Added));
        var labels = new string[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var entry = items[index];
            var kind = entry.StatusId == FoodOps.WellFedStatusId ? Loc.T(L.Settings.KindFood) : Loc.T(L.Settings.KindMedicine);
            var taken = IsAdded(cfg, entry.ItemId) ? addedSuffix : string.Empty;
            labels[index] = Loc.T(L.Settings.ItemLabel, entry.Name, kind, taken);
        }

        available = items.ToArray();
        availableLabels = labels;
    }

    private static bool IsAdded(Configuration cfg, uint itemId)
    {
        for (var index = 0; index < cfg.AutoConsumeItems.Count; index++)
        {
            if (cfg.AutoConsumeItems[index].ItemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    private static void DrawConsumableList(Configuration cfg)
    {
        if (cfg.AutoConsumeItems.Count == 0)
        {
            SettingsControls.Note(Loc.T(L.Settings.NoItemsAdded));
            return;
        }

        var remove = -1;
        for (var index = 0; index < cfg.AutoConsumeItems.Count; index++)
        {
            var entry = cfg.AutoConsumeItems[index];
            var kind = entry.StatusId == FoodOps.WellFedStatusId ? Loc.T(L.Settings.WellFed) : Loc.T(L.Settings.Medicated);
            var status = FoodOps.IsAvailable(entry) ? kind : Loc.T(L.Settings.NoneInBagShort, kind);
            ImGui.PushID(index);
            Gw.BeginRow($"{index + 1}. {entry.Name}", status);
            if (SettingsControls.RemoveButton())
            {
                remove = index;
            }

            Gw.EndRow();
            ImGui.PopID();
        }

        if (remove < 0)
        {
            return;
        }

        cfg.AutoConsumeItems.RemoveAt(remove);
        cfg.SaveDebounced();
        availableBuiltAtMs = 0;
    }
}
