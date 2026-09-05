using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Windows.Components;

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

        if (ExternalPlugins.IsInstalledButDisabled(ExternalPlugin.TextAdvance))
        {
            SettingsRow.Note(Loc.T(L.Settings.TradeTextAdvanceNote), Styling.AccentAmber);
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

    private static void DrawItemGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GemsItem));

        SettingsRow.DrawBlock(Loc.T(L.Settings.ItemToBuy),
            Loc.T(L.Settings.ItemToBuyHelp),
            () =>
            {
                EnsureSortedCatalog();
                if (sortedItems is null || sortedItems.Length == 0)
                {
                    SettingsRow.Note(Loc.T(L.Settings.NoShopItems), Styling.AccentRose);
                    return;
                }

                var effectiveId = GemstoneCatalog.EnsurePersistedTarget();
                var selectedIndex = Array.FindIndex(sortedItems, i => i.ItemId == effectiveId);
                if (selectedIndex < 0)
                {
                    selectedIndex = 0;
                }

                if (SettingsControls.DrawSearchableCombo("##tr_item", sortedLabels!, ref selectedIndex, 380f))
                {
                    cfg.TargetTradeItemId = sortedItems[selectedIndex].ItemId;
                    cfg.SaveDebounced();
                }

                DrawTraderReach(sortedItems[selectedIndex]);
            });
    }

    private static uint reachNoteItemId;
    private static long reachNoteTickMs;
    private static string? reachNote;
    private const int ReachRecheckMs = 2000;

    // The dropdown carries every Bicolor item in game data, including ones only sold in expansions the
    // character has not reached, and picking one used to fail mid-run at the teleport (issue #54).
    // Resolving a seller walks shop rows and reads Excel, so the answer is cached rather than rebuilt
    // every frame; the recheck picks up an attunement (or a language switch) made while the page is open.
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

    // Catalog order is cost-ascending (the default-target picker relies on that). The dropdown wants A-Z
    // for scanning, so keep a name-sorted view cached alongside its labels; rebuilt only if the catalog
    // populates or changes length after game data loads.
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
        var item = GemstoneCatalog.FindById(cfg.TargetTradeItemId);
        if (item is null) return;

        var qty = GemstoneCatalog.ComputeBuyQuantity(cfg.TradeThreshold, item.CostPerOne);

        var color = qty <= 0 ? Styling.AccentRose : Styling.TextMuted;
        SettingsRow.Note(qty <= 0
            ? Loc.T(L.Settings.PreviewCannotAfford, item.ItemName, item.CostPerOne)
            : Loc.T(L.Settings.PreviewBuy, cfg.TradeThreshold, cfg.KeepGemstonesReserve, qty, item.ItemName, qty * item.CostPerOne),
            color);
    }
}
