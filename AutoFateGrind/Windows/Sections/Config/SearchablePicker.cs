using AutoFateGrind.Core.Localization;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

// A filter field over a Gw.ComboBox: Glasswork's combo has no search of its own, and the FATE, item and mount
// catalogs run to hundreds of entries.
internal sealed class SearchablePicker
{
    private const int FilterMaxLength = 64;

    private readonly bool followFilter;
    private string filter = string.Empty;
    private string[] source = [];
    private int[] matches = [];
    private string[] matchLabels = [];
    private int matchCount;

    // A picker whose selection only feeds an Add button follows the filter onto the first match; a picker bound to
    // a setting leaves the selection alone until the user picks.
    public SearchablePicker(bool followFilter)
    {
        this.followFilter = followFilter;
    }

    public bool HasMatches => matchCount > 0;

    public bool Draw(LocString title, LocString help, string[] labels, ref int selected)
    {
        Gw.BeginRow(Loc.T(title));
        Gw.Tooltip(Loc.T(help));
        var filterChanged = SettingsControls.TextField(ref filter, FilterMaxLength, Loc.T(L.Settings.SearchHint), SettingsControls.FilterWidth, SettingsControls.SearchGlyph);
        Gw.EndRow();
        if (filterChanged || !ReferenceEquals(source, labels))
        {
            Rebuild(labels);
        }

        if (matchCount == 0)
        {
            SettingsControls.Note(Loc.T(L.Common.NoMatches, filter));
            return false;
        }

        var changed = false;
        var position = PositionOf(selected);
        if (position < 0 && followFilter)
        {
            position = 0;
            selected = matches[0];
            changed = true;
        }

        if (SettingsControls.ComboBox(ref position, matchLabels.AsSpan(0, matchCount), SettingsControls.PickerWidth))
        {
            selected = matches[position];
            changed = true;
        }

        return changed;
    }

    private void Rebuild(string[] labels)
    {
        source = labels;
        if (matches.Length < labels.Length)
        {
            matches = new int[labels.Length];
            matchLabels = new string[labels.Length];
        }

        matchCount = 0;
        for (var index = 0; index < labels.Length; index++)
        {
            if (filter.Length > 0 && !labels[index].Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matches[matchCount] = index;
            matchLabels[matchCount] = labels[index];
            matchCount++;
        }
    }

    private int PositionOf(int selected)
    {
        for (var position = 0; position < matchCount; position++)
        {
            if (matches[position] == selected)
            {
                return position;
            }
        }

        return -1;
    }
}
