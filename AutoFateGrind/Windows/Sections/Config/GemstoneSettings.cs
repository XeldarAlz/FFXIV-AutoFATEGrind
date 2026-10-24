using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class GemstoneSettings
{
    private const int ThresholdMin = 100;
    private const int SpendGemsMin = 50;
    private const int BuyQuantityMin = 1;
    private const int BuyQuantityMax = 99;
    private const int ListRefreshMs = 1000;
    private const int ReachRecheckMs = 2000;
    private const int MaxStopAtCount = 999;
    private const float StopSliderWidth = 150f;

    private static readonly GemstoneSpendMode[] spendModes =
        [GemstoneSpendMode.SpendAll, GemstoneSpendMode.SpendGems, GemstoneSpendMode.BuyQuantity];

    private static readonly SettingsControls.Choices.Choice[] spendModeChoices =
    [
        new(L.Settings.SpendAllName, L.Settings.SpendAllDetail),
        new(L.Settings.SpendUpToName, L.Settings.SpendUpToDetail),
        new(L.Settings.BuyFixedName, L.Settings.BuyFixedDetail),
    ];

    private static readonly SettingsControls.Choices.Choice[] afterTradeChoices =
    [
        new(L.Settings.AfterResumeName, L.Settings.AfterResumeDetail),
        new(L.Settings.AfterStopName, L.Settings.AfterStopDetail),
    ];

    private static readonly HashSet<uint> noSkippedItems = [];
    private static readonly SearchablePicker itemPicker = new(true);

    private readonly record struct ListRow(uint ItemId, string Name, bool IsCollectible, string Status, string? Tooltip);

    private static GemstoneTradeItem[]? sortedItems;
    private static string[]? sortedLabels;
    private static int pickerSelection;
    private static ListRow[] listRows = [];
    private static long listRowsTickMs;
    private static string previewText = string.Empty;
    private static bool previewBlocked;
    private static uint reachNoteItemId;
    private static long reachNoteTickMs;
    private static string? reachNote;

    public static void Draw(Configuration cfg)
    {
        DrawTriggerGroup(cfg);
        if (!cfg.TradeOnCap)
        {
            return;
        }

        DrawItemGroup(cfg);
        DrawSpendGroup(cfg);
        DrawAfterGroup(cfg);
    }

    private static void DrawTriggerGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.GemsTrigger)))
        {
            DrawTriggerRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawTriggerRows(Configuration cfg)
    {
        var tradeOnCap = cfg.TradeOnCap;
        if (SettingsControls.ToggleRow(L.Settings.AutoTrade, L.Settings.AutoTradeHelp, ref tradeOnCap))
        {
            cfg.TradeOnCap = tradeOnCap;
            cfg.SaveDebounced();
        }

        if (!cfg.TradeOnCap)
        {
            SettingsControls.Note(Loc.T(L.Settings.AutoTradeOff));
            return;
        }

        var threshold = cfg.TradeThreshold;
        if (SettingsControls.SliderRow(L.Settings.Threshold, L.Settings.ThresholdHelp, ref threshold, ThresholdMin, Core.AfgConstants.BicolorCap, Loc.T(L.Settings.GemsFormat)))
        {
            cfg.TradeThreshold = threshold;
            cfg.SaveDebounced();
        }
    }

    private static void DrawItemGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.GemsItem)))
        {
            DrawAddRow(cfg);
            SettingsControls.TitleRow(L.Settings.ShoppingList, L.Settings.ShoppingListHelp);
            DrawShoppingList(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawAddRow(Configuration cfg)
    {
        EnsureSortedCatalog();
        if (sortedItems is null || sortedLabels is null || sortedItems.Length == 0)
        {
            SettingsControls.TitleRow(L.Settings.AddTradeItem, L.Settings.ItemToBuyHelp);
            SettingsControls.Note(Loc.T(L.Settings.NoShopItems), Frame.Look.Destructive);
            return;
        }

        pickerSelection = Math.Clamp(pickerSelection, 0, sortedItems.Length - 1);
        itemPicker.Draw(L.Settings.AddTradeItem, L.Settings.ItemToBuyHelp, sortedLabels, ref pickerSelection);
        var picked = sortedItems[pickerSelection];
        var listed = IsListed(cfg.TradeList, picked.ItemId);

        ImGui.SameLine();
        ImGui.BeginDisabled(listed || !itemPicker.HasMatches);
        if (Gw.Button(Loc.T(L.Common.Add), ButtonStyle.Prominent, ButtonSize.Small))
        {
            cfg.TradeList.Add(new TradeListEntry { ItemId = picked.ItemId });
            cfg.SaveDebounced();
            listRowsTickMs = 0;
        }

        ImGui.EndDisabled();
        if (listed)
        {
            SettingsControls.InlineNote(Loc.T(L.Settings.AlreadyListed));
        }

        DrawTraderReach(picked);
    }

    private static bool IsListed(List<TradeListEntry> entries, uint itemId)
    {
        for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            if (entries[entryIndex].ItemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    private static void DrawShoppingList(Configuration cfg)
    {
        if (cfg.TradeList.Count == 0)
        {
            SettingsControls.Note(Loc.T(L.Settings.ShoppingListEmpty), Frame.Look.Destructive);
            return;
        }

        RefreshListRows(cfg);

        int? moveUp = null, moveDown = null, remove = null;
        var count = cfg.TradeList.Count;
        for (var entryIndex = 0; entryIndex < count; entryIndex++)
        {
            ImGui.PushID(entryIndex);
            switch (DrawShoppingRow(cfg, cfg.TradeList[entryIndex], listRows[entryIndex], entryIndex, count))
            {
                case SettingsControls.ListAction.MoveUp: moveUp = entryIndex; break;
                case SettingsControls.ListAction.MoveDown: moveDown = entryIndex; break;
                case SettingsControls.ListAction.Remove: remove = entryIndex; break;
            }

            ImGui.PopID();
        }

        if (ListReorder.Apply(cfg.TradeList, count, moveUp, moveDown, remove))
        {
            cfg.SaveDebounced();
            listRowsTickMs = 0;
        }
    }

    private static SettingsControls.ListAction DrawShoppingRow(Configuration cfg, TradeListEntry entry, ListRow row, int entryIndex, int count)
    {
        Gw.BeginRow(row.Name, row.Status);
        if (row.Tooltip is not null)
        {
            Gw.Tooltip(row.Tooltip);
        }

        DrawStopControl(cfg, entry, row);
        ImGui.SameLine();
        var action = SettingsControls.ReorderButtons(entryIndex > 0, entryIndex < count - 1);
        Gw.EndRow();
        return action;
    }

    private static void DrawStopControl(Configuration cfg, TradeListEntry entry, ListRow row)
    {
        if (row.IsCollectible)
        {
            SettingsControls.TextOnLine(Loc.T(L.Settings.BuyOnce), Frame.Look.MinHit * Frame.Scale, TextRole.Footnote);
            return;
        }

        var stopAt = entry.StopAtCount;
        if (SettingsControls.Slider("##stop"u8, ref stopAt, 0, MaxStopAtCount, Loc.T(L.Settings.StopAtCount), StopSliderWidth, Loc.T(L.Settings.KeepBuying)))
        {
            entry.StopAtCount = stopAt;
            cfg.SaveDebounced();
            listRowsTickMs = 0;
        }
    }

    // Row states read the inventory, unlock flags and trader reach, so they are rebuilt about once a second rather
    // than every frame; edits reset the clock so the list reacts at once.
    private static void RefreshListRows(Configuration cfg)
    {
        var now = Environment.TickCount64;
        if (listRows.Length == cfg.TradeList.Count && now - listRowsTickMs < ListRefreshMs && RowsMatch(cfg.TradeList))
        {
            return;
        }

        listRowsTickMs = now;
        var skipped = Plugin.Instance.Controller.SessionSnapshot?.TradeSkippedItemIds ?? noSkippedItems;
        var rows = new ListRow[cfg.TradeList.Count];
        for (var entryIndex = 0; entryIndex < rows.Length; entryIndex++)
        {
            rows[entryIndex] = BuildRow(cfg.TradeList[entryIndex], skipped);
        }

        listRows = rows;
        (previewText, previewBlocked) = BuildPreview(cfg);
    }

    private static bool RowsMatch(List<TradeListEntry> entries)
    {
        for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            if (listRows[entryIndex].ItemId != entries[entryIndex].ItemId)
            {
                return false;
            }
        }

        return true;
    }

    private static ListRow BuildRow(TradeListEntry entry, HashSet<uint> skipped)
    {
        var state = TradeList.StateOf(entry, out var item, out var held);
        if (item is null)
        {
            return new ListRow(entry.ItemId, Loc.T(L.Settings.UnknownTradeItem, entry.ItemId), false, string.Empty, null);
        }

        var name = Loc.T(L.Settings.ItemCostLabel, item.ItemName, item.CostPerOne);
        if (skipped.Contains(item.ItemId))
        {
            return new ListRow(item.ItemId, name, item.IsCollectible, Loc.T(L.Settings.StatusSkipped), null);
        }

        return state switch
        {
            TradeEntryState.Learned => new ListRow(item.ItemId, name, true, Loc.T(L.Settings.StatusLearned), null),
            TradeEntryState.Held => new ListRow(item.ItemId, name, true, Loc.T(L.Settings.StatusHeld), null),
            TradeEntryState.Stocked => new ListRow(item.ItemId, name, false, Loc.T(L.Settings.StatusHaveOf, held, entry.StopAtCount), null),
            TradeEntryState.NoTrader => new ListRow(item.ItemId, name, item.IsCollectible, Loc.T(L.Settings.StatusNoTrader), BuildReachNote(item)),
            _ when item.IsCollectible => new ListRow(item.ItemId, name, true, string.Empty, null),
            _ when entry.StopAtCount > 0 => new ListRow(item.ItemId, name, false, Loc.T(L.Settings.StatusHaveOf, held, entry.StopAtCount), null),
            _ => new ListRow(item.ItemId, name, false, Loc.T(L.Settings.StatusHave, held), null),
        };
    }

    // The picker carries every Bicolor item in game data, including ones only sold in expansions the character has
    // not reached, and buying one used to fail mid-run at the teleport (issue #54). Resolving a seller walks shop
    // rows and reads Excel, so the answer is cached rather than rebuilt every frame; the recheck picks up an
    // attunement (or a language switch) made while the page is open.
    private static void DrawTraderReach(GemstoneTradeItem item)
    {
        var now = Environment.TickCount64;
        if (item.ItemId != reachNoteItemId || now - reachNoteTickMs > ReachRecheckMs)
        {
            reachNoteItemId = item.ItemId;
            reachNoteTickMs = now;
            reachNote = BuildReachNote(item);
        }

        if (reachNote is null)
        {
            return;
        }

        SettingsControls.Note(reachNote, Frame.Look.Destructive);
    }

    private static string? BuildReachNote(GemstoneTradeItem item)
    {
        if (GemstoneTrader.PickForItem(item.ItemId, null, null, out var availability) is not null)
        {
            return null;
        }

        return availability == TraderAvailability.AllLocked
            ? Loc.T(L.Settings.TraderLocked, item.ItemName, GemstoneTrader.DescribeSellerZones(item.ItemId))
            : Loc.T(L.Settings.TraderMissing, item.ItemName);
    }

    // Catalog order is cost-ascending. The picker wants A-Z for scanning, so keep a name-sorted view cached alongside
    // its labels; rebuilt only if the catalog populates or changes length after game data loads.
    private static void EnsureSortedCatalog()
    {
        var catalog = GemstoneCatalog.All;
        if (sortedItems is not null && sortedItems.Length == catalog.Length)
        {
            return;
        }

        sortedItems = [.. catalog.OrderBy(item => item.ItemName, StringComparer.OrdinalIgnoreCase)];
        sortedLabels = new string[sortedItems.Length];
        for (var itemIndex = 0; itemIndex < sortedItems.Length; itemIndex++)
        {
            var item = sortedItems[itemIndex];
            sortedLabels[itemIndex] = Loc.T(L.Settings.ItemCostLabel, item.ItemName, item.CostPerOne);
        }
    }

    private static void DrawSpendGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.GemsSpend)))
        {
            DrawSpendRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawSpendRows(Configuration cfg)
    {
        var selected = Math.Max(0, Array.IndexOf(spendModes, cfg.SpendMode));
        if (SettingsControls.Choices.DrawRow(L.Settings.SpendStrategy, L.Settings.SpendStrategyHelp, spendModeChoices, ref selected))
        {
            cfg.SpendMode = spendModes[selected];
            cfg.SaveDebounced();
            listRowsTickMs = 0;
        }

        if (cfg.SpendMode == GemstoneSpendMode.SpendGems)
        {
            var spendGems = cfg.SpendGemsAmount;
            if (SettingsControls.SliderRow(L.Settings.SpendUpTo, L.Settings.SpendUpToHelp, ref spendGems, SpendGemsMin, Core.AfgConstants.BicolorCap, Loc.T(L.Settings.GemsFormat)))
            {
                cfg.SpendGemsAmount = spendGems;
                cfg.SaveDebounced();
            }
        }
        else if (cfg.SpendMode == GemstoneSpendMode.BuyQuantity)
        {
            var quantity = cfg.BuyQuantityAmount;
            if (SettingsControls.SliderRow(L.Settings.BuyQuantity, L.Settings.BuyQuantityHelp, ref quantity, BuyQuantityMin, BuyQuantityMax, Loc.T(L.Settings.BuyQuantityFormat)))
            {
                cfg.BuyQuantityAmount = quantity;
                cfg.SaveDebounced();
            }
        }

        var reserve = cfg.KeepGemstonesReserve;
        if (SettingsControls.SliderRow(L.Settings.Reserve, L.Settings.ReserveHelp, ref reserve, 0, Core.AfgConstants.BicolorCap, Loc.T(L.Settings.GemsFormat)))
        {
            cfg.KeepGemstonesReserve = reserve;
            cfg.SaveDebounced();
        }

        DrawSpendPreview(cfg);
    }

    private static void DrawAfterGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.GemsAfter)))
        {
            var selected = cfg.AfterTrade == AfterTradeAction.Stop ? 1 : 0;
            if (SettingsControls.Choices.DrawRow(L.Settings.WhenDone, L.Settings.WhenDoneHelp, afterTradeChoices, ref selected))
            {
                cfg.AfterTrade = selected == 1 ? AfterTradeAction.Stop : AfterTradeAction.Resume;
                cfg.SaveDebounced();
            }
        }

        Gw.EndSection();
    }

    private static void DrawSpendPreview(Configuration cfg)
    {
        if (cfg.TradeList.Count == 0)
        {
            return;
        }

        RefreshListRows(cfg);
        SettingsControls.Note(previewText, previewBlocked ? Frame.Look.Destructive : null);
    }

    // Replays the shop's budget split at the threshold wallet, trader by trader, so the note matches what the next
    // trade would buy.
    private static (string Text, bool Blocked) BuildPreview(Configuration cfg)
    {
        var plannedItemIds = new HashSet<uint>();
        var plan = TradeList.Plan(cfg.TradeThreshold, null, null, plannedItemIds);
        if (plan is null)
        {
            return (Loc.T(L.Settings.PreviewNothing, cfg.TradeThreshold, cfg.KeepGemstonesReserve), true);
        }

        var wallet = cfg.TradeThreshold;
        var spent = 0;
        var parts = new List<string>(cfg.TradeList.Count);
        while (plan is not null)
        {
            for (var orderIndex = 0; orderIndex < plan.Orders.Length; orderIndex++)
            {
                var item = plan.Orders[orderIndex].Item;
                plannedItemIds.Add(item.ItemId);
                var need = TradeList.RemainingNeed(item, plan.Orders[orderIndex].StopAtCount);
                var quantity = GemstoneCatalog.ComputeBuyQuantity(wallet, item.CostPerOne, need, spent);
                if (quantity <= 0)
                {
                    continue;
                }

                var cost = quantity * (int)item.CostPerOne;
                wallet -= cost;
                spent += cost;
                parts.Add(Loc.T(L.Settings.PreviewItem, quantity, item.ItemName));
            }

            plan = TradeList.Plan(wallet, plan.Trader.TerritoryId, plan.Trader.Expansion, plannedItemIds, spent);
        }

        return (Loc.T(L.Settings.PreviewPlan, cfg.TradeThreshold, cfg.KeepGemstonesReserve, string.Join(", ", parts), spent), false);
    }
}
