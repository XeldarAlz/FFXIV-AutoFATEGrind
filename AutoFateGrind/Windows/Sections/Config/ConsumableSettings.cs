using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Localization;
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
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.ConsumablesGroup));

        SettingsRow.Draw(Loc.T(L.Settings.AutoConsume),
            Loc.T(L.Settings.AutoConsumeHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoConsume, v => cfg.AutoConsume = v, "##con_on"),
            SettingsRow.ToggleHeight);

        if (!cfg.AutoConsume)
        {
            SettingsRow.Note(Loc.T(L.Settings.AutoConsumeOff));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.RefreshUnder),
            Loc.T(L.Settings.RefreshUnderHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##con_min",
                () => cfg.AutoConsumeMinMinutes, v => cfg.AutoConsumeMinMinutes = Math.Clamp(v, 0, 29),
                0, 29, cfg.AutoConsumeMinMinutes == 0 ? Loc.T(L.Settings.RefreshWornOff) : Loc.T(L.Settings.RefreshFormat)));
    }

    private static void DrawItemsGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.ConsumablesItems));

        SettingsRow.DrawBlock(Loc.T(L.Settings.AddItem),
            Loc.T(L.Settings.AddItemHelp),
            () => DrawAddConsumableRow(cfg));

        SettingsRow.DrawBlock(Loc.T(L.Settings.ActiveItems),
            Loc.T(L.Settings.ActiveItemsHelp),
            () => DrawConsumableList(cfg));
    }

    private static void DrawAddConsumableRow(Configuration cfg)
    {
        // Only items currently in the bag — you use one or two per session, so the full game list is
        // noise. Runtime still skips a depleted entry (DrawConsumableList flags it red).
        var catalog = FoodOps.Catalog.Where(FoodOps.IsAvailable).ToArray();
        if (catalog.Length == 0)
        {
            SettingsRow.Note(Loc.T(L.Settings.NoneInBag));
            return;
        }

        var queued = cfg.AutoConsumeItems.Select(e => e.ItemId).ToHashSet();
        var addedSuffix = "  " + Loc.T(L.Settings.Added);
        var labels = catalog.Select(e =>
        {
            var kind = e.StatusId == FoodOps.WellFedStatusId ? Loc.T(L.Settings.KindFood) : Loc.T(L.Settings.KindMedicine);
            var taken = queued.Contains(e.ItemId) ? addedSuffix : "";
            return Loc.T(L.Settings.ItemLabel, e.Name, kind, taken);
        }).ToArray();

        consumablePickerSelection = Math.Clamp(consumablePickerSelection, 0, catalog.Length - 1);
        SettingsControls.DrawPlainCombo("##con_picker", ref consumablePickerSelection, labels, 340f);

        var picked = catalog[consumablePickerSelection];
        var duplicate = queued.Contains(picked.ItemId);

        ImGui.SameLine();
        var addBtnSize = new Vector2(96f * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        using (ImRaii.Disabled(duplicate))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
            if (ImGui.Button($"{Loc.T(L.Common.Add)}##con_add", addBtnSize))
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
                ImGui.TextUnformatted(Loc.T(L.Settings.AlreadyAdded));
            }
    }

    private static void DrawConsumableList(Configuration cfg)
    {
        if (cfg.AutoConsumeItems.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Settings.NoItemsAdded));
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
            var kind = e.StatusId == FoodOps.WellFedStatusId ? Loc.T(L.Settings.WellFed) : Loc.T(L.Settings.Medicated);
            var inBag = FoodOps.IsAvailable(e);
            using (ImRaii.PushColor(ImGuiCol.Text, inBag ? Styling.TextMuted : Styling.AccentRose))
                ImGui.TextUnformatted("  " + (inBag ? kind : Loc.T(L.Settings.NoneInBagShort, kind)));

            ImGui.SameLine(SettingsGroup.InnerRightLocalX() - btnSize);
            if (IconButton.Draw(FontAwesomeIcon.Times, $"##con_rm_{i}", btnSize, Styling.AccentRose, Loc.T(L.Common.Remove)))
                remove = i;
        }

        if (remove is int r)
        {
            cfg.AutoConsumeItems.RemoveAt(r);
            cfg.SaveDebounced();
        }
    }
}
