using CommunityToolkit.Mvvm.ComponentModel;
using MikuDo.Models;

namespace MikuDo.ViewModels;

/// <summary>A workspace's heading among the subtask picker's candidates, with how many it holds in all.</summary>
public class WorkspaceHeading
{
    public string Name { get; }
    public int Count { get; }
    public string Label => $"{Name.ToUpperInvariant()} · {Count}";

    public WorkspaceHeading(string name, int count)
    {
        Name = name;
        Count = count;
    }
}

/// <summary>The foot of a picker list with more to load. Scrolled into view, it loads the next page.</summary>
public class LoadMoreRow
{
    public string Label { get; }

    public LoadMoreRow(string label) => Label = label;
}

/// <summary>A chip that narrows the subtask picker to one workspace, or to all of them.</summary>
public partial class WorkspaceChoice : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    /// <summary><see cref="SubtaskPicker.All"/>, "" for the default workspace, or a workspace id.</summary>
    public string Key { get; }
    public string Name { get; }

    public WorkspaceChoice(string key, string name, bool isSelected)
    {
        Key = key;
        Name = name;
        _isSelected = isSelected;
    }
}

/// <summary>
/// The subtask picker shared by the task page and Add Task. It searches the
/// database a page at a time, lists matches grouped under their workspace with
/// the task's own workspace first, and can narrow to one workspace.
/// </summary>
/// <remarks>
/// Pages run through the workspaces in the order they are listed, each one
/// newest first, so every page continues where the last one stopped and is
/// simply added to the foot of the list. Headings and tasks share one flat
/// list rather than a grouped view, so the list keeps virtualizing.
/// </remarks>
public static class SubtaskPicker
{
    /// <summary>The key of the "All" chip.</summary>
    public const string All = "*";

    /// <summary>Where the next page starts: which workspace in the order, and how far into it.</summary>
    public readonly record struct Cursor(int Group, int Skip);

    /// <summary>One search, captured on the UI thread so it can run off it.</summary>
    /// <param name="Home">The task's own workspace key, listed first.</param>
    /// <param name="Exclude">Ids that cannot be picked: the task itself and what is already linked.</param>
    /// <param name="From">Where this page starts; the first page starts at the top.</param>
    /// <param name="Counts">Matches per workspace from the first page, so later pages need not count again.</param>
    public sealed record Request(string Query, string Filter, string Home, IReadOnlyCollection<string> Exclude,
                                 Cursor From = default, IReadOnlyDictionary<string, int>? Counts = null);

    /// <summary>One page of the answer.</summary>
    /// <param name="Filter">The workspace actually searched: a chip whose workspace has nothing left falls back to All.</param>
    /// <param name="Groups">The workspaces paged through, in the order they are listed.</param>
    /// <param name="Next">Where the page after this one starts.</param>
    /// <param name="Keys">Every workspace with a candidate, for the chips.</param>
    public sealed record Found(Request Request, string Filter, IReadOnlyList<string> Groups, List<TodoItem> Items,
                               IReadOnlyDictionary<string, int> Counts, Cursor Next, HashSet<string> Keys)
    {
        public int Total => Counts.Values.Sum();
    }

    /// <summary>The key a task's workspace is filed under: "" is the default workspace.</summary>
    public static string KeyOf(TodoItem task) => task.WorkspaceId ?? "";

    public static Dictionary<string, string> WorkspaceNames()
    {
        var names = App.Database.GetAllWorkspaces()
            .ToDictionary(w => w.Id.ToString(), w => w.Name, StringComparer.Ordinal);
        names[""] = App.Database.GetDefaultWorkspaceName();
        return names;
    }

    /// <summary>
    /// Fetches one page from the database. It builds nothing for the screen,
    /// so it is safe off the UI thread. <paramref name="keys"/> are the
    /// workspaces from an earlier page, reused while the user types or scrolls
    /// since neither changes them; without them they are read afresh.
    /// </summary>
    public static Found Find(Request request, IReadOnlyDictionary<string, string> names, HashSet<string>? keys = null)
    {
        keys ??= App.Database.GetLinkableWorkspaceKeys(request.Exclude);
        var filter = request.Filter == All || keys.Contains(request.Filter) ? request.Filter : All;
        var groups = filter == All ? Ordered(keys, request.Home, names).ToList() : new List<string> { filter };

        var counts = request.Counts ?? groups.ToDictionary(
            key => key, key => App.Database.CountLinkable(request.Query, key, request.Exclude), StringComparer.Ordinal);

        var items = new List<TodoItem>();
        var (group, skip) = request.From;
        while (items.Count < Paging.PageSize && group < groups.Count)
        {
            var want = Paging.PageSize - items.Count;
            var page = App.Database.SearchLinkable(request.Query, groups[group], request.Exclude, skip, want);
            items.AddRange(page);

            // A short page means that workspace is done; the next starts at its top.
            if (page.Count < want) (group, skip) = (group + 1, 0);
            else skip += page.Count;
        }

        return new Found(request, filter, groups, items, counts, new Cursor(group, skip), keys);
    }

    /// <summary>
    /// Adds a page to the foot of the rows already shown, leaving them as they
    /// are: the foot row comes off, the page's tasks go on with a heading
    /// wherever a new workspace starts, and the foot goes back while more remain.
    /// </summary>
    public static void Append(System.Collections.ObjectModel.ObservableCollection<object> rows, Found page,
                              int loaded, IReadOnlyDictionary<string, string> names)
    {
        if (rows.Count > 0 && rows[^1] is LoadMoreRow) rows.RemoveAt(rows.Count - 1);

        var current = rows.OfType<TaskOption>().LastOrDefault() is { } last ? KeyOf(last.Item) : null;
        foreach (var item in page.Items)
        {
            var key = KeyOf(item);
            if (key != current)
            {
                rows.Add(new WorkspaceHeading(NameOf(key, names), page.Counts.GetValueOrDefault(key)));
                current = key;
            }
            rows.Add(new TaskOption(item));
        }

        if (loaded < page.Total) rows.Add(new LoadMoreRow(Paging.MoreLabel(page.Total - loaded)));
    }

    /// <summary>
    /// "All" and every workspace that has a candidate. They come from the
    /// whole set, not the search, so the chips stay put while the user types.
    /// </summary>
    public static List<WorkspaceChoice> Choices(Found found, IReadOnlyDictionary<string, string> names)
    {
        var choices = new List<WorkspaceChoice> { new(All, "All", found.Filter == All) };
        foreach (var key in Ordered(found.Keys, found.Request.Home, names))
            choices.Add(new WorkspaceChoice(key, NameOf(key, names), found.Filter == key));
        return choices;
    }

    /// <summary>What the list holds, said outright, since it can hold only part of what matches.</summary>
    public static string Note(Found found, int loaded)
    {
        var searching = found.Request.Query.Length > 0;
        if (found.Total == 0)
            return searching ? $"No tasks match \u201c{found.Request.Query}\u201d" : "No tasks to link yet";

        var noun = searching ? (found.Total == 1 ? "match" : "matches") : (found.Total == 1 ? "task" : "tasks");
        return loaded < found.Total
            ? $"Showing {loaded} of {found.Total} {noun}, newest first \u00b7 scroll for more"
            : $"{found.Total} {noun}";
    }

    /// <summary>
    /// Updates the chips in place when the workspaces on offer are the same,
    /// so typing in the search box does not rebuild them on every keystroke.
    /// </summary>
    public static void Sync(System.Collections.ObjectModel.ObservableCollection<WorkspaceChoice> target,
                            List<WorkspaceChoice> fresh)
    {
        if (target.Select(c => c.Key).SequenceEqual(fresh.Select(c => c.Key)))
        {
            for (var i = 0; i < fresh.Count; i++) target[i].IsSelected = fresh[i].IsSelected;
            return;
        }

        target.Clear();
        foreach (var choice in fresh) target.Add(choice);
    }

    private static IEnumerable<string> Ordered(IEnumerable<string> keys, string home, IReadOnlyDictionary<string, string> names)
        => keys.OrderBy(k => k == home ? 0 : 1)
               .ThenBy(k => NameOf(k, names), StringComparer.CurrentCultureIgnoreCase)
               .ThenBy(k => k, StringComparer.Ordinal);

    private static string NameOf(string key, IReadOnlyDictionary<string, string> names)
        => names.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "Unknown workspace";
}
