using AutoFateGrind.Core.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Glasswork;
using Glasswork.Backend;
using System.Numerics;
using System.Text;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class SettingsControls
{
    public const float RowSliderWidth = 180f;
    public const float RowComboWidth = 260f;
    public const float RowFieldWidth = 260f;
    public const float RangeSliderWidth = 110f;
    public const float RangeComboWidth = 110f;
    public const float FilterWidth = 200f;
    public const float PickerWidth = 360f;

    public static readonly string SearchGlyph = FontAwesomeIcon.Search.ToIconString();
    public static readonly string ArrowUpGlyph = FontAwesomeIcon.ArrowUp.ToIconString();
    public static readonly string ArrowDownGlyph = FontAwesomeIcon.ArrowDown.ToIconString();
    public static readonly string RemoveGlyph = FontAwesomeIcon.Times.ToIconString();
    public static readonly string SortDescendingGlyph = FontAwesomeIcon.SortAmountDown.ToIconString();
    public static readonly string SortAscendingGlyph = FontAwesomeIcon.SortAmountUp.ToIconString();

    private static readonly string[] languageLabels = BuildLanguageLabels();
    private static readonly Dictionary<string, string> readoutFormats = new(StringComparer.Ordinal);

    public enum ListAction
    {
        None,
        MoveUp,
        MoveDown,
        Remove,
    }

    public static void TitleRow(LocString title, LocString help)
    {
        Gw.BeginRow(Loc.T(title));
        Gw.Tooltip(Loc.T(help));
        Gw.EndRow();
    }

    public static bool ToggleRow(LocString title, LocString help, ref bool value)
    {
        Gw.BeginRow(Loc.T(title));
        Gw.Tooltip(Loc.T(help));
        var changed = Gw.Toggle(ref value, "##value"u8);
        Gw.EndRow();
        return changed;
    }

    public static bool SliderRow(LocString title, LocString help, ref int value, int minimum, int maximum, string format)
    {
        Gw.BeginRow(Loc.T(title));
        Gw.Tooltip(Loc.T(help));
        var changed = Slider("##value"u8, ref value, minimum, maximum, format, RowSliderWidth);
        Gw.EndRow();
        return changed;
    }

    public static bool RangeRow(LocString title, LocString help, ref int lower, ref int upper, int minimum, int maximum, string format)
    {
        Gw.BeginRow(Loc.T(title));
        Gw.Tooltip(Loc.T(help));
        var changed = false;
        if (Slider("##lower"u8, ref lower, minimum, maximum, format, RangeSliderWidth))
        {
            upper = Math.Max(upper, lower);
            changed = true;
        }

        ImGui.SameLine();
        if (Slider("##upper"u8, ref upper, minimum, maximum, format, RangeSliderWidth))
        {
            lower = Math.Min(lower, upper);
            changed = true;
        }

        Gw.EndRow();
        return changed;
    }

    public static bool ComboRow(LocString title, LocString help, ref int selected, ReadOnlySpan<string> items)
    {
        Gw.BeginRow(Loc.T(title));
        Gw.Tooltip(Loc.T(help));
        var changed = ComboBox(ref selected, items, RowComboWidth);
        Gw.EndRow();
        return changed;
    }

    public static bool ComboRangeRow(LocString title, LocString help, ref int lower, ref int upper, ReadOnlySpan<string> labels)
    {
        Gw.BeginRow(Loc.T(title));
        Gw.Tooltip(Loc.T(help));
        var changed = false;
        ImGui.PushID("lower"u8);
        if (ComboBox(ref lower, labels, RangeComboWidth))
        {
            upper = Math.Max(upper, lower);
            changed = true;
        }

        ImGui.PopID();
        RangeDash();
        ImGui.PushID("upper"u8);
        if (ComboBox(ref upper, labels, RangeComboWidth))
        {
            lower = Math.Min(lower, upper);
            changed = true;
        }

        ImGui.PopID();
        Gw.EndRow();
        return changed;
    }

    public static bool TextFieldRow(LocString title, LocString help, ref string text, int maxLength, string hint = "")
    {
        Gw.BeginRow(Loc.T(title));
        Gw.Tooltip(Loc.T(help));
        var changed = TextField(ref text, maxLength, hint, RowFieldWidth);
        Gw.EndRow();
        return changed;
    }

    // Gw.Slider formats its readout with a .NET numeric format, so the printf templates the catalogs carry are
    // rewritten once each: %d becomes the digit placeholder and everything else is quoted as literal text.
    public static bool Slider(ReadOnlySpan<byte> id, ref int value, int minimum, int maximum, string format, float width, string? zeroText = null)
    {
        ImGui.SetNextItemWidth(width * Frame.Scale);
        var floating = (float)value;
        var changed = Gw.Slider(ref floating, minimum, maximum, id, 1f, ReadoutFormat(format, zeroText));
        value = Math.Clamp((int)MathF.Round(floating), minimum, maximum);
        return changed;
    }

    public static bool ComboBox(ref int selected, ReadOnlySpan<string> items, float width)
    {
        using var region = new ContentWidth(width);
        return Gw.ComboBox(ref selected, items);
    }

    public static bool TextField(ref string text, int maxLength, string hint, float width, string leadingIcon = "")
    {
        using var region = new ContentWidth(width);
        return Gw.TextField(ref text, hint, default, maxLength, false, leadingIcon);
    }

    public static void Note(string text, Color? color = null)
        => Gw.TextWrapped(text, TextRole.Footnote, color ?? Frame.Look.SecondaryLabel);

    public static void InlineNote(string text, Color? color = null)
    {
        var lineHeight = ImGui.GetItemRectSize().Y;
        ImGui.SameLine();
        TextOnLine(text, lineHeight, TextRole.Footnote, color);
    }

    // Text that shares a line with a control sits on the control's vertical centre instead of the line's top.
    public static void TextOnLine(string text, float lineHeight, TextRole role, Color? color = null)
    {
        var look = Frame.Look;
        var scale = Frame.Scale;
        var size = TextPaint.Measure(text, role, look, scale);
        var box = Rect.FromSize(ImGui.GetCursorScreenPos(), new Vector2(size.X, MathF.Max(lineHeight, size.Y)));
        ImGui.Dummy(box.Size);
        TextPaint.DrawInBox(ImGui.GetWindowDrawList(), box, text, role, color ?? look.SecondaryLabel, look, scale, TextAlign.Start, TextTruncation.None);
    }

    public static ListAction ReorderButtons(bool canMoveUp, bool canMoveDown, bool canRemove = true)
    {
        var action = ListAction.None;
        ImGui.BeginDisabled(!canMoveUp);
        if (Gw.IconButton(ArrowUpGlyph, ButtonStyle.Plain, ButtonSize.Small, Loc.T(L.Common.MoveUp)))
        {
            action = ListAction.MoveUp;
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!canMoveDown);
        if (Gw.IconButton(ArrowDownGlyph, ButtonStyle.Plain, ButtonSize.Small, Loc.T(L.Common.MoveDown)))
        {
            action = ListAction.MoveDown;
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (RemoveButton(canRemove))
        {
            action = ListAction.Remove;
        }

        return action;
    }

    public static bool RemoveButton(bool enabled = true, string? tooltip = null)
    {
        ImGui.BeginDisabled(!enabled);
        var clicked = Gw.IconButton(RemoveGlyph, ButtonStyle.Destructive, ButtonSize.Small, tooltip ?? Loc.T(L.Common.Remove));
        ImGui.EndDisabled();
        return clicked;
    }

    public static void LanguageRow(Configuration cfg)
    {
        var languages = Languages.All;
        var selected = 0;
        for (var index = 0; index < languages.Length; index++)
        {
            if (ReferenceEquals(languages[index], Loc.Current))
            {
                selected = index;
            }
        }

        if (ComboRow(L.Settings.Language, L.Settings.LanguageHelp, ref selected, languageLabels))
        {
            ApplyLanguage(cfg, languages[selected].Code);
        }
    }

    private static void RangeDash()
    {
        var lineHeight = ImGui.GetItemRectSize().Y;
        ImGui.SameLine();
        TextOnLine("-", lineHeight, TextRole.Body);
        ImGui.SameLine();
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

    private static string ReadoutFormat(string template, string? zeroText)
    {
        var key = zeroText is null ? template : string.Concat(template, "\0", zeroText);
        if (readoutFormats.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var builder = new StringBuilder(template.Length + 16);
        AppendNumericFormat(builder, template);
        if (zeroText is not null)
        {
            builder.Append(";;");
            AppendLiteral(builder, zeroText);
        }

        var format = builder.ToString();
        readoutFormats[key] = format;
        return format;
    }

    private static void AppendNumericFormat(StringBuilder builder, string template)
    {
        var literalStart = 0;
        for (var index = 0; index + 1 < template.Length; index++)
        {
            if (template[index] != '%')
            {
                continue;
            }

            var next = template[index + 1];
            if (next != 'd' && next != '%')
            {
                continue;
            }

            AppendLiteral(builder, template.AsSpan(literalStart, index - literalStart));
            builder.Append(next == 'd' ? "0" : "\"%\"");
            index++;
            literalStart = index + 1;
        }

        AppendLiteral(builder, template.AsSpan(literalStart));
    }

    private static void AppendLiteral(StringBuilder builder, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return;
        }

        builder.Append('"');
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"')
            {
                builder.Append("\"\\\"\"");
                continue;
            }

            builder.Append(text[index]);
        }

        builder.Append('"');
    }

    internal static class Choices
    {
        public readonly record struct Choice(LocString Name, LocString Detail);

        private static string[] names = [];

        // The chosen option's detail follows the row, where the old dropdown's caption used to sit.
        public static bool DrawRow(LocString title, LocString help, Choice[] options, ref int selected)
        {
            Resolve(options);
            selected = Math.Clamp(selected, 0, options.Length - 1);
            var changed = ComboRow(title, help, ref selected, names.AsSpan(0, options.Length));
            Note(Loc.T(options[selected].Detail));
            return changed;
        }

        private static void Resolve(Choice[] options)
        {
            if (names.Length < options.Length)
            {
                names = new string[options.Length];
            }

            for (var index = 0; index < options.Length; index++)
            {
                names[index] = Loc.T(options[index].Name);
            }
        }
    }
}
