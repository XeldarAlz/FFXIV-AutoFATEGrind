using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class GemstoneSettings
{
    public static void Draw(Configuration cfg)
    {
        SettingsRow.Draw("Auto-trade when at threshold",
            "When your Bicolor Gemstone inventory reaches the threshold below, the plugin teleports to a trader and buys the item.",
            () => SettingsControls.DrawToggle(cfg, () => cfg.TradeOnCap, v => cfg.TradeOnCap = v, "##tr_oncap"));

        if (!cfg.TradeOnCap)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("Auto-trade is off. Enable the toggle above to configure the trade.");
            return;
        }

        if (ExternalPlugins.IsInstalledButDisabled(ExternalPlugin.TextAdvance))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmber))
                ImGui.TextWrapped(
                    "TextAdvance is installed but disabled. Auto-trade may stall at the trader's "
                    + "dialogue — turn on TextAdvance's \"Enable plugin\" toggle for reliable trading.");
            ImGui.Spacing();
        }

        SettingsRow.Draw("Trade threshold",
            "Gem count that triggers the trade. Game cap is 1500. Lower values trade more often so fewer FATEs are wasted near cap.",
            () =>
            {
                var v = cfg.TradeThreshold;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##tr_threshold", ref v, 100, 1500, "%d gems"))
                { cfg.TradeThreshold = Math.Clamp(v, 100, 1500); cfg.SaveDebounced(); }
            });

        SettingsRow.Draw("Item to buy", "Pulled live from game data. Cost shown in gems per one.", () =>
        {
            var catalog = GemstoneCatalog.All;
            if (catalog.Length == 0)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                    ImGui.TextUnformatted("No gem-shop items found.");
                return;
            }
            var effectiveId = GemstoneCatalog.EnsurePersistedTarget();
            var idx = Array.FindIndex(catalog, i => i.ItemId == effectiveId);
            if (idx < 0) idx = 0;
            var labels = catalog.Select(i => $"{i.ItemName}  ({i.CostPerOne}g)").ToArray();
            ImGui.SetNextItemWidth(420);
            if (ImGui.Combo("##tr_item", ref idx, labels, labels.Length))
            { cfg.TargetTradeItemId = catalog[idx].ItemId; cfg.SaveDebounced(); }
        });

        SettingsRow.Draw("How much to spend",
            "Choose the strategy used when the trade fires.", () =>
            {
                if (ImGui.RadioButton("Spend all gemstones (minus reserve)", cfg.SpendMode == GemstoneSpendMode.SpendAll))
                { cfg.SpendMode = GemstoneSpendMode.SpendAll; cfg.SaveDebounced(); }

                if (ImGui.RadioButton("Spend up to a set amount of gems", cfg.SpendMode == GemstoneSpendMode.SpendGems))
                { cfg.SpendMode = GemstoneSpendMode.SpendGems; cfg.SaveDebounced(); }

                if (cfg.SpendMode == GemstoneSpendMode.SpendGems)
                {
                    ImGui.Indent(20f);
                    var g = cfg.SpendGemsAmount;
                    ImGui.SetNextItemWidth(220);
                    if (ImGui.SliderInt("##tr_spend_gems", ref g, 50, 1500, "%d gems / trade"))
                    { cfg.SpendGemsAmount = Math.Clamp(g, 50, 1500); cfg.SaveDebounced(); }
                    ImGui.Unindent(20f);
                }

                if (ImGui.RadioButton("Buy a fixed number of the item", cfg.SpendMode == GemstoneSpendMode.BuyQuantity))
                { cfg.SpendMode = GemstoneSpendMode.BuyQuantity; cfg.SaveDebounced(); }

                if (cfg.SpendMode == GemstoneSpendMode.BuyQuantity)
                {
                    ImGui.Indent(20f);
                    var n = cfg.BuyQuantityAmount;
                    ImGui.SetNextItemWidth(220);
                    if (ImGui.SliderInt("##tr_buy_qty", ref n, 1, 99, "%d × item"))
                    { cfg.BuyQuantityAmount = Math.Clamp(n, 1, 99); cfg.SaveDebounced(); }
                    ImGui.Unindent(20f);
                }
            });

        SettingsRow.Draw("Keep in reserve",
            "Gems left untouched on every trade. Use this when you want to save toward a pricier item without turning auto-trade off.",
            () =>
            {
                var v = cfg.KeepGemstonesReserve;
                ImGui.SetNextItemWidth(280);
                if (ImGui.SliderInt("##tr_reserve", ref v, 0, 1500, "%d gems"))
                { cfg.KeepGemstonesReserve = Math.Clamp(v, 0, 1500); cfg.SaveDebounced(); }
            });

        DrawSpendPreview(cfg);

        SettingsRow.Draw("After the trade",
            "What to do once the buy succeeds.", () =>
            {
                var resume = cfg.AfterTrade == AfterTradeAction.Resume;
                if (ImGui.RadioButton("Resume FATE grind in the same zone", resume))
                { cfg.AfterTrade = AfterTradeAction.Resume; cfg.SaveDebounced(); }
                if (ImGui.RadioButton("Stop the run", !resume))
                { cfg.AfterTrade = AfterTradeAction.Stop; cfg.SaveDebounced(); }
            });
    }

    private static void DrawSpendPreview(Configuration cfg)
    {
        var item = GemstoneCatalog.FindById(cfg.TargetTradeItemId);
        if (item is null) return;

        var qty = GemstoneCatalog.ComputeBuyQuantity(cfg.TradeThreshold, item.CostPerOne);

        var color = qty <= 0 ? Styling.AccentRose : Styling.TextMuted;
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextWrapped(qty <= 0
                ? $"Threshold/reserve won't afford any {item.ItemName} at {item.CostPerOne}g each."
                : $"At threshold {cfg.TradeThreshold}g (keeping {cfg.KeepGemstonesReserve}g), next trade buys ~{qty} × {item.ItemName} for {qty * item.CostPerOne}g.");
        ImGui.Spacing();
    }
}
