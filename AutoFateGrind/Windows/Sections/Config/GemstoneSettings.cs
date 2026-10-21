using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class GemstoneSettings
{
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

    public static void Draw(Configuration cfg)
    {
        DrawTriggerGroup(cfg);
        using var more = Motion.PushSection("##tr_more", cfg.TradeOnCap);
        if (more is null)
        {
            return;
        }

        DrawItemGroup(cfg);
        DrawSpendGroup(cfg);
        DrawAfterGroup(cfg);
    }

    private static void DrawTriggerGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GemsTrigger));

        SettingsRow.Draw(Loc.T(L.Settings.AutoTrade),
            Loc.T(L.Settings.AutoTradeHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.TradeOnCap, v => cfg.TradeOnCap = v, "##tr_oncap"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##tr_body", cfg.TradeOnCap);
        if (!cfg.TradeOnCap)
        {
            SettingsRow.Note(Loc.T(L.Settings.AutoTradeOff));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.Threshold),
            Loc.T(L.Settings.ThresholdHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##tr_threshold",
                () => cfg.TradeThreshold, v => cfg.TradeThreshold = Math.Clamp(v, 100, Core.AfgConstants.BicolorCap),
                100, Core.AfgConstants.BicolorCap, Loc.T(L.Settings.GemsFormat)));
    }

    private static GemstoneTradeItem[]? sortedItems;
    private static string[]? sortedLabels;
    private static int pickerSelection;

    private static void DrawItemGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GemsItem));

        SettingsRow.DrawBlock(Loc.T(L.Settings.AddTradeItem),
            Loc.T(L.Settings.ItemToBuyHelp),
            () => DrawAddRow(cfg));

        SettingsRow.DrawBlock(Loc.T(L.Settings.ShoppingList),
            Loc.T(L.Settings.ShoppingListHelp),
            () => DrawShoppingList(cfg));
    }

    private static void DrawAddRow(Configuration cfg)
    {
        EnsureSortedCatalog();
        if (sortedItems is null || sortedItems.Length == 0)
        {
            SettingsRow.Note(Loc.T(L.Settings.NoShopItems), Styling.AccentRose);
            return;
        }

        pickerSelection = Math.Clamp(pickerSelection, 0, sortedItems.Length - 1);
        SettingsControls.DrawSearchableCombo("##tr_item", sortedLabels!, ref pickerSelection, 380f);

        var picked = sortedItems[pickerSelection];
        var listed = IsListed(cfg.TradeList, picked.ItemId);

        ImGui.SameLine();
        var addButtonSize = new Vector2(96f * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        using (ImRaii.Disabled(listed))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
        {
            if (ImGui.Button($"{Loc.T(L.Common.Add)}##tr_add", addButtonSize))
            {
                cfg.TradeList.Add(new TradeListEntry { ItemId = picked.ItemId });
                cfg.SaveDebounced();
                listRowsTickMs = 0;
            }
        }

        if (listed)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            {
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Loc.T(L.Settings.AlreadyListed));
            }
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

    private readonly record struct ListRow(uint ItemId, string Name, Vector4 NameColor, bool IsCollectible, string Status, Vector4 StatusColor, string? Tooltip);

    private const int ListRefreshMs = 1000;
    private const int MaxStopAtCount = 999;
    private const float RowControlWidth = 150f;

    private static readonly HashSet<uint> noSkippedItems = [];
    private static ListRow[] listRows = [];
    private static long listRowsTickMs;
    private static string previewText = "";
    private static Vector4 previewColor;

    private static void DrawShoppingList(Configuration cfg)
    {
        if (cfg.TradeList.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Settings.ShoppingListEmpty), Styling.AccentRose);
            return;
        }

        RefreshListRows(cfg);

        int? moveUp = null, moveDown = null, remove = null;
        var scale = ImGuiHelpers.GlobalScale;
        var buttonSize = ImGui.GetFrameHeight();
        var spacingX = 4f * scale;
        var controlWidth = RowControlWidth * scale;
        var clusterWidth = controlWidth + spacingX * 2 + buttonSize * 3 + spacingX * 2;
        var clusterX = SettingsGroup.InnerRightLocalX() - clusterWidth;
        var count = cfg.TradeList.Count;

        for (var entryIndex = 0; entryIndex < count; entryIndex++)
        {
            var entry = cfg.TradeList[entryIndex];
            var row = listRows[entryIndex];

            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            {
                ImGui.TextUnformatted($"{entryIndex + 1}.");
            }

            ImGui.SameLine();
            var status = row.Status.Length == 0 ? "" : "  " + row.Status;
            var nameWidth = clusterX - spacingX * 2 - ImGui.GetCursorPosX() - ImGui.CalcTextSize(status).X;
            using (ImRaii.PushColor(ImGuiCol.Text, row.NameColor))
            {
                ImGui.TextUnformatted(TextDraw.Truncate(row.Name, Math.Max(0f, nameWidth)));
            }

            if (status.Length > 0)
            {
                ImGui.SameLine(0f, 0f);
                using (ImRaii.PushColor(ImGuiCol.Text, row.StatusColor))
                {
                    ImGui.TextUnformatted(status);
                }

                if (row.Tooltip is not null && ImGui.IsItemHovered())
                {
                    Tooltip.Show(row.Tooltip);
                }
            }

            ImGui.SameLine(clusterX);
            DrawStopControl(cfg, entry, row, entryIndex, controlWidth);

            ImGui.SameLine(clusterX + controlWidth + spacingX * 2);
            if (IconButton.Draw(FontAwesomeIcon.ArrowUp, $"##tr_up_{entryIndex}", buttonSize, tooltip: Loc.T(L.Common.MoveUp), enabled: entryIndex > 0)) moveUp = entryIndex;
            ImGui.SameLine(0f, spacingX);
            if (IconButton.Draw(FontAwesomeIcon.ArrowDown, $"##tr_dn_{entryIndex}", buttonSize, tooltip: Loc.T(L.Common.MoveDown), enabled: entryIndex < count - 1)) moveDown = entryIndex;
            ImGui.SameLine(0f, spacingX);
            if (IconButton.Draw(FontAwesomeIcon.Times, $"##tr_rm_{entryIndex}", buttonSize, Styling.AccentRose, Loc.T(L.Common.Remove))) remove = entryIndex;
        }

        if (ListReorder.Apply(cfg.TradeList, count, moveUp, moveDown, remove))
        {
            cfg.SaveDebounced();
            listRowsTickMs = 0;
        }
    }

    private static void DrawStopControl(Configuration cfg, TradeListEntry entry, ListRow row, int entryIndex, float width)
    {
        if (row.IsCollectible)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            {
                ImGui.TextUnformatted(Loc.T(L.Settings.BuyOnce));
            }

            return;
        }

        var stopAt = entry.StopAtCount;
        ImGui.SetNextItemWidth(width);
        using (SettingsControls.PushFrameColors())
        {
            if (ImGui.SliderInt($"##tr_stop_{entryIndex}", ref stopAt, 0, MaxStopAtCount,
                    stopAt == 0 ? Loc.T(L.Settings.KeepBuying) : Loc.T(L.Settings.StopAtCount)))
            {
                entry.StopAtCount = Math.Clamp(stopAt, 0, MaxStopAtCount);
                cfg.SaveDebounced();
                listRowsTickMs = 0;
            }
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
        (previewText, previewColor) = BuildPreview(cfg);
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
            return new ListRow(entry.ItemId, Loc.T(L.Settings.UnknownTradeItem, entry.ItemId), Styling.AccentRose, false, "", Styling.TextMuted, null);
        }

        var name = Loc.T(L.Settings.ItemCostLabel, item.ItemName, item.CostPerOne);
        if (skipped.Contains(item.ItemId))
        {
            return new ListRow(item.ItemId, name, Styling.TextStrong, item.IsCollectible, Loc.T(L.Settings.StatusSkipped), Styling.AccentAmber, null);
        }

        return state switch
        {
            TradeEntryState.Learned => new ListRow(item.ItemId, name, Styling.TextStrong, true, Loc.T(L.Settings.StatusLearned), Styling.AccentMint, null),
            TradeEntryState.Held => new ListRow(item.ItemId, name, Styling.TextStrong, true, Loc.T(L.Settings.StatusHeld), Styling.AccentMint, null),
            TradeEntryState.Stocked => new ListRow(item.ItemId, name, Styling.TextStrong, false, Loc.T(L.Settings.StatusHaveOf, held, entry.StopAtCount), Styling.AccentMint, null),
            TradeEntryState.NoTrader => new ListRow(item.ItemId, name, Styling.TextStrong, item.IsCollectible, Loc.T(L.Settings.StatusNoTrader), Styling.AccentRose, BuildReachNote(item)),
            _ when item.IsCollectible => new ListRow(item.ItemId, name, Styling.TextStrong, true, "", Styling.TextMuted, null),
            _ when entry.StopAtCount > 0 => new ListRow(item.ItemId, name, Styling.TextStrong, false, Loc.T(L.Settings.StatusHaveOf, held, entry.StopAtCount), Styling.TextMuted, null),
            _ => new ListRow(item.ItemId, name, Styling.TextStrong, false, Loc.T(L.Settings.StatusHave, held), Styling.TextMuted, null),
        };
    }

    private static uint reachNoteItemId;
    private static long reachNoteTickMs;
    private static string? reachNote;
    private const int ReachRecheckMs = 2000;

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

        SettingsRow.Note(reachNote, Styling.AccentRose);
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

        sortedItems = [.. catalog.OrderBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase)];
        sortedLabels = new string[sortedItems.Length];
        for (var itemIndex = 0; itemIndex < sortedItems.Length; itemIndex++)
        {
            var item = sortedItems[itemIndex];
            sortedLabels[itemIndex] = Loc.T(L.Settings.ItemCostLabel, item.ItemName, item.CostPerOne);
        }
    }

    private static void DrawSpendGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GemsSpend));

        var selected = Math.Max(0, Array.IndexOf(spendModes, cfg.SpendMode));
        SettingsRow.Draw(Loc.T(L.Settings.SpendStrategy),
            Loc.T(L.Settings.SpendStrategyHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##tr_spend_mode", spendModeChoices, selected, choice =>
            {
                cfg.SpendMode = spendModes[choice];
                cfg.SaveDebounced();
                listRowsTickMs = 0;
            }));
        SettingsRow.Caption(Loc.T(spendModeChoices[selected].Detail));

        if (cfg.SpendMode == GemstoneSpendMode.SpendGems)
        {
            SettingsRow.Draw(Loc.T(L.Settings.SpendUpTo),
                Loc.T(L.Settings.SpendUpToHelp),
                SettingsControls.RowSliderWidth,
                () => SettingsControls.DrawIntSlider(cfg, "##tr_spend_gems",
                    () => cfg.SpendGemsAmount, v => cfg.SpendGemsAmount = Math.Clamp(v, 50, Core.AfgConstants.BicolorCap),
                    50, Core.AfgConstants.BicolorCap, Loc.T(L.Settings.GemsFormat)));
        }
        else if (cfg.SpendMode == GemstoneSpendMode.BuyQuantity)
        {
            SettingsRow.Draw(Loc.T(L.Settings.BuyQuantity),
                Loc.T(L.Settings.BuyQuantityHelp),
                SettingsControls.RowSliderWidth,
                () => SettingsControls.DrawIntSlider(cfg, "##tr_buy_qty",
                    () => cfg.BuyQuantityAmount, v => cfg.BuyQuantityAmount = Math.Clamp(v, 1, 99),
                    1, 99, Loc.T(L.Settings.BuyQuantityFormat)));
        }

        SettingsRow.Draw(Loc.T(L.Settings.Reserve),
            Loc.T(L.Settings.ReserveHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##tr_reserve",
                () => cfg.KeepGemstonesReserve, v => cfg.KeepGemstonesReserve = Math.Clamp(v, 0, Core.AfgConstants.BicolorCap),
                0, Core.AfgConstants.BicolorCap, Loc.T(L.Settings.GemsFormat)));

        DrawSpendPreview(cfg);
    }

    private static void DrawAfterGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GemsAfter));

        var selected = cfg.AfterTrade == AfterTradeAction.Stop ? 1 : 0;
        SettingsRow.Draw(Loc.T(L.Settings.WhenDone),
            Loc.T(L.Settings.WhenDoneHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##tr_after", afterTradeChoices, selected, choice =>
            {
                cfg.AfterTrade = choice == 1 ? AfterTradeAction.Stop : AfterTradeAction.Resume;
                cfg.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(afterTradeChoices[selected].Detail));
    }

    private static void DrawSpendPreview(Configuration cfg)
    {
        if (cfg.TradeList.Count == 0)
        {
            return;
        }

        RefreshListRows(cfg);
        SettingsRow.Note(previewText, previewColor);
    }

    // Replays the shop's budget split at the threshold wallet, trader by trader, so the note matches what the next
    // trade would buy.
    private static (string Text, Vector4 Color) BuildPreview(Configuration cfg)
    {
        var plannedItemIds = new HashSet<uint>();
        var plan = TradeList.Plan(cfg.TradeThreshold, null, null, plannedItemIds);
        if (plan is null)
        {
            return (Loc.T(L.Settings.PreviewNothing, cfg.TradeThreshold, cfg.KeepGemstonesReserve), Styling.AccentRose);
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

        return (Loc.T(L.Settings.PreviewPlan, cfg.TradeThreshold, cfg.KeepGemstonesReserve, string.Join(", ", parts), spent), Styling.TextMuted);
    }
}
