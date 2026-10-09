using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Controls;
using MikuDo.Models;

namespace MikuDo.ViewModels;

public class StatCard
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Delta { get; init; } = string.Empty;
    public Brush DeltaForeground { get; init; } = Brushes.Gray;
}

/// <summary>One status in the breakdown, with its share of the tasks shown.</summary>
public class BreakdownRow
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
    public string Share { get; init; } = string.Empty;
    public Brush Dot { get; init; } = Brushes.Gray;
}

/// <summary>One column of the "Completed this week" chart.</summary>
public class ChartBar
{
    public string Day { get; init; } = string.Empty;
    public double Height { get; init; }
    public bool IsToday { get; init; }
    public int Count { get; init; }
}

public class AttentionRow
{
    public TodoItem Item { get; init; } = null!;
    public string Title => Item.Title;
    public string Note { get; init; } = string.Empty;
    public Brush Dot { get; init; } = Brushes.Gray;
    public string Tag { get; init; } = string.Empty;
    public Brush TagBackground { get; init; } = Brushes.Transparent;
    public Brush TagForeground { get; init; } = Brushes.Gray;

    public TodoPriority Priority => Item.Priority;
    public bool HasPriority => Item.Priority != TodoPriority.None;
    public string PriorityName => TaskCardViewModel.PriorityText(Item.Priority);
    public Brush PriorityForeground => Palette.Priority(Item.Priority).Foreground;
    public int CommentCount => Item.Comments.Count;
}

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<StatCard> _stats = new();
    [ObservableProperty] private ObservableCollection<ChartBar> _chart = new();
    [ObservableProperty] private ObservableCollection<AttentionRow> _needsAttention = new();
    [ObservableProperty] private string _weekSummary = string.Empty;
    [ObservableProperty] private bool _chartIsEmpty;

    // ── Breakdown: To do, Doing, Done ──
    [ObservableProperty] private ObservableCollection<BreakdownRow> _breakdown = new();
    [ObservableProperty] private IReadOnlyList<DonutSegment> _breakdownSegments = Array.Empty<DonutSegment>();
    [ObservableProperty] private int _breakdownTotal;

    // ── Flow: what came in against what went out, and how long work takes ──
    [ObservableProperty] private int _createdThisWeek;
    [ObservableProperty] private int _completedThisWeek;
    [ObservableProperty] private string _flowNote = string.Empty;
    [ObservableProperty] private Brush _flowNoteForeground = Brushes.Gray;
    [ObservableProperty] private string _cycleTime = string.Empty;
    [ObservableProperty] private string _cycleNote = string.Empty;

    // ── Which workspace the dashboard covers ──
    public ObservableCollection<WorkspaceChoice> WorkspaceChoices { get; } = new();
    [ObservableProperty] private bool _isWorkspaceMenuOpen;
    [ObservableProperty] private string _workspaceLabel = AllWorkspaces;

    private const string AllWorkspaces = "All workspaces";

    /// <summary><see cref="SubtaskPicker.All"/>, "" for the default workspace, or a workspace id.</summary>
    private string _workspaceKey = SubtaskPicker.All;

    private readonly MainViewModel _main;

    public string TodayLabel => DateTime.Now.ToString("dddd, d MMMM yyyy");

    public DashboardViewModel(MainViewModel main) => _main = main;

    [RelayCommand]
    private void ToggleWorkspaceMenu() => IsWorkspaceMenuOpen = !IsWorkspaceMenuOpen;

    [RelayCommand]
    private void PickWorkspace(WorkspaceChoice? choice)
    {
        IsWorkspaceMenuOpen = false;
        if (choice == null || choice.Key == _workspaceKey) return;

        _workspaceKey = choice.Key;
        Reload();
    }

    /// <summary>
    /// "All workspaces", the default one, then the rest in sidebar order. A
    /// workspace deleted while it was picked falls back to all of them.
    /// </summary>
    private void BuildWorkspaceChoices()
    {
        var choices = new List<WorkspaceChoice>
        {
            new(SubtaskPicker.All, AllWorkspaces, false),
            new("", App.Database.GetDefaultWorkspaceName(), false)
        };
        choices.AddRange(App.Database.GetAllWorkspaces().Select(w => new WorkspaceChoice(w.Id.ToString(), w.Name, false)));

        if (choices.All(c => c.Key != _workspaceKey)) _workspaceKey = SubtaskPicker.All;
        foreach (var choice in choices) choice.IsSelected = choice.Key == _workspaceKey;

        WorkspaceChoices.Clear();
        foreach (var choice in choices) WorkspaceChoices.Add(choice);
        WorkspaceLabel = choices.First(c => c.IsSelected).Name;
    }

    public void Reload()
    {
        BuildWorkspaceChoices();

        var all = App.Database.GetAllBoardTodos();
        if (_workspaceKey != SubtaskPicker.All)
            all = all.Where(t => (t.WorkspaceId ?? "") == _workspaceKey).ToList();

        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);

        var open = all.Count(t => t.Status == TodoStatus.Active);
        var doing = all.Count(t => t.Status == TodoStatus.Doing);
        var done = all.Count(t => t.Status == TodoStatus.Completed);
        var newThisWeek = all.Count(t => t.CreatedAt >= weekAgo);
        var doneThisWeek = all.Count(t => t.Status == TodoStatus.Completed && CompletedOn(t) >= weekAgo);

        Stats = new ObservableCollection<StatCard>
        {
            new() { Label = "Open tasks", Value = open.ToString(),
                    Delta = newThisWeek > 0 ? $"+{newThisWeek}" : "steady",
                    DeltaForeground = Palette.StatusForeground(TodoStatus.Completed) },
            new() { Label = "In progress", Value = doing.ToString(), Delta = doing > 0 ? "active" : "idle",
                    DeltaForeground = Palette.StatusForeground(TodoStatus.Doing) },
            new() { Label = "Completed", Value = done.ToString(),
                    Delta = doneThisWeek > 0 ? $"+{doneThisWeek}" : "none yet",
                    DeltaForeground = Palette.StatusForeground(TodoStatus.Completed) },
            new() { Label = "Vault items", Value = App.Database.GetVaultCount().ToString(), Delta = "encrypted",
                    DeltaForeground = Palette.Label("Internal").Foreground }
        };

        BuildChart(all);
        BuildBreakdown(open, doing, done);
        BuildFlow(all, newThisWeek, doneThisWeek);
        BuildAttention(all, App.Database.GetTodoLookup());
    }

    private void BuildBreakdown(int open, int doing, int done)
    {
        var total = open + doing + done;
        BreakdownTotal = total;

        var parts = new[]
        {
            (Status: TodoStatus.Active, Count: open),
            (Status: TodoStatus.Doing, Count: doing),
            (Status: TodoStatus.Completed, Count: done)
        };

        BreakdownSegments = parts.Select(p => new DonutSegment(p.Count, Palette.ColumnDot(p.Status))).ToList();
        Breakdown = new ObservableCollection<BreakdownRow>(parts.Select(p => new BreakdownRow
        {
            Name = Palette.ColumnName(p.Status),
            Count = p.Count,
            Share = total == 0 ? "—" : $"{Math.Round(100.0 * p.Count / total):0}%",
            Dot = Palette.ColumnDot(p.Status)
        }));
    }

    /// <summary>
    /// Created against completed over the week says whether the backlog is
    /// growing; the median time from created to done says how long a task
    /// takes once it exists. The median, so one ancient task does not skew it.
    /// </summary>
    private void BuildFlow(List<TodoItem> all, int created, int completed)
    {
        CreatedThisWeek = created;
        CompletedThisWeek = completed;

        var net = created - completed;
        (FlowNote, FlowNoteForeground) = net switch
        {
            > 0 => ($"Backlog grew by {net}", Palette.StatusForeground(TodoStatus.Active)),
            < 0 => ($"Backlog shrank by {-net}", Palette.StatusForeground(TodoStatus.Completed)),
            _ => ("Backlog held steady", Palette.StatusForeground(TodoStatus.Doing))
        };

        var monthAgo = DateTime.UtcNow.AddDays(-30);
        var spans = all.Where(t => t.Status == TodoStatus.Completed && t.CompletedAt is { } at && at >= monthAgo)
                       .Select(t => (t.CompletedAt!.Value - t.CreatedAt).TotalDays)
                       .Where(d => d >= 0)
                       .OrderBy(d => d)
                       .ToList();

        if (spans.Count == 0)
        {
            CycleTime = "—";
            CycleNote = "No tasks finished in the last 30 days";
            return;
        }

        var median = spans.Count % 2 == 1
            ? spans[spans.Count / 2]
            : (spans[spans.Count / 2 - 1] + spans[spans.Count / 2]) / 2;

        CycleTime = median < 1
            ? $"{Math.Max(1, Math.Round(median * 24)):0} h"
            : $"{median:0.#} days";
        CycleNote = $"Median from created to done, {spans.Count} task{(spans.Count == 1 ? "" : "s")} in the last 30 days";
    }

    private void BuildChart(List<TodoItem> all)
    {
        var today = DateTime.Now.Date;
        var counts = new int[7];

        for (var i = 0; i < 7; i++)
        {
            var day = today.AddDays(i - 6);
            counts[i] = all.Count(t => t.Status == TodoStatus.Completed &&
                                       CompletedOn(t).ToLocalTime().Date == day);
        }

        var peak = Math.Max(1, counts.Max());
        var bars = new List<ChartBar>();
        for (var i = 0; i < 7; i++)
        {
            var day = today.AddDays(i - 6);
            bars.Add(new ChartBar
            {
                Day = day.ToString("ddd")[..1],
                Height = 0.08 + 0.92 * counts[i] / peak,
                IsToday = day == today,
                Count = counts[i]
            });
        }
        Chart = new ObservableCollection<ChartBar>(bars);

        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var doneThisWeek = counts.Sum();
        ChartIsEmpty = doneThisWeek == 0;
        var touchedThisWeek = all.Count(t => t.UpdatedAt >= weekAgo || t.CreatedAt >= weekAgo);
        WeekSummary = $"{doneThisWeek} of {Math.Max(doneThisWeek, touchedThisWeek)} tasks";
    }

    private void BuildAttention(List<TodoItem> all, IReadOnlyDictionary<string, TodoItem> lookup)
    {
        var open = all.Where(t => t.Status != TodoStatus.Completed)
                      .OrderByDescending(t => (int)t.Priority)
                      .ThenBy(t => t.UpdatedAt)
                      .Take(8);

        var rows = new List<AttentionRow>();
        foreach (var item in open)
        {
            // Priority has a place of its own on the row, so the chip names the
            // first label, or the column when there is none.
            var tag = item.Tags.FirstOrDefault() ?? Palette.ColumnName(item.Status);
            var colors = item.Tags.Count > 0 ? Palette.Label(tag) : Palette.Priority(TodoPriority.None);

            rows.Add(new AttentionRow
            {
                Item = item,
                Note = NoteFor(item, lookup),
                Dot = Palette.ColumnDot(item.Status),
                Tag = tag,
                TagBackground = colors.Background,
                TagForeground = colors.Foreground
            });
        }
        NeedsAttention = new ObservableCollection<AttentionRow>(rows);
    }

    private static string NoteFor(TodoItem item, IReadOnlyDictionary<string, TodoItem> lookup)
    {
        var (done, total) = SubtaskLinks.Progress(item, lookup);
        if (total > 0) return $"{done} of {total} subtasks done";
        if (string.IsNullOrWhiteSpace(item.Description)) return "No description yet";

        var age = (int)(DateTime.UtcNow - item.UpdatedAt).TotalDays;
        return age switch
        {
            <= 0 => "Updated today",
            1 => "Updated yesterday",
            _ => $"Untouched for {age} days"
        };
    }

    private static DateTime CompletedOn(TodoItem item) => item.CompletedAt ?? item.UpdatedAt;

    [RelayCommand]
    private void OpenTask(AttentionRow? row)
    {
        if (row != null) _main.OpenTask(row.Item, isVault: false, "Dashboard");
    }
}
