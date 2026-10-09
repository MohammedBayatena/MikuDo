using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;
using LiteDB;
using Microsoft.Win32;

namespace MikuDo.ViewModels;

/// <summary>An image thumbnail in the attachments panel and in the lightbox.</summary>
public class GalleryImage
{
    public string StorageId { get; }
    public int Number { get; }
    public ImageSource? Source { get; }

    /// <summary>The trailing dashed tile that adds another image.</summary>
    public bool IsAddSlot { get; init; }

    public GalleryImage(string storageId, int number, ImageSource? source)
    {
        StorageId = storageId;
        Number = number;
        Source = source;
    }
}

/// <summary>A recorded voice memo with its waveform.</summary>
public partial class VoiceMemoViewModel : ObservableObject
{
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _position = "0:00";

    public int Number { get; }
    public string StorageId { get; }
    public string Name => $"Voice memo #{Number}";
    public List<WaveBar> Bars { get; }

    public VoiceMemoViewModel(int number, string storageId, double[] bars)
    {
        Number = number;
        StorageId = storageId;
        Bars = bars.Select(b => new WaveBar(b)).ToList();
    }

    public void SetProgress(double fraction)
    {
        var cutoff = (int)(Bars.Count * Math.Clamp(fraction, 0, 1));
        for (var i = 0; i < Bars.Count; i++) Bars[i].IsPlayed = i < cutoff;
    }
}

/// <summary>One bar of a waveform. Height is a fraction of the strip.</summary>
public partial class WaveBar : ObservableObject
{
    [ObservableProperty] private bool _isPlayed;
    public double Height { get; }
    public WaveBar(double height) => Height = Math.Clamp(height, 0.12, 1.0) * 20;
}

/// <summary>A task linked as a subtask of the one being edited.</summary>
public partial class SubtaskRow : ObservableObject
{
    [ObservableProperty] private bool _isDone;

    public TodoItem Item { get; }
    public string Title => Item.Title;
    public string StatusName => Palette.ColumnName(Item.Status);
    public Brush StatusForeground => Palette.StatusForeground(Item.Status);
    public TodoPriority Priority => Item.Priority;
    public bool HasPriority => Item.Priority != TodoPriority.None;
    public string PriorityName => TaskCardViewModel.PriorityText(Item.Priority);
    public Brush PriorityForeground => Palette.Priority(Item.Priority).Foreground;

    private readonly Action<SubtaskRow>? _toggled;

    public SubtaskRow(TodoItem item, Action<SubtaskRow>? toggled)
    {
        Item = item;
        // Assigned to the field so the callback does not run while constructing.
        _isDone = item.Status == TodoStatus.Completed;
        _toggled = toggled;
    }

    partial void OnIsDoneChanged(bool value) => _toggled?.Invoke(this);

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusName));
        OnPropertyChanged(nameof(StatusForeground));
    }
}

/// <summary>A task offered in the "add subtask" picker.</summary>
public class TaskOption
{
    public TodoItem Item { get; }
    public string Title => Item.Title;
    public string StatusName => Palette.ColumnName(Item.Status);
    public Brush StatusForeground => Palette.StatusForeground(Item.Status);
    public TodoPriority Priority => Item.Priority;
    public Brush PriorityForeground => Palette.Priority(Item.Priority).Foreground;

    public TaskOption(TodoItem item) => Item = item;
}

/// <summary>How the task page is being left.</summary>
public enum DetailExit
{
    /// <summary>The Save button or Ctrl+S.</summary>
    Save,

    /// <summary>The back button or Escape.</summary>
    Back,

    /// <summary>Another page was picked from the sidebar; it is already on its way in.</summary>
    Away,

    /// <summary>The app is closing; nothing comes next.</summary>
    Close
}

public partial class TaskDetailViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private TodoItem? _existing;
    private readonly bool _isVault;

    /// <summary>The id of the task on the page; null for one not saved yet.</summary>
    public string? TaskId => _existing?.IdText;
    private readonly string? _workspaceId;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _markdown = string.Empty;
    [ObservableProperty] private string _renderedHtml = string.Empty;
    [ObservableProperty] private TodoStatus _status = TodoStatus.Active;
    [ObservableProperty] private TodoPriority _priority = TodoPriority.None;
    [ObservableProperty] private string _editorMode = "Split";      // Edit | Split | Preview
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _subtaskQuery = string.Empty;
    [ObservableProperty] private bool _isSubtaskPickerOpen;
    [ObservableProperty] private string _newTag = string.Empty;
    [ObservableProperty] private bool _isTagEditorOpen;
    [ObservableProperty] private bool _isStatusMenuOpen;
    [ObservableProperty] private bool _isSidebarCollapsed = App.Database.GetSetting("DetailSidebarCollapsed") == "True";

    [ObservableProperty] private ObservableCollection<TagChip> _tagChips = new();
    [ObservableProperty] private ObservableCollection<SubtaskRow> _subtasks = new();
    /// <summary>Workspace headings and the tasks under them: the newest matches, up to the picker's limit.</summary>
    [ObservableProperty] private ObservableCollection<object> _subtaskCandidates = new();

    /// <summary>What the picker lists, and how much more matches than it shows.</summary>
    [ObservableProperty] private string _subtaskNote = string.Empty;

    public ObservableCollection<WorkspaceChoice> SubtaskWorkspaces { get; } = new();
    private string _subtaskWorkspace = SubtaskPicker.All;
    private Dictionary<string, string>? _workspaceNames;

    /// <summary>The tasks above this one, read once: none of them can be linked beneath it.</summary>
    private HashSet<string>? _ancestorIds;

    /// <summary>The tasks this one is a subtask of. A task can sit under more than one.</summary>
    public IReadOnlyList<TaskOption> Parents { get; }
    public bool HasParents => Parents.Count > 0;

    /// <summary>The note this task was made from, if any.</summary>
    public string? NotePath => _existing?.NotePath;
    public bool HasNote => NotePath != null;
    public string NoteName => NotePath == null ? string.Empty : Path.GetFileName(NotePath);

    /// <summary>Opens the note the task came from; the task is saved on the way, as on any way off the page.</summary>
    [RelayCommand]
    private void OpenNote()
    {
        if (NotePath != null) _main.OpenNote(NotePath);
    }

    /// <summary>Bumped by every search, so a reply that a newer one overtook is dropped.</summary>
    private int _searchVersion;

    /// <summary>The workspaces with candidates, as of the last search that read them.</summary>
    private HashSet<string>? _workspaceKeys;

    /// <summary>The last page fetched, which knows where the next one starts.</summary>
    private SubtaskPicker.Found? _lastPage;

    /// <summary>How many candidates are in the list so far, over every page.</summary>
    private int _loaded;
    [ObservableProperty] private ObservableCollection<GalleryImage> _images = new();
    [ObservableProperty] private ObservableCollection<GalleryImage> _imageSlots = new();
    [ObservableProperty] private ObservableCollection<VoiceMemoViewModel> _voiceMemos = new();
    [ObservableProperty] private ObservableCollection<AttachedFile> _files = new();

    private List<string> _subtaskIds = new();

    private readonly Dictionary<int, string> _imageMap = new();
    private readonly Dictionary<int, string> _voiceMap = new();
    private int _nextImageNum = 1;
    private int _nextVoiceNum = 1;

    // Storage ids created this session: dropped on cancel, kept on save.
    private readonly HashSet<string> _newMediaIds = new();

    /// <summary>Media the saved task still points at but the user has removed; deleted when the task is saved without them.</summary>
    private readonly HashSet<string> _removedMediaIds = new();
    private readonly Dictionary<string, string> _base64Cache = new();

    /// <summary>Records voice memos, stored as the microphone heard them; <see cref="ReadVoice"/> levels them for playback.</summary>
    private readonly AudioService _recorder = new() { LevelsVoice = false };
    private readonly AudioPlayer _player = new();
    private System.Windows.Threading.DispatcherTimer? _debounce;
    private System.Windows.Threading.DispatcherTimer? _playbackTicker;

    public string ParentLabel { get; }

    /// <summary>The shell, for the auto-save setting the header reflects.</summary>
    public MainViewModel Main => _main;
    public bool IsVaultTask => _isVault;
    public bool IsExistingTask => _existing != null;
    public string WorkspaceLabel { get; }

    public string StatusName => Palette.ColumnName(Status);
    public Brush StatusForeground => Palette.StatusForeground(Status);
    public bool ShowEditor => EditorMode is "Edit" or "Split";
    public bool ShowPreview => EditorMode is "Split" or "Preview";
    public bool ShowComments => EditorMode == "Comments";
    public string SubtaskProgress => $"{Subtasks.Count(s => s.IsDone)}/{Subtasks.Count}";
    public bool HasOpenSubtasks => Subtasks.Any(s => !s.IsDone);

    public string[] StatusChoices { get; } = { "To do", "Doing", "Done" };
    public TodoPriority[] PriorityChoices { get; } = { TodoPriority.None, TodoPriority.Low, TodoPriority.Medium, TodoPriority.High };
    public string[] SuggestedLabels => Palette.SuggestedLabels;

    public TaskDetailViewModel(MainViewModel main, TodoItem? existing, bool isVault,
                               string? workspaceId, string parentLabel)
    {
        _main = main;
        _existing = existing;
        _isVault = isVault;
        _workspaceId = existing?.WorkspaceId ?? workspaceId;
        ParentLabel = parentLabel;

        WorkspaceLabel = isVault
            ? "Vault"
            : App.Database.GetWorkspaceById(_workspaceId ?? "")?.Name ?? App.Database.GetDefaultWorkspaceName();

        if (existing != null)
        {
            _title = existing.Title;
            _markdown = existing.Description;
            _status = existing.Status == TodoStatus.Trashed ? TodoStatus.Active : existing.Status;
            _priority = existing.Priority;
            TagChips = new ObservableCollection<TagChip>(existing.Tags.Select(t => new TagChip(t)));
            _subtaskIds = new List<string>(existing.SubtaskIds);
            Files = new ObservableCollection<AttachedFile>(existing.Files);
            foreach (var comment in existing.Comments) Comments.Add(new CommentViewModel(comment, Readable(comment)));
            RebuildMediaMaps(existing);
        }

        Parents = existing != null && !isVault
            ? App.Database.GetParents(existing.IdText).Select(p => new TaskOption(p)).ToList()
            : Array.Empty<TaskOption>();

        _recorder.RecordingStopped += OnRecordingStopped;
        _player.PlaybackEnded += OnPlaybackEnded;

        LoadImages();
        LoadVoiceMemos();
        LoadSubtasks();
        UpdatePreview();

        _savedSignature = Signature();
        _saveState = existing != null ? "Saved" : string.Empty;
    }

    // ── Property reactions ──────────────────────────────────────

    partial void OnMarkdownChanged(string value)
    {
        DebouncePreview();
        CountChecklist();
    }

    partial void OnEditorModeChanged(string value)
    {
        if (value != "Comments") _descriptionMode = value;
        OnPropertyChanged(nameof(ShowEditor));
        OnPropertyChanged(nameof(ShowPreview));
        OnPropertyChanged(nameof(ShowComments));
        OnPropertyChanged(nameof(ShowPreviewSubtasksButton));
        OnPropertyChanged(nameof(PreviewSubtasksNote));

        if (_previewStale) UpdatePreview();
    }

    /// <summary>Edit, Split or Preview: how the description was shown last, for leaving Comments back to it.</summary>
    private string _descriptionMode = "Split";

    /// <summary>Back from Comments to the description, shown as it was last.</summary>
    public void ShowDescription()
    {
        if (ShowComments) EditorMode = _descriptionMode;
    }

    partial void OnStatusChanged(TodoStatus value)
    {
        OnPropertyChanged(nameof(StatusName));
        OnPropertyChanged(nameof(StatusForeground));
    }

    /// <summary>A subtask is a task, so ticking it marks that task done straight away.</summary>
    private void OnSubtaskToggled(SubtaskRow row)
    {
        var status = row.IsDone ? TodoStatus.Completed : TodoStatus.Active;

        // A task changing column lands by date and priority, as one moved on the board does.
        if (row.Item.Status != status) row.Item.IsPlaced = false;
        row.Item.Status = status;
        row.Item.CompletedAt = row.IsDone ? DateTime.UtcNow : null;
        App.Database.UpsertTodo(row.Item);

        OnPropertyChanged(nameof(SubtaskProgress));
        OnPropertyChanged(nameof(HasOpenSubtasks));
        row.RefreshStatus();

        if (row.IsDone) _main.OfferToFinishSubtasks(new[] { row.Item }, LoadSubtasks);
    }

    /// <summary>
    /// Finishes everything beneath this task, however deep, as one step Undo
    /// takes back: the subtasks listed here and their own subtasks too.
    /// </summary>
    [RelayCommand]
    private void CompleteSubtasks()
    {
        if (_existing == null) return;
        var root = new TodoItem { Id = _existing.Id, SubtaskIds = _subtaskIds.ToList() };
        _main.FinishSubtasks(MainViewModel.OpenSubtasksBeneath(new[] { root }));
        LoadSubtasks();
    }

    // ── Preview ─────────────────────────────────────────────────

    /// <summary>
    /// Re-renders the preview once typing pauses.
    /// </summary>
    /// <remarks>
    /// One timer is restarted rather than replaced, so a long edit does not
    /// leave a timer behind per keystroke.
    /// </remarks>
    private void DebouncePreview()
    {
        if (_debounce == null)
        {
            _debounce = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _debounce.Tick += (_, _) =>
            {
                _debounce!.Stop();
                UpdatePreview();
            };
        }

        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>
    /// Markdown is only converted while the preview is on screen; in Edit mode
    /// the result would be parsed, handed to the browser and never looked at.
    /// Switching back into a preview renders whatever was missed.
    /// </summary>
    private void UpdatePreview()
    {
        if (!ShowPreview)
        {
            _previewStale = true;
            return;
        }

        _previewStale = false;
        RenderedHtml = App.Markdown.ToHtml(Markdown ?? "");
    }

    private bool _previewStale;

    public void RefreshTheme()
    {
        TagChips = new ObservableCollection<TagChip>(TagChips.Select(c => new TagChip(c.Label)));
        OnPropertyChanged(nameof(StatusForeground));
        LoadSubtasks();
        _previewStale = true;
        UpdatePreview();
    }

    // ── Editor mode ─────────────────────────────────────────────

    [RelayCommand] private void SetEditorMode(string? mode) => EditorMode = mode ?? "Split";

    [RelayCommand] private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    public const double DefaultSidebarWidth = 268;
    public const double MinSidebarWidth = 240;

    /// <summary>The sidebar's width while open. Dragging its edge changes it.</summary>
    [ObservableProperty] private double _sidebarWidth = StoredSidebarWidth();

    private static double StoredSidebarWidth()
        => double.TryParse(App.Database.GetSetting("DetailSidebarWidth"), System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var width) && width >= MinSidebarWidth
            ? width
            : DefaultSidebarWidth;

    /// <summary>Stored once a drag ends, not on every step of it.</summary>
    public void SaveSidebarWidth()
        => App.Database.SaveSetting("DetailSidebarWidth",
                                    SidebarWidth.ToString("0", System.Globalization.CultureInfo.InvariantCulture));

    partial void OnIsSidebarCollapsedChanged(bool value)
        => App.Database.SaveSetting("DetailSidebarCollapsed", value ? "True" : "False");

    [RelayCommand]
    private void SetStatus(string? name)
    {
        var status = Palette.StatusFromName(name ?? "To do");
        var finishing = status == TodoStatus.Completed && Status != TodoStatus.Completed;
        Status = status;
        IsStatusMenuOpen = false;

        // The subtasks as this page has them, saved or not.
        if (finishing && _existing != null && !_isVault)
            _main.OfferToFinishSubtasks(new[] { new TodoItem { Id = _existing.Id, Title = Title, SubtaskIds = _subtaskIds.ToList() } },
                                        LoadSubtasks);
    }

    [RelayCommand] private void ToggleStatusMenu() => IsStatusMenuOpen = !IsStatusMenuOpen;
    [RelayCommand] private void SetPriority(TodoPriority priority) => Priority = priority;

    // ── Labels ──────────────────────────────────────────────────

    [RelayCommand] private void ToggleTagEditor() => IsTagEditorOpen = !IsTagEditorOpen;

    [RelayCommand]
    private void AddTag(string? explicitTag)
    {
        var tag = (explicitTag ?? NewTag).Trim();
        if (tag.Length == 0) return;
        if (!TagChips.Any(c => string.Equals(c.Label, tag, StringComparison.OrdinalIgnoreCase)))
            TagChips.Add(new TagChip(tag));
        NewTag = string.Empty;
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveTag(TagChip? chip)
    {
        if (chip != null && TagChips.Remove(chip)) ScheduleAutoSave();
    }

    // ── Subtasks ────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the linked rows from the live tasks, and the picker list too
    /// while it is open. A link to a task now trashed or gone is dropped.
    /// </summary>
    private void LoadSubtasks()
    {
        var linked = App.Database.GetLinkableByIds(_subtaskIds);
        _subtaskIds.RemoveAll(id => !linked.ContainsKey(id));

        Subtasks = new ObservableCollection<SubtaskRow>(
            _subtaskIds.Select(id => new SubtaskRow(linked[id], OnSubtaskToggled)));

        if (IsSubtaskPickerOpen) RefreshCandidates();
        OnPropertyChanged(nameof(SubtaskProgress));
        OnPropertyChanged(nameof(HasOpenSubtasks));
    }

    private SubtaskPicker.Request CandidateRequest()
    {
        var exclude = new List<string>(_subtaskIds);
        if (_existing != null)
        {
            exclude.Add(_existing.IdText);
            exclude.AddRange(_ancestorIds ??= App.Database.GetAncestorIds(_existing.IdText));
        }
        return new SubtaskPicker.Request(SubtaskQuery?.Trim() ?? string.Empty, _subtaskWorkspace,
                                         _workspaceId ?? "", exclude);
    }

    /// <summary>Fills the picker straight away: as it opens, or when a chip or a link changes it.</summary>
    private void RefreshCandidates()
    {
        _searchVersion++;
        ShowCandidates(SubtaskPicker.Find(CandidateRequest(), _workspaceNames ??= SubtaskPicker.WorkspaceNames()));
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
        var names = _workspaceNames ??= SubtaskPicker.WorkspaceNames();
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
        var names = _workspaceNames ??= SubtaskPicker.WorkspaceNames();
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
        var names = _workspaceNames ??= SubtaskPicker.WorkspaceNames();
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

    partial void OnSubtaskQueryChanged(string value) => _ = SearchCandidatesAsync();

    [RelayCommand]
    private void ToggleSubtaskPicker()
    {
        IsSubtaskPickerOpen = !IsSubtaskPickerOpen;
        if (IsSubtaskPickerOpen) LoadSubtasks();
    }

    [RelayCommand]
    private void LinkSubtask(TaskOption? option)
    {
        if (option == null || _subtaskIds.Contains(option.Item.IdText)) return;

        _subtaskIds.Add(option.Item.IdText);
        SubtaskQuery = string.Empty;
        IsSubtaskPickerOpen = false;
        LoadSubtasks();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void UnlinkSubtask(SubtaskRow? row)
    {
        if (row == null) return;
        _subtaskIds.Remove(row.Item.IdText);
        LoadSubtasks();
        ScheduleAutoSave();
    }

    // ── Checklist to subtasks ───────────────────────────────────

    /// <summary>
    /// A vault task's checklist stays where it is: a subtask is a task on a
    /// board, where the vault's words would be in the clear.
    /// </summary>
    public bool CanMakeSubtasks => !_isVault;

    private int _checklistCount = -1;

    /// <summary>The description's checklist items, each of which can become a subtask.</summary>
    public int ChecklistCount => _checklistCount < 0 ? _checklistCount = Checklist() : _checklistCount;

    public bool HasChecklist => ChecklistCount > 0;

    /// <summary>The button, while there are items for it and no word of the last ones made stands in its place.</summary>
    public bool ShowSubtasksButton => HasChecklist && SubtasksNote.Length == 0;

    /// <summary>The button's own place, beside PREVIEW, while the Markdown and its bar are out of sight.</summary>
    public bool ShowPreviewSubtasksButton => ShowSubtasksButton && !ShowEditor && ShowPreview;

    public string ChecklistToolTip => ChecklistCount == 1
        ? "Make the checklist item a subtask of this task"
        : $"Make the {ChecklistCount} checklist items subtasks of this task, or only those selected";

    private int Checklist() => CanMakeSubtasks ? NoteFiles.Checklist(Markdown ?? string.Empty).Count : 0;

    private void CountChecklist()
    {
        var count = Checklist();
        if (count == _checklistCount) return;
        _checklistCount = count;
        OnPropertyChanged(nameof(ChecklistCount));
        OnPropertyChanged(nameof(HasChecklist));
        OnPropertyChanged(nameof(ShowSubtasksButton));
        OnPropertyChanged(nameof(ShowPreviewSubtasksButton));
        OnPropertyChanged(nameof(ChecklistToolTip));
    }

    /// <summary>
    /// Makes each item a subtask of this task: a task of its own on the same
    /// board, a ticked one already done, and the items indented under it its
    /// own subtasks. One step Undo takes back. Returns how many were made; the
    /// page takes their lines out of the description.
    /// </summary>
    public int MakeSubtasks(IReadOnlyList<NoteItem> items)
    {
        if (!CanMakeSubtasks || items.Count == 0) return 0;

        var made = MakeTasksViewModel.Create(_main, items, _workspaceId, TodoStatus.Active, null, null);
        foreach (var id in made)
            if (!_subtaskIds.Contains(id)) _subtaskIds.Add(id);
        LoadSubtasks();
        ScheduleAutoSave();
        ShowSubtasksNote(made.Count == 1 ? "1 subtask made" : $"{made.Count} subtasks made");
        return made.Count;
    }

    /// <summary>What the last conversion made, beside its button for a few seconds.</summary>
    [ObservableProperty] private string _subtasksNote = string.Empty;

    /// <summary>The same words beside PREVIEW, while that is where the button is.</summary>
    public string PreviewSubtasksNote => ShowEditor ? string.Empty : SubtasksNote;

    partial void OnSubtasksNoteChanged(string value)
    {
        OnPropertyChanged(nameof(PreviewSubtasksNote));
        OnPropertyChanged(nameof(ShowSubtasksButton));
        OnPropertyChanged(nameof(ShowPreviewSubtasksButton));
    }

    private System.Windows.Threading.DispatcherTimer? _subtasksNoteTimer;

    private void ShowSubtasksNote(string note)
    {
        SubtasksNote = note;
        if (_subtasksNoteTimer == null)
        {
            _subtasksNoteTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _subtasksNoteTimer.Tick += (_, _) =>
            {
                _subtasksNoteTimer.Stop();
                SubtasksNote = string.Empty;
            };
        }
        _subtasksNoteTimer.Stop();
        _subtasksNoteTimer.Start();
    }

    /// <summary>Opens the subtask as the task it is, saving this one on the way out.</summary>
    [RelayCommand]
    private Task OpenSubtask(SubtaskRow? row) => OpenLinked(row?.Item);

    /// <summary>Opens a task this one sits under, saving this one on the way out.</summary>
    [RelayCommand]
    private Task OpenParent(TaskOption? parent) => OpenLinked(parent?.Item);

    private async Task OpenLinked(TodoItem? item)
    {
        if (item == null || _exiting) return;
        _exiting = true;
        _autoSave?.Stop();

        await FinishRecordingAsync();
        _newMediaIds.Clear();
        Persist(string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title);

        var parentLabel = string.IsNullOrWhiteSpace(Title) ? "Task" : Title;
        Detach();
        _main.OpenTask(item, _isVault, parentLabel);
    }

    // ── Comments ────────────────────────────────────────────────

    /// <summary>Oldest first, as a conversation reads.</summary>
    public ObservableCollection<CommentViewModel> Comments { get; } = new();

    [ObservableProperty] private string _commentDraft = string.Empty;

    public int CommentCount => Comments.Count;
    public bool HasComments => Comments.Count > 0;
    public string CommentCountLabel => Comments.Count == 1 ? "1 comment" : $"{Comments.Count} comments";

    [RelayCommand]
    private void PostComment()
    {
        var text = CommentDraft?.Trim() ?? string.Empty;
        if (text.Length == 0) return;

        Comments.Add(new CommentViewModel(new TaskComment(), text));
        CommentDraft = string.Empty;
        CommentsChanged();
    }

    [RelayCommand]
    private void EditComment(CommentViewModel? comment)
    {
        if (comment == null) return;
        foreach (var other in Comments) other.IsEditing = false;
        comment.Draft = comment.Text;
        comment.IsEditing = true;
    }

    [RelayCommand]
    private void CancelCommentEdit(CommentViewModel? comment)
    {
        if (comment != null) comment.IsEditing = false;
    }

    /// <summary>An edit emptied of text is dropped; the comment keeps what it said.</summary>
    [RelayCommand]
    private void SaveCommentEdit(CommentViewModel? comment)
    {
        if (comment == null) return;
        comment.IsEditing = false;

        var text = comment.Draft.Trim();
        if (text.Length == 0 || text == comment.Text) return;

        comment.Text = text;
        comment.EditedAt = DateTime.UtcNow;
        CommentsChanged();
    }

    [RelayCommand]
    private void DeleteComment(CommentViewModel? comment)
    {
        if (comment == null) return;
        if (!DialogService.Confirm("Delete this comment? This cannot be undone.", "Delete Comment")) return;

        Comments.Remove(comment);
        CommentsChanged();
    }

    [RelayCommand]
    private void CopyComment(CommentViewModel? comment)
    {
        if (comment == null) return;
        try { Clipboard.SetText(comment.Text); }
        catch (Exception ex) { LogService.Error("Copy comment", ex); }
    }

    private void CommentsChanged()
    {
        OnPropertyChanged(nameof(CommentCount));
        OnPropertyChanged(nameof(HasComments));
        OnPropertyChanged(nameof(CommentCountLabel));
        StoreComments();
    }

    /// <summary>
    /// Writes the comments to a saved task straight away and nothing else with
    /// them, so posting a comment never saves edits the user has not chosen to
    /// keep. A task not saved yet keeps its comments until it is.
    /// </summary>
    private void StoreComments()
    {
        if (_existing == null || (_isVault && !App.Encryption.IsUnlocked)) return;

        var stored = App.Database.GetTodoById(_existing.Id);
        if (stored == null) return;

        stored.Comments = StoredComments();
        if (_isVault) App.Database.UpsertVaultTodo(stored);
        else App.Database.UpsertTodo(stored);

        _existing.Comments = Comments.Select(c => c.ToModel()).ToList();
    }

    /// <summary>The comments as stored: encrypted on a vault task, like its title and description.</summary>
    private List<TaskComment> StoredComments()
        => Comments.Select(c =>
        {
            var model = c.ToModel();
            if (_isVault)
            {
                var (text, iv) = App.Encryption.Encrypt(model.Text);
                model.Text = text;
                model.EncryptionIV = iv;
            }
            return model;
        }).ToList();

    private static string Readable(TaskComment comment)
    {
        if (comment.EncryptionIV == null) return comment.Text;
        try { return App.Encryption.Decrypt(comment.Text, comment.EncryptionIV); }
        catch { return "[Decryption failed]"; }
    }

    // ── Dictation ───────────────────────────────────────────────

    private readonly AudioService _dictationRecorder = new();
    private CancellationTokenSource? _dictationCts;
    private System.Windows.Threading.DispatcherTimer? _dictationTicker;
    private DateTime _dictationStartedAt;

    /// <summary>The longest a single dictation runs before it stops by itself.</summary>
    private static readonly TimeSpan MaxDictation = TimeSpan.FromMinutes(10);

    /// <summary>"" when idle, then "Listening", "Transcribing" and, with the AI on, "Writing".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDictationIdle), nameof(IsDictating), nameof(IsDictationBusy), nameof(DictationStatus))]
    private string _dictationState = string.Empty;

    [ObservableProperty] private string _dictationElapsed = "0:00";
    [ObservableProperty] private bool _isDictationHelpOpen;
    [ObservableProperty] private bool _isDictationModeMenuOpen;

    /// <summary>
    /// Write the words up with the AI, or keep them exactly as spoken. Each
    /// task page starts on the default from Settings; switching here holds
    /// only until the page closes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExactMode), nameof(DictationModeLabel), nameof(DictateToolTip))]
    private bool _isWriteUpMode = App.Database.GetSetting("DictationWriteUp") != "False";

    /// <summary>Writing up needs the AI Import model; without it every dictation is exact.</summary>
    public bool CanWriteUp => App.AiImport.IsModelDownloaded;

    private bool WriteUpActive => IsWriteUpMode && CanWriteUp;
    public bool IsExactMode => !WriteUpActive;
    public string DictationModeLabel => WriteUpActive ? "Write up" : "Exact";

    public bool IsDictationAvailable => App.Dictation.IsModelDownloaded;
    public bool IsDictationIdle => DictationState.Length == 0;
    public bool IsDictating => DictationState == "Listening";
    public bool IsDictationBusy => DictationState is "Transcribing" or "Writing";
    public string DictationStatus => DictationState == "Writing" ? "Writing it up…" : "Transcribing…";

    public string DictateToolTip => !IsDictationAvailable
        ? "Dictation needs a speech model: see Settings, Dictation"
        : WriteUpActive ? "Speak, and the AI writes it up here" : "Speak, and your words are written here as said";

    [RelayCommand]
    private void ToggleDictationModeMenu() => IsDictationModeMenuOpen = !IsDictationModeMenuOpen;

    /// <summary>"Exact" or "WriteUp", for this page only.</summary>
    [RelayCommand]
    private void SetDictationMode(string? mode)
    {
        IsDictationModeMenuOpen = false;
        if (mode == "WriteUp" && !CanWriteUp) return;
        IsWriteUpMode = mode == "WriteUp";
    }

    /// <summary>Raised with the words to add to the description, for the view to place at the caret.</summary>
    public event Action<string>? DictationReady;

    /// <summary>Starts listening. With no model installed it explains where to get one instead.</summary>
    [RelayCommand]
    private void Dictate()
    {
        if (!IsDictationAvailable)
        {
            IsDictationHelpOpen = true;
            return;
        }
        if (!IsDictationIdle || IsRecording) return;

        _dictationRecorder.StartRecording();
        if (!_dictationRecorder.IsRecording)
        {
            DialogService.Notify("No microphone could be opened. Check that one is connected and that Windows lets apps use it.",
                                 "Dictation");
            return;
        }

        _dictationCts = new CancellationTokenSource();
        _dictationStartedAt = DateTime.UtcNow;
        DictationElapsed = "0:00";
        DictationState = "Listening";

        _dictationTicker ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _dictationTicker.Tick -= OnDictationTick;
        _dictationTicker.Tick += OnDictationTick;
        _dictationTicker.Start();

        // The model loads while the user talks, so the words come back sooner.
        _ = WarmUpAsync(_dictationCts.Token);
    }

    private static async Task WarmUpAsync(CancellationToken ct)
    {
        try { await App.Dictation.PrepareAsync(ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogService.Error("Loading the dictation model", ex); }
    }

    private void OnDictationTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.UtcNow - _dictationStartedAt;
        DictationElapsed = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        if (elapsed >= MaxDictation) StopDictationCommand.Execute(null);
    }

    /// <summary>Stops listening, then transcribes, then writes it up when the AI is on.</summary>
    [RelayCommand]
    private async Task StopDictation()
    {
        if (!IsDictating || _dictationCts == null) return;

        var ct = _dictationCts.Token;
        _dictationTicker?.Stop();
        DictationState = "Transcribing";

        try
        {
            var wav = await _dictationRecorder.StopAsync();
            var text = await App.Dictation.TranscribeAsync(wav, ct);
            if (text.Length == 0)
            {
                DialogService.Notify("Nothing was heard. Check the microphone and try again.", "Dictation");
                return;
            }

            if (WriteUpActive)
            {
                DictationState = "Writing";
                var written = await Task.Run(() => App.AiImport.DescribeAsync(text, ct), ct);
                if (!string.IsNullOrWhiteSpace(written)) text = written;
            }

            ct.ThrowIfCancellationRequested();
            DictationReady?.Invoke(text);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogService.Error("Dictation", ex);
            DialogService.Notify($"Dictation failed:\n{ex.Message}", "Dictation");
        }
        finally
        {
            DictationState = string.Empty;
        }
    }

    /// <summary>Stops without keeping anything: Escape, the discard button, or the page going away.</summary>
    [RelayCommand]
    private void CancelDictation()
    {
        _dictationCts?.Cancel();
        _dictationTicker?.Stop();
        if (_dictationRecorder.IsRecording) _ = _dictationRecorder.StopAsync();
        if (IsDictating) DictationState = string.Empty;
    }

    [RelayCommand]
    private void OpenDictationSettings()
    {
        IsDictationHelpOpen = false;
        _main.NavigateToSettingsCommand.Execute(null);
    }

    // ── Rewrite with AI ─────────────────────────────────────────

    private CancellationTokenSource? _rewriteCts;

    /// <summary>The model is at work; the editor is read-only until it is done.</summary>
    [ObservableProperty] private bool _isRewriting;
    [ObservableProperty] private bool _isRewriteHelpOpen;

    /// <summary>A word beside the button when a rewrite changed nothing, and why. It clears itself.</summary>
    [ObservableProperty] private string _rewriteNote = string.Empty;

    private System.Windows.Threading.DispatcherTimer? _rewriteNoteTimer;

    private void ShowRewriteNote(string note)
    {
        RewriteNote = note;
        _rewriteNoteTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _rewriteNoteTimer.Tick -= ClearRewriteNote;
        _rewriteNoteTimer.Tick += ClearRewriteNote;
        _rewriteNoteTimer.Stop();
        _rewriteNoteTimer.Start();
    }

    private void ClearRewriteNote(object? sender, EventArgs e)
    {
        _rewriteNoteTimer?.Stop();
        RewriteNote = string.Empty;
    }

    /// <summary>Rewrite uses the AI Import model.</summary>
    public bool CanRewrite => App.AiImport.IsModelDownloaded;

    public string RewriteToolTip => CanRewrite
        ? "Rewrite the selection, or the whole description, with AI (Ctrl+Z puts it back)"
        : "Rewrite needs the AI Import model: see Settings";

    private static readonly Regex MediaTokenRe = new(@"!?\[[^\]]*\]\((?:img|voice)://\d+\)", RegexOptions.Compiled);

    /// <summary>
    /// <paramref name="text"/> rewritten by the model, or null when there is
    /// nothing to show for it. Images and voice memos are swapped for markers
    /// the model is told to keep, and put back after; any it drops are added at
    /// the end, so a rewrite never loses an attachment.
    /// </summary>
    public async Task<string?> RewriteAsync(string text)
    {
        if (IsRewriting || string.IsNullOrWhiteSpace(text)) return null;
        RewriteNote = string.Empty;
        if (text.Length > AiImportService.RewriteMaxChars)
        {
            ShowRewriteNote("Too long for one go: select a part and rewrite that");
            return null;
        }

        var tokens = new List<string>();
        var masked = MediaTokenRe.Replace(text, m =>
        {
            tokens.Add(m.Value);
            return $"@@{tokens.Count}@@";
        });

        IsRewriting = true;
        _rewriteCts = new CancellationTokenSource();
        try
        {
            var ct = _rewriteCts.Token;
            var result = await Task.Run(() => App.AiImport.RewriteAsync(masked, ct), ct);
            if (result == null)
            {
                ShowRewriteNote("The rewrite did not hold up; your text is unchanged");
                return null;
            }

            for (var i = 0; i < tokens.Count; i++)
            {
                var marker = $"@@{i + 1}@@";
                result = result.Contains(marker) ? result.Replace(marker, tokens[i]) : result + "\n\n" + tokens[i];
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            LogService.Error("Rewrite with AI", ex);
            ShowRewriteNote("The rewrite failed; your text is unchanged");
            return null;
        }
        finally
        {
            IsRewriting = false;
        }
    }

    [RelayCommand]
    private void CancelRewrite() => _rewriteCts?.Cancel();

    [RelayCommand]
    private void OpenAiSettings()
    {
        IsRewriteHelpOpen = false;
        _main.NavigateToSettingsCommand.Execute(null);
    }

    // ── Images ──────────────────────────────────────────────────

    private void LoadImages()
    {
        Images = new ObservableCollection<GalleryImage>(
            _imageMap.OrderBy(kv => kv.Key)
                     .Select(kv => new GalleryImage(kv.Value, kv.Key, LoadBitmap(kv.Value))));

        ImageSlots = new ObservableCollection<GalleryImage>(Images)
        {
            new(string.Empty, 0, null) { IsAddSlot = true }
        };
    }

    private static ImageSource? LoadBitmap(string storageId)
    {
        try
        {
            using var stream = App.Database.GetImage(storageId);
            if (stream == null) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    [RelayCommand]
    private void OpenImage(GalleryImage? image)
    {
        if (image == null) return;
        _main.OpenGallery(Images.ToList(), Math.Max(0, Images.IndexOf(image)));
    }

    /// <summary>Opens the viewer on image #<paramref name="number"/>, as the preview numbers them.</summary>
    public void OpenImageNumber(int number) => OpenImage(Images.FirstOrDefault(i => i.Number == number));

    /// <summary>Removes an image once the user agrees: its tile, its place in the text, and on saving its file.</summary>
    [RelayCommand]
    private void RemoveImageTile(GalleryImage? image)
    {
        if (image is not { IsAddSlot: false }) return;
        if (!DialogService.Confirm($"Remove {ImageFiles.NameOf(image.StorageId)} from this task?",
                                   "Remove image", "Remove", "Keep")) return;
        RemoveImage(image.Number);
        ScheduleAutoSave();
    }

    /// <summary>
    /// Lets go of a removed image, voice memo or file. One attached since the
    /// page opened is deleted at once. One the saved task still points at is
    /// kept until the task is saved without it, so leaving without saving
    /// leaves the task whole.
    /// </summary>
    private void ForgetMedia(string storageId)
    {
        if (_newMediaIds.Remove(storageId)) App.Database.DeleteImage(storageId);
        else _removedMediaIds.Add(storageId);
        _base64Cache.Remove(storageId);
    }

    [RelayCommand]
    private void SaveImage(GalleryImage? image)
    {
        if (image is { IsAddSlot: false }) ImageFiles.SaveAs(image.StorageId);
    }

    [RelayCommand]
    private void AttachImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Attach image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
        {
            using var fs = File.OpenRead(path);
            AddImage(fs, Path.GetFileName(path));
        }
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    /// <summary>Whether the clipboard holds a picture or image files to attach.</summary>
    public static bool ClipboardHasImages()
    {
        try
        {
            return Clipboard.ContainsImage()
                || (Clipboard.ContainsFileDropList()
                    && Clipboard.GetFileDropList().Cast<string>().Any(IsImageFile));
        }
        catch { return false; }
    }

    private static bool IsImageFile(string? path)
        => path != null && ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Attaches what the clipboard holds: a copied picture, or image files
    /// copied in Explorer. False when there is nothing to attach.
    /// </summary>
    /// <remarks>
    /// A picture is taken from the clipboard's PNG when there is one. The
    /// bitmap form that screenshots and browsers also offer often loses its
    /// transparency, or comes out black where the PNG is fine.
    /// </remarks>
    public bool PasteImages()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().Where(IsImageFile).ToList();
                foreach (var path in files)
                {
                    using var fs = File.OpenRead(path);
                    AddImage(fs, Path.GetFileName(path));
                }
                if (files.Count > 0) return true;
            }

            if (!Clipboard.ContainsImage()) return false;
            var name = $"paste_{DateTime.Now:yyyyMMdd_HHmmss}.png";

            if (Clipboard.GetData("PNG") is MemoryStream png && png.Length > 0)
            {
                png.Position = 0;
                AddImage(png, name);
                return true;
            }

            var image = Clipboard.GetImage();
            if (image == null) return false;

            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(ms);
            ms.Position = 0;
            AddImage(ms, name);
            return true;
        }
        catch (Exception ex)
        {
            DialogService.Notify($"Could not paste the image:\n{ex.Message}", "Paste");
            return true;
        }
    }

    public void InsertScreenshot(byte[] pngData)
    {
        using var ms = new MemoryStream(pngData);
        AddImage(ms, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
    }

    private void AddImage(Stream data, string filename)
    {
        var storageId = App.Database.StoreImage(data, filename);
        _newMediaIds.Add(storageId);
        var num = _nextImageNum++;
        _imageMap[num] = storageId;
        Markdown += $"\n\n![Image#{num}](img://{num})\n";
        LoadImages();
    }

    public void RemoveImage(int num)
    {
        if (_imageMap.TryGetValue(num, out var storageId))
        {
            ForgetMedia(storageId);
            _imageMap.Remove(num);
        }
        Markdown = Regex.Replace(Markdown ?? "", $@"\s*!\[Image#{num}\]\(img://{num}\)\s*", "\n").Trim();
        LoadImages();
    }

    // ── Voice memos ─────────────────────────────────────────────

    private void LoadVoiceMemos()
    {
        VoiceMemos = new ObservableCollection<VoiceMemoViewModel>(
            _voiceMap.OrderBy(kv => kv.Key).Select(kv =>
            {
                var bytes = ReadBytes(kv.Value);
                return new VoiceMemoViewModel(kv.Key, kv.Value, AudioPlayer.Waveform(bytes, 20));
            }));
    }

    private static byte[] ReadBytes(string storageId)
    {
        try
        {
            using var stream = App.Database.GetImage(storageId);
            if (stream == null) return Array.Empty<byte>();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return Array.Empty<byte>(); }
    }

    /// <summary>
    /// A voice memo as it should be heard. Memos are stored as quiet as the
    /// microphone made them and leveled on the way out, kept for the rest of
    /// the page so a second play does not level the clip again.
    /// </summary>
    private byte[] ReadVoice(string storageId)
    {
        if (_leveledVoice.TryGetValue(storageId, out var known)) return known;
        return _leveledVoice[storageId] = VoiceLevel.ApplyToWav(ReadBytes(storageId));
    }

    private readonly Dictionary<string, byte[]> _leveledVoice = new(StringComparer.Ordinal);

    [RelayCommand]
    private void ToggleRecording()
    {
        if (!IsDictationIdle) return;   // one microphone, one use at a time
        if (_recorder.IsRecording) _recorder.StopRecording();
        else
        {
            _recorder.StartRecording();
            IsRecording = true;
        }
    }

    private void OnRecordingStopped(byte[] wavData)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsRecording = false;
            if (wavData.Length < 1000) return;

            using var ms = new MemoryStream(wavData);
            var storageId = App.Database.StoreImage(ms, $"voice_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            _newMediaIds.Add(storageId);
            var num = _nextVoiceNum++;
            _voiceMap[num] = storageId;
            Markdown += $"\n\n[Voice#{num}](voice://{num})\n";
            LoadVoiceMemos();
        });
    }

    [RelayCommand]
    private void TogglePlay(VoiceMemoViewModel? memo)
    {
        if (memo == null) return;

        if (_player.CurrentId == memo.StorageId && _player.IsPlaying)
        {
            _player.Toggle(memo.StorageId, Array.Empty<byte>());
            memo.IsPlaying = false;
            StopTicker();
            return;
        }

        foreach (var other in VoiceMemos) other.IsPlaying = false;
        _player.Play(memo.StorageId, ReadVoice(memo.StorageId));
        memo.IsPlaying = _player.IsPlaying;
        StartTicker();
    }

    [RelayCommand]
    private void StopPlay()
    {
        _player.Stop();
        StopTicker();
        foreach (var memo in VoiceMemos)
        {
            memo.IsPlaying = false;
            memo.Position = "0:00";
            memo.SetProgress(0);
        }
    }

    private void StartTicker()
    {
        _playbackTicker ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _playbackTicker.Tick -= OnTick;
        _playbackTicker.Tick += OnTick;
        _playbackTicker.Start();
    }

    private void StopTicker() => _playbackTicker?.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        var memo = VoiceMemos.FirstOrDefault(m => m.StorageId == _player.CurrentId);
        if (memo == null) { StopTicker(); return; }

        var duration = _player.Duration.TotalSeconds;
        var position = _player.Position.TotalSeconds;
        memo.Position = TimeSpan.FromSeconds(position).ToString(@"m\:ss");
        memo.SetProgress(duration > 0 ? position / duration : 0);
    }

    private void OnPlaybackEnded()
        => Application.Current?.Dispatcher.BeginInvoke(new Action(StopPlay));

    public void RemoveVoice(int num)
    {
        if (_voiceMap.TryGetValue(num, out var storageId))
        {
            if (_player.CurrentId == storageId) StopPlay();
            ForgetMedia(storageId);
            _voiceMap.Remove(num);
        }
        Markdown = Regex.Replace(Markdown ?? "", $@"\s*\[Voice#{num}\]\(voice://{num}\)\s*", "\n").Trim();
        LoadVoiceMemos();
    }

    [RelayCommand]
    private void DeleteVoice(VoiceMemoViewModel? memo)
    {
        if (memo != null) RemoveVoice(memo.Number);
    }

    // ── File attachments ────────────────────────────────────────

    [RelayCommand]
    private void AttachFile()
    {
        var dlg = new OpenFileDialog { Title = "Add attachment", Multiselect = true };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
        {
            try
            {
                var info = new FileInfo(path);
                using var fs = File.OpenRead(path);
                var storageId = App.Database.StoreImage(fs, info.Name);
                _newMediaIds.Add(storageId);
                Files.Add(new AttachedFile { StorageId = storageId, Name = info.Name, Size = info.Length });
                ScheduleAutoSave();
            }
            catch (Exception ex)
            {
                DialogService.Notify($"Could not attach {Path.GetFileName(path)}:\n{ex.Message}", "Attachment");
            }
        }
    }

    [RelayCommand]
    private void OpenFile(AttachedFile? file)
    {
        if (file == null) return;
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "MikuDo", file.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);

            using (var source = App.Database.GetImage(file.StorageId))
            {
                if (source == null) return;
                using var dest = File.Create(temp);
                source.CopyTo(dest);
            }
            Process.Start(new ProcessStartInfo(temp) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DialogService.Notify($"Could not open {file.Name}:\n{ex.Message}", "Attachment");
        }
    }

    [RelayCommand]
    private void RemoveFile(AttachedFile? file)
    {
        if (file == null) return;
        ForgetMedia(file.StorageId);
        Files.Remove(file);
        ScheduleAutoSave();
    }

    // ── Markdown preview bridge ─────────────────────────────────

    private string? LoadBase64(string storageId, bool voice = false)
    {
        if (_base64Cache.TryGetValue(storageId, out var cached)) return cached;

        var bytes = voice ? ReadVoice(storageId) : ReadBytes(storageId);
        if (bytes.Length == 0) return null;

        var encoded = Convert.ToBase64String(bytes);
        _base64Cache[storageId] = encoded;
        return encoded;
    }

    /// <summary>
    /// Script injected into the preview: swaps img://N and voice://N for real
    /// data, adds remove buttons, has a click on an image open it in the app's
    /// image viewer, and passes Ctrl+V on to the page, since the preview keeps
    /// the keyboard while it has focus.
    /// </summary>
    public string GetMediaControlsScript()
    {
        var panel = Palette.Css("InlineAddBgBrush");
        var border = Palette.Css("BorderFaintBrush");
        var text = Palette.Css("TextSecondaryBrush");
        var accent = Palette.Css("AccentBrush");

        var sb = new System.Text.StringBuilder();
        sb.Append("var _imgData={");
        foreach (var (num, storageId) in _imageMap)
        {
            var b64 = LoadBase64(storageId);
            if (b64 != null) sb.Append($"{num}:'data:image/png;base64,{b64}',");
        }
        sb.Append("}; var _voiceData={");
        foreach (var (num, storageId) in _voiceMap)
        {
            var b64 = LoadBase64(storageId, voice: true);
            if (b64 != null) sb.Append($"{num}:'data:audio/wav;base64,{b64}',");
        }
        sb.Append("};");

        sb.Append(@"
        document.addEventListener('keydown', function(e) {
            if (e.ctrlKey && !e.altKey && (e.key === 'v' || e.key === 'V')) window.chrome.webview.postMessage('paste');
        });

        document.querySelectorAll('img').forEach(function(img) {
            var m = (img.getAttribute('src') || '').match(/^img:\/\/(\d+)$/);
            if (m && _imgData[m[1]]) img.src = _imgData[m[1]];
            var altM = (img.getAttribute('alt') || '').match(/^Image#(\d+)$/);
            if (!altM) return;
            var num = altM[1];
            var wrap = document.createElement('div');
            wrap.style.cssText = 'position:relative;display:inline-block;margin:10px 0';
            img.parentNode.insertBefore(wrap, img); wrap.appendChild(img);
            img.style.cursor = 'zoom-in'; img.style.margin = '0';
            img.onclick = function(e) { e.stopPropagation(); window.chrome.webview.postMessage('open_image:' + num); };
            var x = document.createElement('span');
            x.innerHTML = '&times;'; x.title = 'Remove';
            x.style.cssText = 'position:absolute;top:8px;right:8px;cursor:pointer;background:rgba(0,0,0,.55);color:#fff;width:22px;height:22px;border-radius:11px;display:flex;align-items:center;justify-content:center;font-size:14px';
            x.onclick = function() { window.chrome.webview.postMessage('remove_image:' + num); };
            wrap.appendChild(x);
        });

        document.querySelectorAll('a').forEach(function(a) {
            var m = (a.getAttribute('href') || '').match(/^voice:\/\/(\d+)$/);
            if (!m) return;
            var num = m[1], src = _voiceData[num];
            if (!src) return;
            var row = document.createElement('div');
            row.className = 'mikudo-audio';
            row.style.cssText = 'margin:10px 0;display:flex;align-items:center;gap:11px;padding:9px 12px;border:1px solid " + border + @";border-radius:10px;background:" + panel + @"';
            var label = document.createElement('span');
            label.textContent = 'Voice memo #' + num;
            label.style.cssText = 'font-size:11.5px;font-weight:600;color:" + text + @"';
            var audio = document.createElement('audio');
            audio.controls = true; audio.src = src;
            audio.style.cssText = 'height:30px;flex:1;min-width:140px';
            var x = document.createElement('span');
            x.innerHTML = '&times;'; x.title = 'Remove';
            x.style.cssText = 'cursor:pointer;color:" + accent + @";font-size:17px;font-weight:700;line-height:1';
            x.onclick = function() { window.chrome.webview.postMessage('remove_voice:' + num); };
            row.appendChild(label); row.appendChild(audio); row.appendChild(x);
            a.parentNode.replaceChild(row, a);
        });");

        return sb.ToString();
    }

    // ── Save / delete / navigate ────────────────────────────────

    [RelayCommand]
    private Task Save() => ExitAsync(DetailExit.Save);

    [RelayCommand]
    private Task Cancel() => ExitAsync(DetailExit.Back);

    /// <summary>
    /// The one way off this page.
    /// </summary>
    /// <remarks>
    /// A recording still running is treated as stopped and kept, whichever way
    /// the page is left. With auto-save on, leaving saves; with it off, a new
    /// task with content becomes a draft and an existing one drops its edits —
    /// unless a recording was just kept, which saves the task so the clip has
    /// somewhere to live.
    /// </remarks>
    public async Task ExitAsync(DetailExit exit)
    {
        if (_exiting) return;
        _exiting = true;
        _autoSave?.Stop();

        var recorded = await FinishRecordingAsync();

        if (exit == DetailExit.Save)
        {
            if (string.IsNullOrWhiteSpace(Title)) Title = "Untitled";
            _newMediaIds.Clear();
            Persist(Title);
        }
        else if (_main.IsAutoSave)
        {
            if (!SaveIfChanged()) DiscardNewMedia();
        }
        else if (exit == DetailExit.Close)
        {
            // Closing the app gives no chance to choose, so nothing typed is
            // lost: an existing task keeps its edits under its own title, and a
            // new one with content becomes a draft.
            if (IsExistingTask) SaveIfChanged();
            else if (HasUnsavedContent()) SaveAsDraft();
            else DiscardNewMedia();
        }
        else if (!IsExistingTask)
        {
            if (HasUnsavedContent()) SaveAsDraft();
            else DiscardNewMedia();
        }
        else if (recorded)
        {
            SaveIfChanged();
        }
        else
        {
            DiscardNewMedia();
        }

        Detach();

        // Leaving through the sidebar, the next page is already on screen and
        // may list this task; leaving through the page, the board comes next.
        if (exit == DetailExit.Away) _main.RefreshCurrentPage();
        else if (exit != DetailExit.Close) _main.CloseDetail(_isVault, _workspaceId);
    }

    /// <summary>
    /// Stops a recording still running and waits for its clip to be stored.
    /// True when a clip long enough to keep came out of it.
    /// </summary>
    private async Task<bool> FinishRecordingAsync()
    {
        if (!_recorder.IsRecording) return false;

        var stop = _recorder.StopAsync();
        if (await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))) != stop)
        {
            LogService.Error("A recording did not finish stopping within 5 seconds and was dropped", null);
            return false;
        }

        // OnRecordingStopped has stored the clip by the time the stop completes.
        return (await stop).Length >= 1000;
    }

    // ── Auto-save ───────────────────────────────────────────────

    /// <summary>"Saving…" while an edit waits to be written, "Saved" after.</summary>
    [ObservableProperty] private string _saveState;

    private System.Windows.Threading.DispatcherTimer? _autoSave;
    private string _savedSignature;
    private bool _exiting;

    /// <summary>Everything a save writes, joined, so "changed" is one string comparison.</summary>
    private string Signature() => string.Join('\u001f',
        Title, Markdown, Status, Priority,
        string.Join(',', TagChips.Select(c => c.Label)),
        string.Join(',', _subtaskIds),
        string.Join(',', _imageMap.OrderBy(kv => kv.Key).Select(kv => kv.Value)),
        string.Join(',', _voiceMap.OrderBy(kv => kv.Key).Select(kv => kv.Value)),
        string.Join(',', Files.Select(f => f.StorageId)),
        string.Join(',', Comments.Select(c => c.Id + ":" + c.EditedAt?.Ticks)));

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Title) or nameof(Markdown) or nameof(Status) or nameof(Priority))
            ScheduleAutoSave();
    }

    /// <summary>
    /// Writes the task once edits pause. The pause matters more than the
    /// delay: every write moves the task's timestamp and refreshes its card.
    /// </summary>
    private void ScheduleAutoSave()
    {
        if (!_main.IsAutoSave || _exiting) return;

        if (_autoSave == null)
        {
            _autoSave = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(900)
            };
            _autoSave.Tick += (_, _) =>
            {
                _autoSave!.Stop();
                SaveIfChanged();
            };
        }

        SaveState = "Saving…";
        _autoSave.Stop();
        _autoSave.Start();
    }

    /// <summary>Saves when anything changed and there is something to save. True if it wrote.</summary>
    private bool SaveIfChanged()
    {
        var signature = Signature();
        if (signature == _savedSignature || (!IsExistingTask && !HasUnsavedContent()))
        {
            SaveState = IsExistingTask ? "Saved" : string.Empty;
            return false;
        }

        _newMediaIds.Clear();
        Persist(string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title);
        _savedSignature = signature;
        SaveState = "Saved";
        return true;
    }

    /// <summary>The Auto Save pill writes now, without leaving.</summary>
    [RelayCommand]
    private void SaveNow()
    {
        _autoSave?.Stop();
        SaveIfChanged();
    }

    [RelayCommand]
    private void Delete()
    {
        if (_existing == null) { DiscardNewMedia(); GoBack(); return; }

        if (_isVault)
        {
            if (!DialogService.Confirm($"Permanently delete \"{Title}\"? Vault items are not sent to trash.",
                    "Delete Vault Item"))
                return;
            App.Database.DeleteTodoPermanently(_existing.Id);
        }
        else
        {
            if (!DialogService.Confirm($"Move \"{Title}\" to trash?", "Delete Task")) return;
            _existing.StatusBeforeTrash = _existing.Status;
            _existing.Status = TodoStatus.Trashed;
            _existing.TrashedAt = DateTime.UtcNow;
            App.Database.UpsertTodo(_existing);
        }
        GoBack();
    }

    public bool HasUnsavedContent()
        => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Markdown)
           || _imageMap.Count > 0 || _voiceMap.Count > 0 || Files.Count > 0 || _subtaskIds.Count > 0
           || Comments.Count > 0;

    public void SaveAsDraft()
    {
        _newMediaIds.Clear();
        Persist(string.IsNullOrWhiteSpace(Title) ? "[DRAFT]" : $"[DRAFT] {Title}");
    }

    private void Persist(string title)
    {
        var attached = _imageMap.OrderBy(kv => kv.Key).Select(kv => kv.Value)
            .Concat(_voiceMap.OrderBy(kv => kv.Key).Select(kv => kv.Value)).ToList();


        if (_isVault && App.Encryption.IsUnlocked)
        {
            var item = _existing != null
                ? App.Database.GetTodoById(_existing.Id) ?? new TodoItem { Id = _existing.Id }
                : new TodoItem();

            item.Priority = Priority;
            item.Tags = TagChips.Select(c => c.Label).ToList();
            item.AttachedImages = attached;
            item.Files = Files.ToList();
            item.SubtaskIds = new List<string>(_subtaskIds);
            item.Comments = StoredComments();
            item.Status = Status;
            item.CompletedAt = Status == TodoStatus.Completed ? DateTime.UtcNow : null;

            var (encTitle, titleIv) = App.Encryption.Encrypt(title);
            var (encDesc, descIv) = App.Encryption.Encrypt(Markdown ?? "");
            item.Title = encTitle;
            item.EncryptionTitleIV = titleIv;
            item.Description = encDesc;
            item.EncryptionIV = descIv;

            App.Database.UpsertVaultTodo(item);
            _existing ??= item;
        }
        else
        {
            var item = _existing ?? new TodoItem();
            var isNew = _existing == null;

            item.Priority = Priority;
            item.Tags = TagChips.Select(c => c.Label).ToList();
            item.AttachedImages = attached;
            item.Files = Files.ToList();
            item.SubtaskIds = new List<string>(_subtaskIds);
            item.Title = title;
            item.Description = Markdown ?? "";
            // A locked vault cannot encrypt them, so they are left as stored.
            if (!_isVault) item.Comments = StoredComments();
            item.Status = Status;
            item.CompletedAt = Status == TodoStatus.Completed ? (item.CompletedAt ?? DateTime.UtcNow) : null;

            if (isNew)
            {
                item.WorkspaceId = _workspaceId;
                item.SortOrder = App.Database.GetNextSortOrder(_workspaceId, Status);
            }
            App.Database.UpsertTodo(item);
            _existing = item;
        }

        foreach (var id in _removedMediaIds) App.Database.DeleteImage(id);
        _removedMediaIds.Clear();
    }

    private void DiscardNewMedia()
    {
        foreach (var id in _newMediaIds) App.Database.DeleteImage(id);
        _newMediaIds.Clear();
    }

    /// <summary>
    /// Releases timers and audio devices when the page goes away. Playback
    /// stops here; the page leaving is not left to do it.
    /// </summary>
    public void Detach()
    {
        _debounce?.Stop();
        _debounce = null;
        _autoSave?.Stop();
        _playbackTicker?.Stop();

        _recorder.RecordingStopped -= OnRecordingStopped;
        _player.PlaybackEnded -= OnPlaybackEnded;
        _player.Dispose();
        _recorder.Dispose();

        _dictationCts?.Cancel();
        _dictationTicker?.Stop();
        _dictationRecorder.Dispose();
        _rewriteCts?.Cancel();
        _rewriteNoteTimer?.Stop();
    }

    /// <summary>Leaves after a delete: nothing is left to save.</summary>
    private void GoBack()
    {
        _exiting = true;
        Detach();
        _main.CloseDetail(_isVault, _workspaceId);
    }

    // ── Media maps ──────────────────────────────────────────────

    /// <summary>
    /// Images occupy the first entries of AttachedImages and voice memos the
    /// rest, in the order their markdown references appear.
    /// </summary>
    private void RebuildMediaMaps(TodoItem item)
    {
        if (string.IsNullOrEmpty(Markdown) || item.AttachedImages.Count == 0) return;

        foreach (Match m in Regex.Matches(Markdown, @"img://(\d+)"))
        {
            if (int.TryParse(m.Groups[1].Value, out var n) && n - 1 < item.AttachedImages.Count)
            {
                _imageMap[n] = item.AttachedImages[n - 1];
                if (n >= _nextImageNum) _nextImageNum = n + 1;
            }
        }

        var imageCount = _imageMap.Count;
        foreach (Match m in Regex.Matches(Markdown, @"voice://(\d+)"))
        {
            if (!int.TryParse(m.Groups[1].Value, out var n)) continue;
            var index = imageCount + n - 1;
            if (index < item.AttachedImages.Count)
            {
                _voiceMap[n] = item.AttachedImages[index];
                if (n >= _nextVoiceNum) _nextVoiceNum = n + 1;
            }
        }
    }
}
