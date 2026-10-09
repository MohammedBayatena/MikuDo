namespace MikuDo.Models;

/// <summary>
/// Resolves a task's subtask links against a lookup of live tasks. Links to
/// tasks that no longer exist are skipped rather than shown as blanks.
/// </summary>
public static class SubtaskLinks
{
    public static List<TodoItem> Resolve(TodoItem item, IReadOnlyDictionary<string, TodoItem>? lookup)
    {
        if (lookup == null) return new List<TodoItem>();

        var linked = new List<TodoItem>(item.SubtaskIds.Count);
        foreach (var id in item.SubtaskIds)
        {
            if (lookup.TryGetValue(id, out var child)) linked.Add(child);
        }
        return linked;
    }

    public static (int Done, int Total) Progress(TodoItem item, IReadOnlyDictionary<string, TodoItem>? lookup)
    {
        var linked = Resolve(item, lookup);
        return (linked.Count(t => t.Status == TodoStatus.Completed), linked.Count);
    }

    /// <summary>"2/5", or an em dash when the task has no subtasks.</summary>
    public static string ProgressLabel(TodoItem item, IReadOnlyDictionary<string, TodoItem>? lookup)
    {
        var (done, total) = Progress(item, lookup);
        return total > 0 ? $"{done}/{total}" : "—";
    }
}
