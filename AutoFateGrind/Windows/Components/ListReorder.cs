namespace AutoFateGrind.Windows.Components;

// Shared tail for the deferred move-up / move-down / remove pattern used by the list editors. moveDown is
// bounded by `count` (the displayed row count, which can differ from list.Count when the view is filtered).
// Returns true if it mutated the list so the caller can persist once.
internal static class ListReorder
{
    public static bool Apply<T>(IList<T> list, int count, int? moveUp, int? moveDown, int? remove)
    {
        if (moveUp is int mu && mu > 0)
        {
            (list[mu - 1], list[mu]) = (list[mu], list[mu - 1]);
            return true;
        }
        if (moveDown is int md && md < count - 1)
        {
            (list[md + 1], list[md]) = (list[md], list[md + 1]);
            return true;
        }
        if (remove is int r)
        {
            list.RemoveAt(r);
            return true;
        }
        return false;
    }

    public static bool Move<T>(IList<T> list, int from, int to)
    {
        if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to) return false;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
        return true;
    }
}
