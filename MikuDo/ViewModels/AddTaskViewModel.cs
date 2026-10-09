using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>A label the user can switch on in a picker.</summary>
public partial class LabelChoice : ObservableObject
{
    [ObservableProperty] private bool _isChecked;
    public string Label { get; }
    public LabelChoice(string label, bool isChecked = false)
    {
        Label = label;
        _isChecked = isChecked;
    }
}

/// <summary>The Add Task modal.</summary>
public partial class AddTaskViewModel : ObservableObject
{
    [ObservableProperty] private string _taskName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _statusName = "To do";
    [ObservableProperty] private TodoPriority _priority = TodoPriority.None;
    [ObservableProperty] private string _customLabel = string.Empty;
    [ObservableProperty] private string _subtaskQuery = string.Empty;
    /// <summary>Workspace headings and the tasks under them: the newest matches, up to the picker's limit.</summary>
    [ObservableProperty] private ObservableCollection<object> _subtaskCandidates = new();

    /// <summary>What the picker lists, and how much more matches than it shows.</summary>
    [ObservableProperty] private string _subtaskNote = string.Empty;

    public ObservableCollection<WorkspaceChoice> SubtaskWorkspaces { get; } = new();
    private string _subtaskWorkspace = SubtaskPicker.All;
    private readonly Dictionary<string, string> _workspaceNames = SubtaskPicker.WorkspaceNames();

    /// <summary>Bumped by every search, so a reply that a newer one overtook is dropped.</summary>
    private int _searchVersion;

    /// <summary>The workspaces with candidates, as of the last search that read them.</summary>
    private HashSet<string>? _workspaceKeys;

    /// <summary>The last page fetched, which knows where the next one starts.</summary>
    private SubtaskPicker.Found? _lastPage;

    /// <summary>How many candidates are in the list so far, over every page.</summary>
    private int _loaded;
    [ObservableProperty] private ObservableCollection<TaskOption> _selectedSubtasks = new();

    private readonly MainViewModel _main;
    private readonly string? _workspaceId;

    public string WorkspaceLabel { get; }
    public string[] StatusChoices { get; } = { "To do", "Doing", "Done" };
    public TodoPriority[] PriorityChoices { get; } = { TodoPriority.None, TodoPriority.Low, TodoPriority.Medium, TodoPriority.High };
    public ObservableCollection<LabelChoice> Labels { get; }

    public AddTaskViewModel(MainViewModel main, string? workspaceId, TodoStatus status)
    {
        _main = main;
        _workspaceId = workspaceId;
        _statusName = Palette.ColumnName(status);

        WorkspaceLabel = App.Database.GetWorkspaceById(workspaceId ?? "")?.Name
                         ?? App.Database.GetDefaultWorkspaceName();
        Labels = new ObservableCollection<LabelChoice>(Palette.SuggestedLabels.Select(l => new LabelChoice(l)));

        RefreshCandidates();
    }

    [RelayCommand] private void PickStatus(string? name) => StatusName = name ?? "To do";
    [RelayCommand] private void PickPriority(TodoPriority priority) => Priority = priority;

    [RelayCommand]
    private void ToggleLabel(LabelChoice? choice)
    {
        if (choice != null) choice.IsChecked = !choice.IsChecked;
    }

    [RelayCommand]
    private void AddCustomLabel()
    {
        var label = CustomLabel.Trim();
        if (label.Length == 0) return;

        var existing = Labels.FirstOrDefault(l => string.Equals(l.Label, label, StringComparison.OrdinalIgnoreCase));
        if (existing != null) existing.IsChecked = true;
        else Labels.Add(new LabelChoice(label, isChecked: true));

        CustomLabel = string.Empty;
    }

    // ── Subtasks ────────────────────────────────────────────────

    partial void OnSubtaskQueryChanged(string value) => _ = SearchCandidatesAsync();

    private SubtaskPicker.Request CandidateRequest()
        => new(SubtaskQuery?.Trim() ?? string.Empty, _subtaskWorkspace, _workspaceId ?? "",
               SelectedSubtasks.Select(o => o.Item.IdText).ToList());

    /// <summary>Fills the picker straight away: as the dialog opens, or when a chip or a link changes it.</summary>
    private void RefreshCandidates()
    {
        _searchVersion++;
        ShowCandidates(SubtaskPicker.Find(CandidateRequest(), _workspaceNames));
    }

    /// <summary>
    /// Searches as the user types. It runs off the UI thread, so however large
    /// the database grows, typing never waits on it.
    /// </summary>
    private async Task SearchCandidatesAsync()
    {
        var version = ++_searchVersion;
        var request = CandidateRequest();
        var keys = _workspaceKeys;
        var names = _workspaceNames;
        try
        {
            var found = await Task.Run(() => SubtaskPicker.Find(request, names, keys));
            if (version == _searchVersion) ShowCandidates(found);
        }
        catch (Exception ex)
        {
            LogService.Error("Subtask search", ex);
        }
    }

    /// <summary>A fresh list from a search's first page; the scroll goes back to the top.</summary>
    private void ShowCandidates(SubtaskPicker.Found found)
    {
        var names = _workspaceNames;
        _subtaskWorkspace = found.Filter;
        _workspaceKeys = found.Keys;
        _lastPage = found;
        _loaded = found.Items.Count;
        SubtaskPicker.Sync(SubtaskWorkspaces, SubtaskPicker.Choices(found, names));

        var rows = new ObservableCollection<object>();
        SubtaskPicker.Append(rows, found, _loaded, names);
        SubtaskCandidates = rows;
        SubtaskNote = SubtaskPicker.Note(found, _loaded);
    }

    /// <summary>
    /// The next page, as the foot of the list scrolls into view. It continues
    /// the same search from where the last page stopped and is added below
    /// what is shown; a newer search makes it moot and it is dropped.
    /// </summary>
    [RelayCommand]
    private async Task LoadMoreCandidates()
    {
        if (_lastPage is not { } last || _loaded >= last.Total) return;

        var version = _searchVersion;
        var request = last.Request with { Filter = last.Filter, From = last.Next, Counts = last.Counts };
        var keys = last.Keys;
        var names = _workspaceNames;
        try
        {
            var page = await Task.Run(() => SubtaskPicker.Find(request, names, keys));
            if (version != _searchVersion) return;

            _lastPage = page;
            _loaded += page.Items.Count;
            SubtaskPicker.Append(SubtaskCandidates, page, _loaded, names);
            SubtaskNote = SubtaskPicker.Note(page, _loaded);
        }
        catch (Exception ex)
        {
            LogService.Error("Loading more subtasks", ex);
        }
    }

    [RelayCommand]
    private void PickSubtaskWorkspace(WorkspaceChoice? choice)
    {
        if (choice == null) return;
        _subtaskWorkspace = choice.Key;
        RefreshCandidates();
    }

    [RelayCommand]
    private void LinkSubtask(TaskOption? option)
    {
        if (option == null) return;
        if (SelectedSubtasks.Any(o => o.Item.IdText == option.Item.IdText)) return;

        SelectedSubtasks.Add(option);
        SubtaskQuery = string.Empty;
        RefreshCandidates();
    }

    [RelayCommand]
    private void UnlinkSubtask(TaskOption? option)
    {
        if (option == null) return;
        SelectedSubtasks.Remove(option);
        RefreshCandidates();
    }

    // ── Create ──────────────────────────────────────────────────

    public bool CanCreate => !string.IsNullOrWhiteSpace(TaskName);

    partial void OnTaskNameChanged(string value) => OnPropertyChanged(nameof(CanCreate));

    [RelayCommand]
    private void Create()
    {
        var name = TaskName.Trim();
        if (name.Length == 0) return;

        var status = Palette.StatusFromName(StatusName);
        App.Database.UpsertTodo(new TodoItem
        {
            Title = name,
            Description = Description.Trim(),
            Status = status,
            Priority = Priority,
            WorkspaceId = _workspaceId,
            Tags = Labels.Where(l => l.IsChecked).Select(l => l.Label).ToList(),
            SubtaskIds = SelectedSubtasks.Select(o => o.Item.IdText).ToList(),
            SortOrder = App.Database.GetNextSortOrder(_workspaceId, status),
            CompletedAt = status == TodoStatus.Completed ? DateTime.UtcNow : null
        });

        _main.CloseAddTaskCommand.Execute(null);
        _main.AfterTasksCreated(_workspaceId);
    }
}
