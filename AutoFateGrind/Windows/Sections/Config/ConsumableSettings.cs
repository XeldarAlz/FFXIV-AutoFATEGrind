using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class ConsumableSettings
{
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
        using var group = SettingsGroup.Begin("Consumables");

        SettingsRow.Draw("Auto-consume food & medicine",
            "Use food and medicine between FATEs to keep their buffs up; Well Fed alone is a free +3% EXP. Items are consumed only when out of combat, and refreshed before the buff runs out.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoConsume, v => cfg.AutoConsume = v, "##con_on"),
            SettingsRow.ToggleHeight);

        if (!cfg.AutoConsume)
        {
            SettingsRow.Note("Auto-consume is off. Enable it to pick items.");
            return;
        }

        SettingsRow.Draw("Refresh when under",
            "Re-consume once the buff has fewer than this many minutes left. 0 only re-applies after it fully wears off. Food and medicine last 30 minutes.",
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##con_min",
                () => cfg.AutoConsumeMinMinutes, v => cfg.AutoConsumeMinMinutes = Math.Clamp(v, 0, 29),
                0, 29, cfg.AutoConsumeMinMinutes == 0 ? "only when worn off" : "%d min left"));
    }

    private static void DrawItemsGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Items");

        SettingsRow.DrawBlock("Add an item",
            "Pick from the food and medicine in your bag. HQ is used automatically when you have it.",
            () => DrawAddConsumableRow(cfg));

        SettingsRow.DrawBlock("Active items",
            "Each is kept active in order; the next available one is consumed if the first runs out.",
            () => DrawConsumableList(cfg));
    }

    private static void DrawAddConsumableRow(Configuration cfg)
    {
        // Only items currently in the bag — you use one or two per session, so the full game list is
        // noise. Runtime still skips a depleted entry (DrawConsumableList flags it red).
        var catalog = FoodOps.Catalog.Where(FoodOps.IsAvailable).ToArray();
        if (catalog.Length == 0)
        {
            SettingsRow.Note("No food or medicine in your bag. Stock some, then add it here.");
            return;
        }

        var queued = cfg.AutoConsumeItems.Select(e => e.ItemId).ToHashSet();
        var labels = catalog.Select(e =>
        {
            var kind = e.StatusId == FoodOps.WellFedStatusId ? "Food" : "Medicine";
            var taken = queued.Contains(e.ItemId) ? "  (added)" : "";
            return $"{e.Name}  [{kind}]{taken}";
        }).ToArray();

        consumablePickerSelection = Math.Clamp(consumablePickerSelection, 0, catalog.Length - 1);
        SettingsControls.DrawPlainCombo("##con_picker", ref consumablePickerSelection, labels, 340f);

        var picked = catalog[consumablePickerSelection];
        var duplicate = queued.Contains(picked.ItemId);

        ImGui.SameLine();
        var addBtnSize = new Vector2(96f * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        using (ImRaii.Disabled(duplicate))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
            if (ImGui.Button("Add##con_add", addBtnSize))
            {
                cfg.AutoConsumeItems.Add(new ConsumableEntry
                {
                    ItemId = picked.ItemId,
                    Name = picked.Name,
                    StatusId = picked.StatusId,
                    CanBeHq = picked.CanBeHq,
                });
                cfg.SaveDebounced();
            }

        if (duplicate)
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            {
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Already added.");
            }
    }

    private static void DrawConsumableList(Configuration cfg)
    {
        if (cfg.AutoConsumeItems.Count == 0)
        {
            SettingsRow.Note("No items added - nothing will be consumed.");
            return;
        }

        int? remove = null;
        var btnSize = ImGui.GetFrameHeight();
        for (var i = 0; i < cfg.AutoConsumeItems.Count; i++)
        {
            var e = cfg.AutoConsumeItems[i];
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted($"{i + 1}.");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                ImGui.TextUnformatted(e.Name);

            ImGui.SameLine();
            var kind = e.StatusId == FoodOps.WellFedStatusId ? "Well Fed" : "Medicated";
            var inBag = FoodOps.IsAvailable(e);
            using (ImRaii.PushColor(ImGuiCol.Text, inBag ? Styling.TextMuted : Styling.AccentRose))
                ImGui.TextUnformatted(inBag ? $"  {kind}" : $"  {kind}, none in bag");

            ImGui.SameLine(SettingsGroup.InnerRightLocalX() - btnSize);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
            using (ImRaii.PushFont(UiBuilder.IconFont))
                if (ImGui.Button(FontAwesomeIcon.Times.ToIconString() + $"##con_rm_{i}", new Vector2(btnSize, btnSize)))
                    remove = i;
        }

        if (remove is int r)
        {
            cfg.AutoConsumeItems.RemoveAt(r);
            cfg.SaveDebounced();
        }
    }
}
