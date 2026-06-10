using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Windows.Components;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class GemstoneSettings
{
    private static readonly GemstoneSpendMode[] spendModes =
        [GemstoneSpendMode.SpendAll, GemstoneSpendMode.SpendGems, GemstoneSpendMode.BuyQuantity];

    private static readonly SettingsControls.Choices.Choice[] spendModeChoices =
    [
        new("Spend all gemstones", "Spend everything above the reserve on each trade."),
        new("Spend up to a set amount", "Cap how many gems each trade is allowed to spend."),
        new("Buy a fixed number", "Buy a set quantity of the item on each trade."),
    ];

    private static readonly SettingsControls.Choices.Choice[] afterTradeChoices =
    [
        new("Resume the grind", "Keep grinding FATEs in the same zone after the buy."),
        new("Stop the run", "End the run once the buy succeeds."),
    ];

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
        using var group = SettingsGroup.Begin("Trade trigger");

        SettingsRow.Draw("Auto-trade at threshold",
            "When your Bicolor Gemstone inventory reaches the threshold below, the plugin teleports to a trader and buys the item.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.TradeOnCap, v => cfg.TradeOnCap = v, "##tr_oncap"),
            SettingsRow.ToggleHeight);

        if (!cfg.TradeOnCap)
        {
            SettingsRow.Note("Auto-trade is off. Enable it to configure the trade.");
            return;
        }

        if (ExternalPlugins.IsInstalledButDisabled(ExternalPlugin.TextAdvance))
        {
            SettingsRow.Note(
                "TextAdvance is installed but disabled. Auto-trade may stall at the trader's "
                + "dialogue; turn on TextAdvance's \"Enable plugin\" toggle for reliable trading.",
                Styling.AccentAmber);
        }

        SettingsRow.Draw("Trade threshold",
            "Gem count that triggers the trade. Game cap is 1500. Lower values trade more often so fewer FATEs are wasted near cap.",
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##tr_threshold",
                () => cfg.TradeThreshold, v => cfg.TradeThreshold = Math.Clamp(v, 100, Core.AfgConstants.BicolorCap),
                100, Core.AfgConstants.BicolorCap, "%d gems"));
    }

    private static void DrawItemGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("What to buy");

        SettingsRow.DrawBlock("Item to buy",
            "Pulled live from game data. Cost shown in gems per one.",
            () =>
            {
                var catalog = GemstoneCatalog.All;
                if (catalog.Length == 0)
                {
                    SettingsRow.Note("No gem-shop items found.", Styling.AccentRose);
                    return;
                }

                var effectiveId = GemstoneCatalog.EnsurePersistedTarget();
                var idx = Array.FindIndex(catalog, i => i.ItemId == effectiveId);
                if (idx < 0) idx = 0;
                var labels = catalog.Select(i => $"{i.ItemName}  ({i.CostPerOne}g)").ToArray();
                if (SettingsControls.DrawPlainCombo("##tr_item", ref idx, labels, 380f))
                { cfg.TargetTradeItemId = catalog[idx].ItemId; cfg.SaveDebounced(); }
            });
    }

    private static void DrawSpendGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("How much to spend");

        var selected = Math.Max(0, Array.IndexOf(spendModes, cfg.SpendMode));
        SettingsRow.Draw("Spend strategy",
            "How much each trade spends when it fires.",
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##tr_spend_mode", spendModeChoices, selected, choice =>
            {
                cfg.SpendMode = spendModes[choice];
                cfg.SaveDebounced();
            }));
        SettingsRow.Caption(spendModeChoices[selected].Detail);

        if (cfg.SpendMode == GemstoneSpendMode.SpendGems)
        {
            SettingsRow.Draw("Spend up to",
                "Maximum gems spent per trade.",
                SettingsControls.RowSliderWidth,
                () => SettingsControls.DrawIntSlider(cfg, "##tr_spend_gems",
                    () => cfg.SpendGemsAmount, v => cfg.SpendGemsAmount = Math.Clamp(v, 50, Core.AfgConstants.BicolorCap),
                    50, Core.AfgConstants.BicolorCap, "%d gems"));
        }
        else if (cfg.SpendMode == GemstoneSpendMode.BuyQuantity)
        {
            SettingsRow.Draw("Buy quantity",
                "How many of the item to buy per trade.",
                SettingsControls.RowSliderWidth,
                () => SettingsControls.DrawIntSlider(cfg, "##tr_buy_qty",
                    () => cfg.BuyQuantityAmount, v => cfg.BuyQuantityAmount = Math.Clamp(v, 1, 99),
                    1, 99, "%d x item"));
        }

        SettingsRow.Draw("Keep in reserve",
            "Gems left untouched on every trade. Use this when you want to save toward a pricier item without turning auto-trade off.",
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##tr_reserve",
                () => cfg.KeepGemstonesReserve, v => cfg.KeepGemstonesReserve = Math.Clamp(v, 0, Core.AfgConstants.BicolorCap),
                0, Core.AfgConstants.BicolorCap, "%d gems"));

        DrawSpendPreview(cfg);
    }

    private static void DrawAfterGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("After the trade");

        var selected = cfg.AfterTrade == AfterTradeAction.Stop ? 1 : 0;
        SettingsRow.Draw("When done",
            "What to do once the buy succeeds.",
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##tr_after", afterTradeChoices, selected, choice =>
            {
                cfg.AfterTrade = choice == 1 ? AfterTradeAction.Stop : AfterTradeAction.Resume;
                cfg.SaveDebounced();
            }));
        SettingsRow.Caption(afterTradeChoices[selected].Detail);
    }

    private static void DrawSpendPreview(Configuration cfg)
    {
        var item = GemstoneCatalog.FindById(cfg.TargetTradeItemId);
        if (item is null) return;

        var qty = GemstoneCatalog.ComputeBuyQuantity(cfg.TradeThreshold, item.CostPerOne);

        var color = qty <= 0 ? Styling.AccentRose : Styling.TextMuted;
        SettingsRow.Note(qty <= 0
            ? $"Threshold/reserve won't afford any {item.ItemName} at {item.CostPerOne}g each."
            : $"At threshold {cfg.TradeThreshold}g (keeping {cfg.KeepGemstonesReserve}g), next trade buys ~{qty} x {item.ItemName} for {qty * item.CostPerOne}g.",
            color);
    }
}
