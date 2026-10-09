namespace MikuDo.Models;

/// <summary>
/// The orders a list of tasks can be shown in, and the one used when nothing
/// else is asked for.
/// </summary>
/// <remarks>
/// The default keeps a card wherever the user put it: placed cards lead, in the
/// order they were arranged. Everything else is grouped by the day it was
/// created, newest day first, and within one day the more urgent task leads.
/// Comparing the calendar day rather than the timestamp is what gives priority
/// a say at all; two tasks are almost never created in the same instant.
/// Every other order falls back to this one to settle ties, and every order
/// ends on the task's id, which no two tasks share. So a list never shuffles
/// between two identical renders, and a list cut into pages joins up exactly.
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

    /// <summary>Placed cards in their arranged order, then day created, then priority.</summary>
    public static IEnumerable<TodoItem> Default(IEnumerable<TodoItem> items)
        => ThenDefault(items.OrderBy(t => t.IsPlaced ? 0 : 1));

    /// <summary>
    /// The same order with placement ignored, for lists nobody arranges by hand:
    /// the trash, the vault, and the pickers that offer tasks to link.
    /// </summary>
    public static IEnumerable<TodoItem> Unplaced(IEnumerable<TodoItem> items)
        => items.OrderByDescending(Day)
                .ThenByDescending(t => t.Priority)
                .ThenByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id);

    public static IEnumerable<TodoItem> Apply(IEnumerable<TodoItem> items, string? order) => order switch
    {
        Priority => ThenDefault(items.OrderByDescending(t => t.Priority)),
        Newest => items.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Priority).ThenByDescending(t => t.Id),
        Oldest => items.OrderBy(t => t.CreatedAt).ThenByDescending(t => t.Priority).ThenBy(t => t.Id),
        Name => ThenDefault(items.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)),
        MostSubtasks => ThenDefault(items.OrderByDescending(t => t.SubtaskIds.Count)),
        // U+FFFF sorts after any real label, so untagged tasks go last.
        Label => ThenDefault(items.OrderBy(t => t.Tags.FirstOrDefault() ?? "￿",
                                           StringComparer.CurrentCultureIgnoreCase)),
        _ => Default(items)
    };

    private static IEnumerable<TodoItem> ThenDefault(IOrderedEnumerable<TodoItem> ordered)
        => ordered.ThenBy(t => t.IsPlaced ? 0 : 1)
                  .ThenBy(t => t.IsPlaced ? t.SortOrder : 0)
                  .ThenByDescending(Day)
                  .ThenByDescending(t => t.Priority)
                  .ThenByDescending(t => t.CreatedAt)
                  .ThenByDescending(t => t.Id);

    private static DateTime Day(TodoItem t) => t.CreatedAt.ToLocalTime().Date;
}
