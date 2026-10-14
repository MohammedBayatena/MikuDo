using MikuDo.Models;

namespace MikuDo.Services;

/// <summary>When a task was made and when its words last changed, as its page and its card say it.</summary>
public static class TaskDates
{
    /// <summary>A change lands later than the task was made by at least this much before it is worth saying.</summary>
    private static readonly TimeSpan WorthSaying = TimeSpan.FromMinutes(1);

    /// <summary>The title, description or labels changed after the task was made.</summary>
    public static bool WasEdited(TodoItem task)
        => task.EditedAt is { } edited && edited - task.CreatedAt >= WorthSaying;

    /// <summary>"Created on 3 Oct at 14:32 · Edited today at 09:12", for the task's own page.</summary>
    public static string Label(TodoItem task)
        => "Created " + When.Phrase(task.CreatedAt)
           + (WasEdited(task) ? " · Edited " + When.Phrase(task.EditedAt!.Value) : "");

    /// <summary>
    /// The moments in full: made, words last edited, and last changed in any
    /// way (a move, a tick, a new comment) when that came later still. Then the
    /// time zone they are shown in.
    /// </summary>
    public static string Tooltip(TodoItem task)
    {
        var lines = new List<string> { $"Created {When.Full(task.CreatedAt)}" };
        var edited = WasEdited(task);
        if (edited) lines.Add($"Edited {When.Full(task.EditedAt!.Value)}");

        var since = edited ? task.EditedAt!.Value : task.CreatedAt;
        if (task.UpdatedAt - since >= WorthSaying) lines.Add($"Last changed in any way {When.Full(task.UpdatedAt)}");

        lines.Add($"Times are in your time zone, {When.Zone}");
        return string.Join("\n", lines);
    }
}
