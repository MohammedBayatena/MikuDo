using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;
using LiteDB;

namespace MikuDo.ViewModels;

/// <summary>An item offered as a task, ticked to make it.</summary>
public partial class MakeTaskRow : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public NoteItem Item { get; }
    public string Text => Item.Text;
    public bool Done => Item.Done;
    public List<string> Subtasks { get; }
    public bool HasSubtasks => Subtasks.Count > 0;
    public string SubtaskLabel => Subtasks.Count == 1 ? "1 subtask" : $"{Subtasks.Count} subtasks";

    /// <summary>The heading above the first item under it, shown as a caption.</summary>
    public string? Caption { get; init; }
    public bool HasCaption => Caption != null;
    public string? CaptionUpper => Caption?.ToUpperInvariant();

    public MakeTaskRow(NoteItem item)
    {
        Item = item;
        _isSelected = !item.Done;
        Subtasks = item.Children.Select(c => c.Text).ToList();
    }
}

/// <summary>
/// Turns a note, or lines picked out of it, into tasks: one per item with
/// whatever is nested under it as subtasks, or one task holding all of it.
/// </summary>
public partial class MakeTasksViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(CreateLabel), nameof(CanCreate), nameof(IsWhole))]
    private bool _isPerItem = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private WorkspaceNavItem? _workspace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _list = "To do";

    [ObservableProperty] private string _label = "None";
    [ObservableProperty] private bool _linkBack = true;
    [ObservableProperty] private string _wholeTitle = string.Empty;

    private readonly MainViewModel _main;
    private readonly NoteViewModel _note;
    private readonly string _wholeText;

    public string Title { get; }
    public string Subtitle { get; }
    public string NoteName => _note.FileName;

    /// <summary>The note the tasks come from.</summary>
    public NoteViewModel Note => _note;
    public ObservableCollection<MakeTaskRow> Rows { get; } = new();
    public IReadOnlyList<WorkspaceNavItem> Workspaces => _main.Workspaces;
    public string[] Lists { get; } = { "To do", "Doing" };
    public string[] Labels { get; } = new[] { "None" }.Concat(Palette.QuickLabels).ToArray();

    public bool HasRows => Rows.Count > 0;
    public bool IsWhole => !IsPerItem;
    public string PerItemLabel { get; }
    public string WholeLabel { get; }

    private int SelectedCount => Rows.Count(r => r.IsSelected);
    private int SubtaskCount => Rows.Where(r => r.IsSelected).Sum(r => r.Subtasks.Count);

    public bool CanCreate => IsPerItem ? SelectedCount > 0 : WholeTitle.Trim().Length > 0;

    public string CreateLabel => !IsPerItem ? "Create task"
                               : SelectedCount == 1 ? "Create 1 task" : $"Create {SelectedCount} tasks";

    public string Summary
    {
        get
        {
            var where = $"{Workspace?.Name ?? "My Todos"} · {List}";
            if (!IsPerItem) return $"1 task into {where}";
            var tasks = SelectedCount == 1 ? "1 task" : $"{SelectedCount} tasks";
            var subtasks = SubtaskCount switch { 0 => "", 1 => " and 1 subtask", var n => $" and {n} subtasks" };
            return $"{tasks}{subtasks} into {where}";
        }
    }

    private MakeTasksViewModel(MainViewModel main, NoteViewModel note, IReadOnlyList<NoteItem> items,
                               string wholeTitle, string wholeText, string title, string subtitle,
                               string perItemLabel, string wholeLabel)
    {
        _main = main;
        _note = note;
        _wholeText = wholeText;
        _wholeTitle = wholeTitle;
        Title = title;
        Subtitle = subtitle;
        PerItemLabel = perItemLabel;
        WholeLabel = wholeLabel;
        _workspace = main.Workspaces.FirstOrDefault(w => w.Id == main.LastWorkspaceId) ?? main.Workspaces.FirstOrDefault();

        string? heading = null;
        var first = true;
        foreach (var item in items)
        {
            var caption = first || item.Heading != heading ? item.Heading : null;
            heading = item.Heading;
            first = false;
            var row = new MakeTaskRow(item) { Caption = caption };
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(MakeTaskRow.IsSelected)) return;
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(CreateLabel));
                OnPropertyChanged(nameof(CanCreate));
            };
            Rows.Add(row);
        }
    }

    public static MakeTasksViewModel ForNote(MainViewModel main, NoteViewModel note, IReadOnlyList<NoteItem> items,
                                             string wholeTitle, string wholeText)
    {
        var open = items.Sum(i => (i.Done ? 0 : 1) + i.Children.Count(c => !c.Done));
        var headings = items.Select(i => i.Heading).Distinct().Count(h => h != null);
        var subtitle = items.Count == 0
            ? "No checklist items in this note"
            : $"{open} open {(open == 1 ? "item" : "items")} found" +
              (headings > 0 ? $" under {headings} {(headings == 1 ? "heading" : "headings")}" : "");
        return new MakeTasksViewModel(main, note, items, wholeTitle, wholeText,
            $"Make tasks from {note.FileName}", subtitle,
            "A task per checklist item", "One task, the whole note");
    }

    public static MakeTasksViewModel ForSelection(MainViewModel main, NoteViewModel note, IReadOnlyList<NoteItem> items,
                                                  string wholeTitle, string wholeText)
    {
        var lines = items.Count;
        return new MakeTasksViewModel(main, note, items, wholeTitle, wholeText,
            "Make tasks from the selection", $"{lines} {(lines == 1 ? "line" : "lines")} from {note.FileName}",
            "A task per line", "One task, all of it");
    }

    partial void OnWholeTitleChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreate));
    }

    [RelayCommand]
    private void SetMode(string? mode) => IsPerItem = mode != "whole";

    [RelayCommand]
    private void CreateTasks()
    {
        if (!CanCreate) return;

        var status = List == "Doing" ? TodoStatus.Doing : TodoStatus.Active;
        var tag = Label == "None" ? null : Label;
        var link = LinkBack ? _note.Path : null;

        List<string> made;
        if (IsPerItem)
        {
            made = Create(_main, Rows.Where(r => r.IsSelected).Select(r => r.Item).ToList(), Workspace?.Id, status, tag, link);
        }
        else
        {
            var task = new TodoItem
            {
                Title = WholeTitle.Trim(),
                Description = _wholeText,
                Status = status,
                WorkspaceId = Workspace?.Id,
                Tags = tag == null ? new List<string>() : new List<string> { tag },
                NotePath = link,
                SortOrder = App.Database.GetNextSortOrder(Workspace?.Id, status)
            };
            App.Database.UpsertTodo(task);
            made = new List<string> { task.IdText };
            _main.PushUndo("make task", Workspace?.Id, () => App.Database.DeleteTodoPermanently(task.Id));
        }

        _note.CloseDialog();
        _main.RefreshCounts();
        var count = made.Count;
        _note.ShowFlash($"{(count == 1 ? "1 task" : $"{count} tasks")} added to {Workspace?.Name ?? "My Todos"}");
        _note.RaiseTasksChanged();
    }

    /// <summary>
    /// Makes a task of each item, its nested items as subtasks: ticked ones in
    /// Done, the rest in <paramref name="status"/>. One step Undo takes back.
    /// Returns the ids of the top-level tasks.
    /// </summary>
    public static List<string> Create(MainViewModel main, IReadOnlyList<NoteItem> items, string? workspaceId,
                                      TodoStatus status, string? tag, string? notePath)
    {
        var created = new List<ObjectId>();
        var top = new List<string>();

        TodoItem Make(NoteItem item, List<string>? children)
        {
            var itemStatus = item.Done ? TodoStatus.Completed : status;
            var task = new TodoItem
            {
                Title = item.Text,
                Status = itemStatus,
                CompletedAt = item.Done ? DateTime.UtcNow : null,
                WorkspaceId = workspaceId,
                Tags = tag == null ? new List<string>() : new List<string> { tag },
                SubtaskIds = children ?? new List<string>(),
                NotePath = notePath,
                SortOrder = App.Database.GetNextSortOrder(workspaceId, itemStatus)
            };
            App.Database.UpsertTodo(task);
            created.Add(task.Id);
            return task;
        }

        foreach (var item in items)
        {
            var children = item.Children.Select(c => Make(c, null).IdText).ToList();
            top.Add(Make(item, children).IdText);
        }

        if (created.Count > 0)
            main.PushUndo(created.Count == 1 ? "make task" : "make tasks", workspaceId, () =>
            {
                foreach (var id in created) App.Database.DeleteTodoPermanently(id);
            });
        main.RefreshCounts();
        return top;
    }

    [RelayCommand]
    private void Close() => _note.CloseDialog();
}
