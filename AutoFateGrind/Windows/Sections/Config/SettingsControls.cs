using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class SettingsControls
{
    public const float ToggleWidth = 40f;
    public const float RowSliderWidth = 180f;
    public const float RowComboWidth = 170f;

    private const float RangeDragWidth = 62f;
    private const float RangeDashSlot = 14f;
    private const float RangeDragSpeed = 0.25f;

    private const float SearchComboMaxHeight = 360f;
    private const int SearchComboFilterMaxLength = 64;

    private static readonly Dictionary<string, string> searchComboFilters = new();

    public static float RangeInlineWidth()
        => RangeDragWidth * 2f + RangeDashSlot;

    public static void DrawToggle(Configuration cfg, Func<bool> getter, Action<bool> setter, string id)
    {
        var value = getter();
        if (ToggleSwitch.Draw(id, ref value))
        {
            setter(value);
            cfg.SaveDebounced();
        }
    }

    public static void DrawIntSlider(Configuration cfg, string id, Func<int> getter, Action<int> setter,
        int minimum, int maximum, string format = "%d", float width = RowSliderWidth)
    {
        var value = getter();
        ImGui.SetNextItemWidth(width * ImGuiHelpers.GlobalScale);
        using (PushFrameColors())
        {
            if (ImGui.SliderInt(id, ref value, minimum, maximum, format))
            {
                setter(Math.Clamp(value, minimum, maximum));
                cfg.SaveDebounced();
            }
        }
    }

    public static bool DrawPlainCombo(string id, ref int index, string[] labels, float width)
    {
        ImGui.SetNextItemWidth(width * ImGuiHelpers.GlobalScale);
        using (PushFrameColors())
        {
            return ImGui.Combo(id, ref index, labels, labels.Length);
        }
    }

    public static void DrawLanguageCombo(Configuration cfg, float width = RowComboWidth)
    {
        ImGui.SetNextItemWidth(width * ImGuiHelpers.GlobalScale);
        using var frameColors = PushFrameColors();
        using var combo = ImRaii.Combo("##afg_language", Loc.Current.NativeName);
        if (!combo) return;

        var languages = Languages.All;
        for (var index = 0; index < languages.Length; index++)
        {
            var language = languages[index];
            var selected = ReferenceEquals(language, Loc.Current);
            if (ImGui.Selectable(language.NativeName, selected) && !selected) ApplyLanguage(cfg, language.Code);
        }
    }

    private static void ApplyLanguage(Configuration cfg, string code)
    {
        cfg.Language = code;
        cfg.Save();
        Loc.SetLanguage(code);
        Fonts.OnLanguageChanged();
        Plugin.Instance.OnLanguageChanged();
    }

    public static bool DrawSearchableCombo(string id, string preview, string[] labels, ref int selectedIndex,
        float width, string? hint = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextItemWidth(width * scale);
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(width * scale, 0f),
            new Vector2(width * scale, SearchComboMaxHeight * scale));

        using var frameColors = PushFrameColors();
        using var combo = ImRaii.Combo(id, preview);
        if (!combo)
        {
            return false;
        }

        if (!searchComboFilters.TryGetValue(id, out var filter))
        {
            filter = string.Empty;
        }

        if (ImGui.IsWindowAppearing())
        {
            filter = string.Empty;
            ImGui.SetKeyboardFocusHere(0);
        }

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint($"{id}_search", hint ?? Loc.T(L.Settings.SearchHint), ref filter, SearchComboFilterMaxLength);
        searchComboFilters[id] = filter;

        var changed = false;
        for (var labelIndex = 0; labelIndex < labels.Length; labelIndex++)
        {
            if (filter.Length > 0 && labels[labelIndex].IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (ImGui.Selectable(labels[labelIndex], labelIndex == selectedIndex))
            {
                selectedIndex = labelIndex;
                changed = true;
            }
        }

        return changed;
    }

    public static void DrawRangeInline(Configuration cfg, string minId, string maxId,
        Func<int> getMin, Action<int> setMin, Func<int> getMax, Action<int> setMax,
        int maxValue, int minValue = 0, string format = "%d s")
    {
        using var colors = PushFrameColors();

        DrawRangeBound(cfg, minId, getMin, setMin, minValue, maxValue, format,
            onChanged: value => { if (value > getMax()) setMax(value); });

        DrawRangeDash();

        DrawRangeBound(cfg, maxId, getMax, setMax, minValue, maxValue, format,
            onChanged: value => { if (value < getMin()) setMin(value); });
    }

    private static void DrawRangeBound(Configuration cfg, string id, Func<int> getter, Action<int> setter,
        int minValue, int maxValue, string format, Action<int> onChanged)
    {
        var value = getter();
        ImGui.SetNextItemWidth(RangeDragWidth * ImGuiHelpers.GlobalScale);
        if (!ImGui.DragInt(id, ref value, RangeDragSpeed, minValue, maxValue, format))
        {
            return;
        }

        value = Math.Clamp(value, minValue, maxValue);
        setter(value);
        onChanged(value);
        cfg.SaveDebounced();
    }

    private static void DrawRangeDash()
    {
        var dashSlot = RangeDashSlot * ImGuiHelpers.GlobalScale;

        ImGui.SameLine(0f, 0f);
        var slotOrigin = ImGui.GetCursorScreenPos();
        var dashSize = ImGui.CalcTextSize("-");
        ImGui.SetCursorScreenPos(slotOrigin + new Vector2((dashSlot - dashSize.X) * 0.5f, (ImGui.GetFrameHeight() - dashSize.Y) * 0.5f));
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
        {
            ImGui.TextUnformatted("-");
        }

        ImGui.SameLine(0f, 0f);
        ImGui.SetCursorScreenPos(slotOrigin with { X = slotOrigin.X + dashSlot });
    }

    public static IDisposable PushFrameColors()
        => ImRaii.PushColor(ImGuiCol.SliderGrab, Styling.AccentViolet)
            .Push(ImGuiCol.SliderGrabActive, Styling.AccentVioletSoft)
            .Push(ImGuiCol.FrameBg, Styling.SliderBg)
            .Push(ImGuiCol.FrameBgHovered, Styling.CardBgHover)
            .Push(ImGuiCol.FrameBgActive, Styling.CardBgHover);

    internal static class Choices
    {
        public readonly record struct Choice(LocString Name, LocString Detail);

        private const float PopupWidth = 320f;
        private const float ItemPaddingX = 6f;
        private const float ItemPaddingY = 5f;
        private const float NameDetailGap = 2f;

        public static void DrawCombo(string id, Choice[] options, int selected, Action<int> onSelect,
            float width = RowComboWidth)
        {
            var scale = ImGuiHelpers.GlobalScale;
            ImGui.SetNextItemWidth(width * scale);
            ImGui.SetNextWindowSizeConstraints(new Vector2(PopupWidth * scale, 0f), new Vector2(PopupWidth * scale, 600f * scale));

            using var frameColors = PushFrameColors();
            using var combo = ImRaii.Combo(id, Loc.T(options[selected].Name));
            if (!combo)
            {
                return;
            }

            for (var optionIndex = 0; optionIndex < options.Length; optionIndex++)
            {
                if (DrawItem(id, options[optionIndex], optionIndex, optionIndex == selected))
                {
                    onSelect(optionIndex);
                }
            }
        }

        private static bool DrawItem(string comboId, Choice option, int optionIndex, bool selected)
        {
            var scale = ImGuiHelpers.GlobalScale;
            var paddingX = ItemPaddingX * scale;
            var paddingY = ItemPaddingY * scale;
            var nameDetailGap = NameDetailGap * scale;
            var lineHeight = ImGui.GetTextLineHeight();
            var wrapWidth = ImGui.GetContentRegionAvail().X - paddingX * 2f;
            var name = Loc.T(option.Name);
            var detail = Loc.T(option.Detail);

            var detailSize = ImGui.CalcTextSize(detail, false, wrapWidth);
            var itemHeight = paddingY * 2f + lineHeight + nameDetailGap + detailSize.Y;

            var itemOrigin = ImGui.GetCursorScreenPos();
            var clicked = ImGui.Selectable($"##{comboId}_opt{optionIndex}", selected,
                ImGuiSelectableFlags.None, new Vector2(0f, itemHeight));

            var drawList = ImGui.GetWindowDrawList();
            var nameColor = selected ? Styling.AccentVioletSoft : Styling.TextStrong;
            drawList.AddText(itemOrigin + new Vector2(paddingX, paddingY), ImGui.GetColorU32(nameColor), name);
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                itemOrigin + new Vector2(paddingX, paddingY + lineHeight + nameDetailGap),
                ImGui.GetColorU32(Styling.TextMuted), detail, wrapWidth);

            return clicked;
        }
    }
}
