using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>A heading in the outline beside a note's preview.</summary>
public sealed record OutlineEntry(string Text, int Level, int Line, int Index)
{
    /// <summary>Deeper headings sit further in.</summary>
    public Thickness Indent => new(10 + Math.Max(0, Level - 2) * 12, 5, 10, 5);
}

/// <summary>
/// A Markdown file opened as a note. Nothing is written to the file until the
/// user saves; until then the note says it has unsaved changes, and leaving
/// it asks first.
/// </summary>
public partial class NoteViewModel : ObservableObject
{
    [ObservableProperty] private string _markdown;
    [ObservableProperty] private string _editorMode = "Preview";   // Preview | Split | Edit
    [ObservableProperty] private string _renderedHtml = string.Empty;

    /// <summary>The text differs from what the file holds.</summary>
    [ObservableProperty] private bool _isDirty;

    /// <summary>Someone else changed the file while there were unsaved changes here.</summary>
    [ObservableProperty] private bool _changedOnDisk;

    [ObservableProperty] private bool _isMissing;
    [ObservableProperty] private string _savedLabel = string.Empty;

    /// <summary>A passing word on what just happened, such as tasks made.</summary>
    [ObservableProperty] private string? _flash;

    [ObservableProperty] private int _caretLine = 1;

    /// <summary>The folders above the note, from the opened folder it lies in; empty when it lies in none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCrumbs))]
    private IReadOnlyList<NoteCrumb> _crumbs = Array.Empty<NoteCrumb>();

    public bool HasCrumbs => Crumbs.Count > 0;

    /// <summary>Shown in a reader window of its own rather than on the Notes page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOutline), nameof(ShowToolbar), nameof(ShowReaderBar))]
    private bool _isReader;

    /// <summary>The Make tasks dialog over a reader window. On the Notes page the main window holds it.</summary>
    [ObservableProperty] private MakeTasksViewModel? _readerDialog;

    /// <summary>
    /// Raised for a link to another note: a reader window opens it in place.
    /// With no one listening, the note opens on the Notes page.
    /// </summary>
    public event Action<string>? NoteLinkFollowed;

    private readonly MainViewModel _main;
    private NoteText _format;
    private string _savedText;
    private DateTime? _savedAt;
    private FileSystemWatcher? _watcher;
    private System.Windows.Threading.DispatcherTimer? _flashTimer;

    public string Path { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    public MainViewModel Main => _main;

    public ObservableCollection<OutlineEntry> Outline { get; } = new();

    /// <summary>The host the preview serves the note's folder under, so relative images and links resolve.</summary>
    public const string NoteHost = "mikudo-note.example";

    public bool ShowEditor => EditorMode != "Preview";
    public bool ShowPreview => EditorMode != "Edit";
    public bool IsSplit => EditorMode == "Split";
    public bool ShowOutline => !IsReader && EditorMode == "Preview" && Outline.Count > 1;

    /// <summary>A reader window shows the page alone while reading, and the toolbar once editing.</summary>
    public bool ShowToolbar => !IsReader || EditorMode != "Preview";

    /// <summary>The bar along the foot of a reader window, while reading.</summary>
    public bool ShowReaderBar => IsReader && EditorMode == "Preview";
    public bool HasFlash => !string.IsNullOrEmpty(Flash);

    /// <summary>Checklist items not ticked yet: what Make tasks would offer.</summary>
    public int OpenItems { get; private set; }
    public bool HasOpenItems => OpenItems > 0;
    public string OpenItemsLabel => OpenItems == 1 ? "1 open checklist item" : $"{OpenItems} open checklist items";

    public string FormatLabel => (_format.Encoding is System.Text.UnicodeEncoding ? "UTF-16" : "UTF-8")
                                 + " · " + (_format.LineEnding == "\r\n" ? "CRLF" : "LF");
    public string StatusLine => $"Line {CaretLine}  ·  {FormatLabel}  ·  {SavedLabel}";

    public NoteViewModel(MainViewModel main, string path)
    {
        _main = main;
        Path = path;
        _format = NoteFiles.Read(path);
        _markdown = _format.Text;
        _savedText = Normalized(_format.Text);
        SavedLabel = "Opened " + DateTime.Now.ToString("t");
        Refresh();
        Watch();
    }

    private static string Normalized(string text) => NoteFiles.WithLineEndings(text, "\n");

    partial void OnMarkdownChanged(string value)
    {
        IsDirty = Normalized(value) != _savedText;
        Refresh();
    }

    partial void OnEditorModeChanged(string value)
    {
        OnPropertyChanged(nameof(ShowEditor));
        OnPropertyChanged(nameof(ShowPreview));
        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(ShowOutline));
        OnPropertyChanged(nameof(ShowToolbar));
        OnPropertyChanged(nameof(ShowReaderBar));
    }

    /// <summary>A reader window lays the page out across its width, under the note's folder.</summary>
    partial void OnIsReaderChanged(bool value) => Refresh();

    /// <summary>The page again in the theme now on screen; its colours are written into it.</summary>
    public void RefreshTheme() => Refresh();

    /// <summary>A link to another note, followed where this note is shown.</summary>
    public void FollowNoteLink(string path)
    {
        if (NoteLinkFollowed != null) NoteLinkFollowed(path);
        else _main.OpenNote(path);
    }

    partial void OnSavedLabelChanged(string value) => OnPropertyChanged(nameof(StatusLine));
    partial void OnCaretLineChanged(int value) => OnPropertyChanged(nameof(StatusLine));
    partial void OnFlashChanged(string? value) => OnPropertyChanged(nameof(HasFlash));

    /// <summary>The preview, outline and open-item count, from the text as it stands.</summary>
    private void Refresh()
    {
        RenderedHtml = App.Markdown.ToHtml(Markdown ?? string.Empty, reading: true, baseHref: $"https://{NoteHost}/",
                                           kicker: IsReader ? Folder : null);

        var headings = NoteFiles.Headings(Markdown ?? string.Empty).Where(h => h.Level <= 3).ToList();
        var entries = headings.Select((h, i) => new OutlineEntry(h.Text, h.Level, h.Line, i)).ToList();
        if (!entries.SequenceEqual(Outline))
        {
            Outline.Clear();
            foreach (var entry in entries) Outline.Add(entry);
            OnPropertyChanged(nameof(ShowOutline));
        }

        OpenItems = NoteFiles.Checklist(Markdown ?? string.Empty).Sum(i => (i.Done ? 0 : 1) + i.Children.Count(c => !c.Done));
        OnPropertyChanged(nameof(OpenItems));
        OnPropertyChanged(nameof(HasOpenItems));
        OnPropertyChanged(nameof(OpenItemsLabel));
    }

    [RelayCommand]
    private void SetEditorMode(string? mode) => EditorMode = mode ?? "Preview";

    // ── Saving ──────────────────────────────────────────────────

    /// <summary>
    /// Writes the note to its file. If the file changed on disk meanwhile, the
    /// user says whether their version goes over it.
    /// </summary>
    [RelayCommand]
    public void Save()
    {
        if (!IsDirty) return;

        if (ChangedOnDisk && !DialogService.Confirm(
                $"{FileName} was changed outside MikuDo after you started editing. Save your version over it?",
                "The file changed on disk", "Save mine", "Cancel"))
            return;

        try
        {
            _ignoreDiskUntil = DateTime.UtcNow.AddSeconds(2);
            NoteFiles.Write(Path, Markdown, _format);
        }
        catch (Exception ex)
        {
            DialogService.Notify($"{FileName} could not be saved:\n{ex.Message}", "Save note");
            return;
        }

        _savedText = Normalized(Markdown);
        _savedAt = DateTime.Now;
        IsDirty = false;
        ChangedOnDisk = false;
        IsMissing = false;
        SavedLabel = "Saved " + _savedAt.Value.ToString("t");
    }

    /// <summary>Drops the unsaved changes and shows the file as it is on disk.</summary>
    [RelayCommand]
    private void Revert()
    {
        if (IsDirty && !DialogService.Confirm($"Drop the changes you have not saved to {FileName}?", "Revert", "Drop changes", "Keep editing"))
            return;
        LoadFromDisk();
    }

    [RelayCommand]
    private void Reload() => Revert();

    private void LoadFromDisk()
    {
        try
        {
            _format = NoteFiles.Read(Path);
        }
        catch (Exception ex)
        {
            DialogService.Notify($"{FileName} could not be read:\n{ex.Message}", "Reload");
            return;
        }
        _savedText = Normalized(_format.Text);
        Markdown = _format.Text;
        IsDirty = false;
        ChangedOnDisk = false;
        IsMissing = false;
        OnPropertyChanged(nameof(FormatLabel));
        OnPropertyChanged(nameof(StatusLine));
    }

    /// <summary>
    /// Before the note goes off screen: true when it may go. Unsaved changes
    /// ask Save, Don't save or Cancel; a save that fails stays put.
    /// </summary>
    public bool ConfirmLeave()
    {
        if (!IsDirty) return true;

        switch (DialogService.AskToSave($"{FileName} has changes that are not saved to the file yet."))
        {
            case SaveChoice.Save:
                Save();
                return !IsDirty;
            case SaveChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    // ── The file on disk ────────────────────────────────────────

    private DateTime _ignoreDiskUntil;

    /// <summary>
    /// Follows the file. A change made elsewhere is taken in at once when
    /// nothing here is unsaved; over unsaved changes it is only flagged, so
    /// nothing typed is lost.
    /// </summary>
    private void Watch()
    {
        try
        {
            _watcher = new FileSystemWatcher(Folder, FileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler changed = (_, _) => Application.Current?.Dispatcher.BeginInvoke(CheckDisk);
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Renamed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(CheckDisk);
        }
        catch (Exception ex)
        {
            LogService.Error($"Could not watch {Path}", ex);
        }
    }

    private void CheckDisk()
    {
        if (_watcher == null) return;

        if (!File.Exists(Path))
        {
            IsMissing = true;
            return;
        }
        IsMissing = false;

        NoteText onDisk;
        try { onDisk = NoteFiles.Read(Path); }
        catch (IOException) { return; }   // still being written; the next event reads it

        if (Normalized(onDisk.Text) == _savedText) return;
        if (DateTime.UtcNow < _ignoreDiskUntil && Normalized(onDisk.Text) == Normalized(Markdown)) return;

        if (IsDirty)
        {
            ChangedOnDisk = true;
            return;
        }

        LoadFromDisk();
        ShowFlash("Updated from the file on disk");
    }

    /// <summary>Stops following the file once the note leaves the screen.</summary>
    public void Close()
    {
        _watcher?.Dispose();
        _watcher = null;
        _flashTimer?.Stop();
    }

    [RelayCommand]
    private void ShowInFolder() => ShowInExplorer(Path);

    public static void ShowInExplorer(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (System.IO.Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder))
                Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch (Exception ex)
        {
            LogService.Error($"Could not show {path} in Explorer", ex);
        }
    }

    public void ShowFlash(string text)
    {
        Flash = text;
        _flashTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _flashTimer.Stop();
        _flashTimer.Tick -= ClearFlash;
        _flashTimer.Tick += ClearFlash;
        _flashTimer.Start();
    }

    private void ClearFlash(object? sender, EventArgs e)
    {
        _flashTimer?.Stop();
        Flash = null;
    }

    // ── Tasks from the note ─────────────────────────────────────

    /// <summary>The whole checklist, or the whole note as one task.</summary>
    [RelayCommand]
    private void MakeTasks(string? mode)
    {
        if (IsDirty)
        {
            DialogService.Notify("Save the note first, so the tasks match what is in the file.", "Make tasks");
            return;
        }
        var items = NoteFiles.Checklist(Markdown);
        var dialog = MakeTasksViewModel.ForNote(_main, this, items, NoteFiles.TitleOf(Markdown, Path), Markdown);
        if (mode == "whole" || items.Count == 0) dialog.IsPerItem = false;
        OpenDialog(dialog);
    }

    /// <summary>A reader window shows the dialog over itself; the Notes page has the main window show it.</summary>
    private void OpenDialog(MakeTasksViewModel dialog)
    {
        if (IsReader) ReaderDialog = dialog;
        else _main.OpenMakeTasks(dialog);
    }

    /// <summary>Closes this note's Make tasks dialog, wherever it is shown.</summary>
    public void CloseDialog()
    {
        ReaderDialog = null;
        if (_main.MakeTasks?.Note == this) _main.CloseMakeTasksCommand.Execute(null);
    }

    /// <summary>Selected lines in the editor, as a task each or as one task.</summary>
    public void MakeTasksFromSelection(string selection, int firstLine, bool asOne)
    {
        var items = NoteFiles.Passage(selection, firstLine);
        if (items.Count == 0) return;

        var title = items[0].Text;
        var rest = string.Join(Environment.NewLine,
            selection.Replace("\r\n", "\n").Split('\n').SkipWhile(l => l.Trim().Length == 0).Skip(1)).Trim();
        var dialog = MakeTasksViewModel.ForSelection(_main, this, items, title, rest);
        dialog.IsPerItem = !asOne;
        OpenDialog(dialog);
    }

    /// <summary>
    /// One checklist item, straight onto the last board opened: the + Task on
    /// an item in the preview. The item is found by its text.
    /// </summary>
    public void AddTaskForItem(string text)
    {
        var item = NoteFiles.Checklist(Markdown).FirstOrDefault(i => Same(i.Text, text));
        if (item == null) return;

        var workspace = _main.Workspaces.FirstOrDefault(w => w.Id == _main.LastWorkspaceId) ?? _main.Workspaces.FirstOrDefault();
        var made = MakeTasksViewModel.Create(_main, new[] { item }, workspace?.Id, TodoStatus.Active, null, Path);
        ShowFlash($"Added to {workspace?.Name ?? "My Todos"}");
        _ = made;
        TasksChanged?.Invoke();
    }

    /// <summary>Titles of the tasks already made from this note, for the preview to mark.</summary>
    public HashSet<string> TasksMade()
        => App.Database.GetAllBoardTodos()
                       .Where(t => t.NotePath != null && MainViewModel.SamePath(t.NotePath, Path))
                       .Select(t => Key(t.Title))
                       .ToHashSet();

    /// <summary>Raised when tasks were made from this note, so the preview can mark them.</summary>
    public event Action? TasksChanged;

    public void RaiseTasksChanged() => TasksChanged?.Invoke();

    public static string Key(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    private static bool Same(string a, string b) => Key(a) == Key(b);
}
