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

    private static readonly string[] languageLabels = BuildLanguageLabels();

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
        => Dropdown.Draw(id, labels, ref index, width);

    public static void DrawLanguageCombo(Configuration cfg, float width = RowComboWidth)
    {
        var languages = Languages.All;
        var selected = 0;
        for (var index = 0; index < languages.Length; index++)
        {
            if (ReferenceEquals(languages[index], Loc.Current)) selected = index;
        }

        if (Dropdown.Draw("##afg_language", languageLabels, ref selected, width))
        {
            ApplyLanguage(cfg, languages[selected].Code);
        }
    }

    private static string[] BuildLanguageLabels()
    {
        var languages = Languages.All;
        var labels = new string[languages.Length];
        for (var index = 0; index < languages.Length; index++)
        {
            labels[index] = languages[index].NativeName;
        }

        return labels;
    }

    private static void ApplyLanguage(Configuration cfg, string code)
    {
        cfg.Language = code;
        cfg.Save();
        Loc.SetLanguage(code);
        Fonts.OnLanguageChanged();
        Plugin.Instance.OnLanguageChanged();
    }

    public static bool DrawSearchableCombo(string id, string[] labels, ref int selectedIndex,
        float width, string? hint = null)
        => Dropdown.Draw(id, labels, ref selectedIndex, width, width, true, hint);

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

        private const float PanelWidth = 320f;

        private static string[] names = [];
        private static string[] details = [];

        public static void DrawCombo(string id, Choice[] options, int selected, Action<int> onSelect,
            float width = RowComboWidth)
        {
            Resolve(options);

            var picked = selected;
            if (Dropdown.DrawDetailed(id, names.AsSpan(0, options.Length), details.AsSpan(0, options.Length),
                ref picked, width, PanelWidth))
            {
                onSelect(picked);
            }
        }

        // The catalog hands back cached strings, so refilling shared buffers keeps the per frame
        // translation of a choice list allocation free.
        private static void Resolve(Choice[] options)
        {
            if (names.Length < options.Length)
            {
                names = new string[options.Length];
                details = new string[options.Length];
            }

            for (var index = 0; index < options.Length; index++)
            {
                names[index] = Loc.T(options[index].Name);
                details[index] = Loc.T(options[index].Detail);
            }
        }
    }
}
