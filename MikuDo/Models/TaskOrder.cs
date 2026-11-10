namespace MikuDo.Models;

/// <summary>
/// The orders a list of tasks can be shown in, and the one used when nothing
/// else is asked for.
/// </summary>
/// <remarks>
/// The default keeps a card wherever the user put it: a placed card holds its
/// position in the list, counted from the top, and every other card fills the
/// places around it by priority, the most urgent first, and within one
/// priority the newest first. So a new urgent task still goes to the top,
/// and a card dragged to third place stays third.
/// Every order ends on the task's id, which no two tasks share. So a list
/// never shuffles between two identical renders, and a list cut into pages
/// joins up exactly.
/// </remarks>
public static class TaskOrder
{
    public const string Manual = "Manual";
    public const string Priority = "Priority";
    public const string Newest = "Newest first";
    public const string Oldest = "Oldest first";
    public const string Name = "Name A–Z";
    public const string MostSubtasks = "Most subtasks";
    public const string Label = "Label";

    /// <summary>Shown on the board's sort button when the lists are sorted separately.</summary>
    public const string Custom = "Custom";

    public static readonly string[] All = { Manual, Priority, Newest, Oldest, Name, MostSubtasks, Label };

    /// <summary>
    /// One list in the default order: each placed card at its position, and
    /// the rest in <see cref="Unplaced"/> order in the places between. A
    /// position past the end of the list puts the card last; two cards placed
    /// at the same position stand together, the newer first.
    /// </summary>
    public static IEnumerable<TodoItem> Default(IEnumerable<TodoItem> items)
    {
        var all = items.ToList();
        var placed = all.Where(t => t.IsPlaced)
                        .OrderBy(t => t.SortOrder)
                        .ThenByDescending(t => t.CreatedAt)
                        .ThenByDescending(t => t.Id)
                        .ToList();
        var rest = Unplaced(all.Where(t => !t.IsPlaced)).ToList();

        int p = 0, r = 0;
        for (var at = 0; p < placed.Count || r < rest.Count; at++)
            yield return p < placed.Count && (placed[p].SortOrder <= at || r == rest.Count) ? placed[p++] : rest[r++];
    }

    /// <summary>
    /// Priority, the most urgent first, then the newest first, with placement
    /// ignored. Cards nobody placed take this order, and so do lists nobody
    /// arranges by hand: the trash, the vault, and the pickers that offer tasks to link.
    /// </summary>
    public static IEnumerable<TodoItem> Unplaced(IEnumerable<TodoItem> items)
        => items.OrderByDescending(t => t.Priority)
                .ThenByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id);

    /// <summary>
    /// <paramref name="items"/> in <paramref name="order"/>. For the default
    /// order, <paramref name="positions"/> can hold where each task stands in
    /// its whole list, so a list cut down by a search or a filter keeps the
    /// order its cards have in the whole of it.
    /// </summary>
    public static IEnumerable<TodoItem> Apply(IEnumerable<TodoItem> items, string? order,
                                              IReadOnlyDictionary<string, int>? positions = null) => order switch
    {
        Priority => Unplaced(items),
        Newest => items.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Priority).ThenByDescending(t => t.Id),
        Oldest => items.OrderBy(t => t.CreatedAt).ThenByDescending(t => t.Priority).ThenBy(t => t.Id),
        Name => ThenUnplaced(items.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)),
        MostSubtasks => ThenUnplaced(items.OrderByDescending(t => t.SubtaskIds.Count)),
        // U+FFFF sorts after any real label, so untagged tasks go last.
        Label => ThenUnplaced(items.OrderBy(t => t.Tags.FirstOrDefault() ?? "￿",
                                            StringComparer.CurrentCultureIgnoreCase)),
        _ when positions != null => items.OrderBy(t => positions.TryGetValue(t.IdText, out var at) ? at : int.MaxValue)
                                         .ThenByDescending(t => t.Id),
        _ => Default(items)
    };

    /// <summary>Where each task stands in <paramref name="items"/> in the default order, 0 at the top.</summary>
    public static Dictionary<string, int> Positions(IEnumerable<TodoItem> items)
        => Default(items).Select((t, i) => (t.IdText, i)).ToDictionary(p => p.IdText, p => p.i, StringComparer.Ordinal);

    private static IOrderedEnumerable<TodoItem> ThenUnplaced(IOrderedEnumerable<TodoItem> ordered)
        => ordered.ThenByDescending(t => t.Priority)
                  .ThenByDescending(t => t.CreatedAt)
                  .ThenByDescending(t => t.Id);
}
