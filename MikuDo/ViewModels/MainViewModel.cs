using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;
using MikuDo.Views;

namespace MikuDo.ViewModels;

public enum AppPage { Dashboard, Board, TaskDetail, Vault, Trash, Settings, About, Note }

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private AppPage _currentPage = AppPage.Board;
    [ObservableProperty] private bool _isDark;
    [ObservableProperty] private string? _selectedWorkspaceId;
    [ObservableProperty] private ObservableCollection<WorkspaceNavItem> _workspaces = new();
    [ObservableProperty] private bool _isAddingWorkspace;
    [ObservableProperty] private string _newWorkspaceName = string.Empty;
    [ObservableProperty] private string _globalSearch = string.Empty;
    [ObservableProperty] private bool _screenshotAllScreens;
    [ObservableProperty] private bool _isSidebarCollapsed;

    [ObservableProperty] private AddTaskViewModel? _addTask;
    [ObservableProperty] private AiImportViewModel? _aiImport;
    [ObservableProperty] private GalleryViewModel? _gallery;
    [ObservableProperty] private ExportViewModel? _export;
    [ObservableProperty] private LabelSuggestViewModel? _labelSuggest;
    [ObservableProperty] private MakeTasksViewModel? _makeTasks;

    /// <summary>The task page saves as it goes and on the way out, instead of waiting for Save.</summary>
    [ObservableProperty] private bool _isAutoSave;

    /// <summary>Boards show a subtask under its parent rather than as a card of its own.</summary>
    [ObservableProperty] private bool _groupByParent;

    // ── Tips ──

    /// <summary>Tips are shown at the foot of the sidebar. Settings switches them off for good.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTip))]
    private bool _isTipsOn;

    /// <summary>Closed with its ×: gone until the app next starts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTip))]
    private bool _isTipDismissed;

    [ObservableProperty] private string _tip = string.Empty;

    public bool ShowTip => IsTipsOn && !IsTipDismissed;

    private int _tipIndex;
    private System.Windows.Threading.DispatcherTimer? _tipTimer;

    /// <summary>A tip stays this long before the next takes its place: long enough to be ignored.</summary>
    private static readonly TimeSpan TipInterval = TimeSpan.FromMinutes(4);

    // ── Sidebar GIF ──

    /// <summary>
    /// The app's own copy of the GIF chosen in Settings, or null while none is.
    /// The box under the tip is there only while one is chosen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSidebarGif))]
    private string? _sidebarGifPath;

    public bool HasSidebarGif => SidebarGifPath != null;

    /// <summary>Files this size or larger are turned away: decoding them is slow and they hold no more than the box shows.</summary>
    private const long MaxGifBytes = 40L * 1024 * 1024;

    private readonly Dictionary<string, BoardViewModel> _boards = new();
    private BoardViewModel? _searchBoard;
    private VaultViewModel? _vaultVm;
    private TrashViewModel? _trashVm;
    private DashboardViewModel? _dashboardVm;
    private string? _workspaceBeforeSearch;
    private bool _searchNavigating;

    // Views are kept alongside their view models and reused. Building one means
    // parsing its BAML and raising a whole visual tree, which is what made
    // returning to a page cost about as much as opening it the first time.
    private readonly Dictionary<string, BoardView> _boardViews = new();
    private BoardView? _searchView;
    private DashboardView? _dashboardView;
    private VaultView? _vaultView;
    private TrashView? _trashView;
    private SettingsView? _settingsView;
    private AboutView? _aboutView;
    private TaskDetailView? _detailView;
    private NoteView? _noteView;

    /// <summary>Set by MainWindow so screenshots can hide the app first.</summary>
    public Window? AppWindow { get; set; }

    public MainViewModel()
    {
        _isDark = App.IsDark;
        _screenshotAllScreens = App.Database.GetSetting("ScreenshotAllScreens") == "True";
        _isSidebarCollapsed = App.Database.GetSetting("SidebarCollapsed") == "True";
        _isAutoSave = App.Database.GetSetting("AutoSave") == "True";
        _groupByParent = App.Database.GetSetting("GroupByParent") == "True";
        _isTipsOn = App.Database.GetSetting("ShowTips") != "False";
        _sidebarGifPath = StoredGif();
        StartTips();
        // A tip names keys; when they change it is written again.
        Shortcuts.Changed += () => ShowTipAt(_tipIndex);
        ReloadWorkspaces();
        ReloadRecentNotes();
        LoadNoteTree();
        NavigateToWorkspace(Workspaces.FirstOrDefault());
    }

    partial void OnScreenshotAllScreensChanged(bool value)
        => App.Database.SaveSetting("ScreenshotAllScreens", value ? "True" : "False");

    partial void OnIsSidebarCollapsedChanged(bool value)
        => App.Database.SaveSetting("SidebarCollapsed", value ? "True" : "False");

    partial void OnIsAutoSaveChanged(bool value)
        => App.Database.SaveSetting("AutoSave", value ? "True" : "False");

    [RelayCommand]
    private void SetAutoSave(string? value) => IsAutoSave = value == "On";

    /// <summary>Every board reloads when it is shown, so only the one on screen needs it now.</summary>
    partial void OnGroupByParentChanged(bool value)
    {
        App.Database.SaveSetting("GroupByParent", value ? "True" : "False");
        RefreshCurrentPage();
    }

    [RelayCommand]
    private void ToggleGroupByParent() => GroupByParent = !GroupByParent;

    /// <summary>Each start of the app picks up the tips where the last one stopped.</summary>
    private void StartTips()
    {
        _tipIndex = int.TryParse(App.Database.GetSetting("TipIndex"), out var last) ? last + 1 : 0;
        ShowTipAt(_tipIndex);
        if (IsTipsOn) RunTipTimer(true);
    }

    /// <summary>Shows the tip at <paramref name="index"/>, or the next one on when its command has no keys to name.</summary>
    private void ShowTipAt(int index, int direction = 1)
    {
        for (var tries = 0; tries < Tips.All.Length; tries++, index += direction)
        {
            _tipIndex = ((index % Tips.All.Length) + Tips.All.Length) % Tips.All.Length;
            if (Tips.Resolve(Tips.All[_tipIndex]) is not { } text) continue;
            Tip = text;
            break;
        }
        App.Database.SaveSetting("TipIndex", _tipIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void RunTipTimer(bool run)
    {
        if (!run)
        {
            _tipTimer?.Stop();
            return;
        }

        _tipTimer ??= new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TipInterval
        };
        _tipTimer.Tick -= OnTipTick;
        _tipTimer.Tick += OnTipTick;
        _tipTimer.Start();
    }

    private void OnTipTick(object? sender, EventArgs e) => ShowTipAt(_tipIndex + 1);

    partial void OnIsTipsOnChanged(bool value)
    {
        App.Database.SaveSetting("ShowTips", value ? "True" : "False");
        if (value) IsTipDismissed = false;
        RunTipTimer(value && !IsTipDismissed);
    }

    [RelayCommand]
    private void SetTips(string? value) => IsTipsOn = value == "On";

    /// <summary>Moving through tips by hand restarts the wait, so a tip is never swapped while being read.</summary>
    [RelayCommand]
    private void NextTip()
    {
        ShowTipAt(_tipIndex + 1);
        RunTipTimer(ShowTip);
    }

    [RelayCommand]
    private void PreviousTip()
    {
        ShowTipAt(_tipIndex - 1, direction: -1);
        RunTipTimer(ShowTip);
    }

    [RelayCommand]
    private void DismissTip()
    {
        IsTipDismissed = true;
        RunTipTimer(false);
    }

    /// <summary>Chosen GIFs are copied in beside the database, so moving or deleting the original loses nothing.</summary>
    private static string MediaFolder
        => Path.Combine(Path.GetDirectoryName(App.Database.GetDatabasePath())!, "media");

    private static string? StoredGif()
    {
        var name = App.Database.GetSetting("SidebarGif");
        if (string.IsNullOrEmpty(name)) return null;
        var path = Path.Combine(MediaFolder, name);
        return File.Exists(path) ? path : null;
    }

    [RelayCommand]
    private void ChooseGif()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a GIF for the sidebar",
            Filter = "GIF images (*.gif)|*.gif|Images (*.gif;*.png;*.jpg;*.jpeg;*.bmp)|*.gif;*.png;*.jpg;*.jpeg;*.bmp"
        };
        if (dialog.ShowDialog() != true) return;
        UseGif(dialog.FileName);
    }

    /// <summary>
    /// Copies <paramref name="file"/> in and shows it. The copy gets a new
    /// name each time, so the box notices the change and nothing still
    /// reading the last one is overwritten.
    /// </summary>
    public void UseGif(string file)
    {
        try
        {
            if (new FileInfo(file).Length >= MaxGifBytes)
            {
                DialogService.Notify("That file is over 40 MB. Pick a smaller GIF; the box shows it at a small size anyway.");
                return;
            }

            Directory.CreateDirectory(MediaFolder);
            var name = $"sidebar-{Guid.NewGuid():N}{Path.GetExtension(file).ToLowerInvariant()}";
            File.Copy(file, Path.Combine(MediaFolder, name));

            var previous = SidebarGifPath;
            SidebarGifPath = Path.Combine(MediaFolder, name);
            App.Database.SaveSetting("SidebarGif", name);
            DeleteQuietly(previous);
        }
        catch (Exception ex)
        {
            LogService.Error("Could not use the chosen GIF", ex);
            DialogService.Notify("That file could not be used. Try another GIF.");
        }
    }

    [RelayCommand]
    private void RemoveGif()
    {
        var previous = SidebarGifPath;
        SidebarGifPath = null;
        App.Database.SaveSetting("SidebarGif", string.Empty);
        DeleteQuietly(previous);
    }

    private static void DeleteQuietly(string? path)
    {
        if (path == null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    // ── Sidebar ─────────────────────────────────────────────────

    public void ReloadWorkspaces()
    {
        var counts = App.Database.GetWorkspaceOpenCounts();

        var defaultName = App.Database.GetDefaultWorkspaceName();
        var items = new List<WorkspaceNavItem> { new(null, counts.GetValueOrDefault(""), defaultName) };
        items.AddRange(App.Database.GetAllWorkspaces()
            .Select(ws => new WorkspaceNavItem(ws, counts.GetValueOrDefault(ws.Id.ToString()), defaultName)));

        Workspaces = new ObservableCollection<WorkspaceNavItem>(items);
        SyncNavHighlight();
    }

    /// <summary>Refreshes badge counts without rebuilding the list.</summary>
    public void RefreshCounts()
    {
        var counts = App.Database.GetWorkspaceOpenCounts();
        foreach (var item in Workspaces)
            item.Count = counts.GetValueOrDefault(item.Id ?? "");
    }

    private void SyncNavHighlight()
    {
        var onBoard = CurrentPage is AppPage.Board or AppPage.TaskDetail;
        foreach (var item in Workspaces)
            item.IsActive = onBoard && item.Id == SelectedWorkspaceId;

        SyncWorkspaceSquares();
        MarkNoteRows();
    }

    // ── Navigation ──────────────────────────────────────────────

    [RelayCommand]
    public void NavigateToWorkspace(WorkspaceNavItem? item)
    {
        if (item == null || !LeaveCurrentPage()) return;

        SelectedWorkspaceId = item.Id;
        LastWorkspaceId = item.Id;
        CurrentPage = AppPage.Board;

        var key = item.Id ?? "";
        if (!_boards.TryGetValue(key, out var board))
        {
            board = new BoardViewModel(this, item.Id, item.Name);
            _boards[key] = board;
        }
        board.WorkspaceName = item.Name;
        board.Reload();

        if (!_boardViews.TryGetValue(key, out var view))
        {
            view = new BoardView();
            _boardViews[key] = view;
        }
        view.DataContext = board;
        CurrentView = view;
        SyncNavHighlight();
        RefreshCounts();
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        if (!LeaveCurrentPage()) return;
        CurrentPage = AppPage.Dashboard;
        SelectedWorkspaceId = null;
        _dashboardVm ??= new DashboardViewModel(this);
        _dashboardVm.Reload();
        _dashboardView ??= new DashboardView();
        _dashboardView.DataContext = _dashboardVm;
        CurrentView = _dashboardView;
        SyncNavHighlight();
    }

    [RelayCommand]
    private void NavigateToVault()
    {
        if (!LeaveCurrentPage(lockVault: false)) return;
        CurrentPage = AppPage.Vault;
        SelectedWorkspaceId = null;
        _vaultVm ??= new VaultViewModel(this);
        _vaultVm.Refresh();
        _vaultView ??= new VaultView();
        _vaultView.DataContext = _vaultVm;
        CurrentView = _vaultView;
        SyncNavHighlight();
    }

    [RelayCommand]
    private void NavigateToTrash()
    {
        if (!LeaveCurrentPage()) return;
        CurrentPage = AppPage.Trash;
        SelectedWorkspaceId = null;
        _trashVm ??= new TrashViewModel(this);
        _trashVm.Reload();
        _trashView ??= new TrashView();
        _trashView.DataContext = _trashVm;
        CurrentView = _trashView;
        SyncNavHighlight();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        if (!LeaveCurrentPage()) return;
        CurrentPage = AppPage.Settings;
        SelectedWorkspaceId = null;
        _settingsView ??= new SettingsView();
        (_settingsView.DataContext as SettingsViewModel)?.Detach();
        _settingsView.DataContext = new SettingsViewModel(this);
        CurrentView = _settingsView;
        SyncNavHighlight();
    }

    [RelayCommand]
    private void NavigateToAbout()
    {
        if (!LeaveCurrentPage()) return;
        CurrentPage = AppPage.About;
        SelectedWorkspaceId = null;
        _aboutView ??= new AboutView();
        _aboutView.DataContext = new AboutViewModel();
        CurrentView = _aboutView;
        SyncNavHighlight();
    }

    /// <summary>Opens the detail page for a task. <paramref name="parentLabel"/> labels the back button.</summary>
    public void OpenTask(TodoItem item, bool isVault, string parentLabel)
    {
        if (!isVault && !item.IsVault) RememberOpened(TaskKind, item.IdText);
        var detail = new TaskDetailViewModel(this, item, isVault, item.WorkspaceId, parentLabel);
        ShowDetail(detail);
    }

    public void OpenNewTask(TodoStatus status, bool isVault, string? workspaceId, string parentLabel)
    {
        var detail = new TaskDetailViewModel(this, null, isVault, workspaceId, parentLabel) { Status = status };
        ShowDetail(detail);
    }

    private void ShowDetail(TaskDetailViewModel detail)
    {
        CurrentPage = AppPage.TaskDetail;

        // One detail view is reused for every task: it hosts a WebView2, and
        // spinning up a fresh browser is by far the slowest thing the app does.
        _detailView ??= new TaskDetailView();
        _detailView.DataContext = detail;
        CurrentView = _detailView;
        SyncNavHighlight();
    }

    /// <summary>Returns from the detail page to whichever list opened it.</summary>
    public void CloseDetail(bool wasVault, string? workspaceId)
    {
        if (wasVault)
        {
            NavigateToVault();
            return;
        }
        var item = Workspaces.FirstOrDefault(w => w.Id == workspaceId) ?? Workspaces.FirstOrDefault();
        NavigateToWorkspace(item);
    }

    /// <summary>
    /// Leaves the page on screen. False when the user chose to stay: a note
    /// with changes not saved to its file asks first.
    /// </summary>
    private bool LeaveCurrentPage(bool lockVault = true)
    {
        if (OpenNotePage is { } note)
        {
            if (!note.ConfirmLeave()) return false;
            note.Close();
        }

        // Navigating elsewhere does not wait for the page: it saves and stops
        // any recording on its own, and refreshes whatever is on screen after.
        if (CurrentView is TaskDetailView { DataContext: TaskDetailViewModel detail })
            _ = detail.ExitAsync(DetailExit.Away);

        if (lockVault && CurrentPage == AppPage.Vault && App.Encryption.IsUnlocked)
        {
            App.Encryption.LockVault();
            _vaultVm?.OnLockedExternally();
        }
        return true;
    }

    /// <summary>
    /// The task page open as the app closes, if any. The window waits on its
    /// exit before going, since a recording still running has to finish first.
    /// </summary>
    public TaskDetailViewModel? OpenTaskPage
        => CurrentView is TaskDetailView { DataContext: TaskDetailViewModel detail } ? detail : null;

    /// <summary>The note on screen, if any.</summary>
    public NoteViewModel? OpenNotePage
        => CurrentView is NoteView { DataContext: NoteViewModel note } ? note : null;

    /// <summary>The board last opened, where tasks made from a note go unless told otherwise.</summary>
    public string? LastWorkspaceId { get; private set; }

    // ── Notes ───────────────────────────────────────────────────

    /// <summary>Notes opened lately, newest first: the rows of the sidebar's Recent group.</summary>
    public ObservableCollection<NoteRow> RecentNotes { get; } = new();

    private static NoteRow RecentRow(string path) => new() { Depth = 1, Path = path, IsMissing = !File.Exists(path) };

    private const int RecentNoteLimit = 12;

    public static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void ReloadRecentNotes()
    {
        RecentNotes.Clear();
        var stored = App.Database.GetSetting("RecentNotes");
        if (string.IsNullOrEmpty(stored)) return;
        try
        {
            foreach (var path in System.Text.Json.JsonSerializer.Deserialize<List<string>>(stored) ?? new())
                RecentNotes.Add(RecentRow(path));
        }
        catch (System.Text.Json.JsonException) { }
    }

    private void SaveRecentNotes()
        => App.Database.SaveSetting("RecentNotes",
            System.Text.Json.JsonSerializer.Serialize(RecentNotes.Select(n => n.Path).ToList()));

    /// <summary>Adds a note to the sidebar without opening it.</summary>
    public void RememberNotePath(string path) => RememberNote(Path.GetFullPath(path));

    /// <summary>Puts a note at the top of the list, keeping the list short.</summary>
    private void RememberNote(string path)
    {
        var known = RecentNotes.FirstOrDefault(n => SamePath(n.Path, path));
        if (known != null)
        {
            RecentNotes.Remove(known);
            known.IsMissing = !File.Exists(path);
        }
        RecentNotes.Insert(0, known ?? RecentRow(path));
        while (RecentNotes.Count > RecentNoteLimit) RecentNotes.RemoveAt(RecentNotes.Count - 1);
        SaveRecentNotes();
        SyncRecentRows();
    }

    /// <summary>Takes a note off the sidebar. The file itself is left alone.</summary>
    [RelayCommand]
    public void ForgetNote(NoteRow? item)
    {
        if (item == null || !RecentNotes.Remove(item)) return;
        SaveRecentNotes();
        SyncRecentRows();
    }

    [RelayCommand]
    private void OpenNoteRow(NoteRow? item)
    {
        if (item != null) OpenNote(item.Path);
    }

    [RelayCommand]
    private void ShowNoteInFolder(NoteRow? item)
    {
        if (item != null) NoteViewModel.ShowInExplorer(item.Path);
    }

    /// <summary>Ctrl+O: picks a Markdown file and opens it.</summary>
    [RelayCommand]
    private void OpenNoteFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open a note",
            Filter = "Markdown|*.md;*.markdown;*.mdown;*.mkd|All files|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(AppWindow) == true) OpenNote(dialog.FileName);
    }

    [RelayCommand]
    private void NewNote() => CreateNote(null);

    /// <summary>Asks where to save a new note, starting in <paramref name="folder"/>, creates it, and opens it ready to write.</summary>
    private void CreateNote(string? folder)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "New note",
            FileName = "Untitled.md",
            Filter = "Markdown|*.md",
            DefaultExt = ".md",
            AddExtension = true
        };
        if (folder != null) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(AppWindow) != true) return;

        try
        {
            var title = Path.GetFileNameWithoutExtension(dialog.FileName);
            File.WriteAllText(dialog.FileName, $"# {title}{Environment.NewLine}{Environment.NewLine}", new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            DialogService.Notify($"The note could not be created:\n{ex.Message}", "New note");
            return;
        }
        // The folder's watcher would find it too, a moment later; looking now shows it as it opens.
        foreach (var opened in OpenedFolders.Where(f => f.Holds(dialog.FileName))) Rescan(opened);
        OpenNote(dialog.FileName, "Split");
    }

    /// <summary>
    /// Opens a Markdown file as a note: in Preview unless <paramref name="mode"/>
    /// says otherwise. Opening the note already on screen only brings it forward.
    /// </summary>
    public void OpenNote(string path, string? mode = null)
    {
        try { path = Path.GetFullPath(path); }
        catch { return; }

        if (!File.Exists(path))
        {
            var known = RecentNotes.FirstOrDefault(n => SamePath(n.Path, path));
            if (known != null) known.IsMissing = true;
            DialogService.Notify($"{Path.GetFileName(path)} is no longer at\n{Path.GetDirectoryName(path)}", "Note not found");
            return;
        }

        if (OpenNotePage is { } current && SamePath(current.Path, path))
        {
            if (mode != null) current.EditorMode = mode;
            return;
        }

        // A note open in a reader window stays there, so one file is never edited in two places.
        if (NoteWindow.Find(path) is { } reader)
        {
            reader.BringForward();
            return;
        }

        NoteViewModel note;
        try
        {
            note = new NoteViewModel(this, path);
        }
        catch (Exception ex)
        {
            DialogService.Notify($"{Path.GetFileName(path)} could not be opened:\n{ex.Message}", "Open note");
            return;
        }

        if (!ShowNote(note, mode)) note.Close();
    }

    /// <summary>
    /// Puts an open note on the Notes page: one just read from its file, or one
    /// coming back from a reader window with its unsaved changes. False when
    /// the page on screen would not let go.
    /// </summary>
    public bool ShowNote(NoteViewModel note, string? mode = null)
    {
        if (!LeaveCurrentPage()) return false;

        note.IsReader = false;
        if (mode != null) note.EditorMode = mode;
        note.Crumbs = CrumbsFor(note.Path);
        note.PropertyChanged -= OnShownNoteChanged;
        note.PropertyChanged += OnShownNoteChanged;

        CurrentPage = AppPage.Note;
        SelectedWorkspaceId = null;
        _noteView ??= new NoteView();
        _noteView.DataContext = note;
        CurrentView = _noteView;

        RememberNote(note.Path);
        RememberOpened(NoteKind, note.Path);
        SyncNavHighlight();
        return true;
    }

    private void OnShownNoteChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteViewModel.IsDirty)) MarkNoteRows();
    }

    /// <summary>Moves the note on the Notes page into a reader window of its own, unsaved changes and all.</summary>
    [RelayCommand]
    private void PopOutNote()
    {
        if (OpenNotePage is not { } note) return;

        // The page lets go of the note first, so leaving it neither asks about the note nor closes it.
        note.PropertyChanged -= OnShownNoteChanged;
        _noteView!.DataContext = null;
        NavigateToWorkspace(Workspaces.FirstOrDefault(w => w.Id == LastWorkspaceId) ?? Workspaces.FirstOrDefault());
        NoteWindow.Adopt(this, note);
    }

    /// <summary>A note from the sidebar in a reader window; the one on the Notes page moves there.</summary>
    [RelayCommand]
    private void OpenNoteInWindow(NoteRow? row)
    {
        if (row == null) return;
        if (OpenNotePage is { } shown && SamePath(shown.Path, row.Path)) PopOutNote();
        else NoteWindow.Open(this, row.Path);
    }

    /// <summary>Empties the Recent list. The files themselves are left alone.</summary>
    [RelayCommand]
    private void ClearRecentNotes()
    {
        if (RecentNotes.Count == 0) return;
        RecentNotes.Clear();
        SaveRecentNotes();
        SyncRecentRows();
    }

    public void OpenMakeTasks(MakeTasksViewModel dialog) => MakeTasks = dialog;

    [RelayCommand]
    private void CloseMakeTasks() => MakeTasks = null;

    // ── Global search ───────────────────────────────────────────

    partial void OnGlobalSearchChanged(string value)
    {
        if (_searchNavigating) return;
        _searchNavigating = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (OpenNotePage != null && !LeaveCurrentPage())
                {
                    GlobalSearch = string.Empty;
                    return;
                }
                _workspaceBeforeSearch ??= SelectedWorkspaceId;
                _searchBoard ??= new BoardViewModel(this, null, "Search", allWorkspaces: true);
                _searchBoard.Reload();
                _searchBoard.SearchQuery = value;

                if (CurrentView is not BoardView { DataContext: BoardViewModel b } || !b.AllWorkspaces)
                {
                    CurrentPage = AppPage.Board;
                    SelectedWorkspaceId = null;
                    _searchView ??= new BoardView();
                    _searchView.DataContext = _searchBoard;
                    CurrentView = _searchView;
                    SyncNavHighlight();
                }
            }
            else if (_searchBoard != null && CurrentView is BoardView { DataContext: BoardViewModel bv } && bv.AllWorkspaces)
            {
                var target = Workspaces.FirstOrDefault(w => w.Id == _workspaceBeforeSearch) ?? Workspaces.FirstOrDefault();
                _workspaceBeforeSearch = null;
                _searchNavigating = false;
                NavigateToWorkspace(target);
            }
        }
        finally
        {
            _searchNavigating = false;
        }
    }

    [RelayCommand]
    private void ClearGlobalSearch() => GlobalSearch = string.Empty;

    // ── Theme ───────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleTheme()
    {
        // Between the light and the dark theme used last: Sun and Moon at first.
        var key = IsDark ? "LastLightTheme" : "LastDarkTheme";
        var fallback = IsDark ? ThemeCatalog.Sun : ThemeCatalog.Moon;
        var next = App.Database.GetSetting(key) is { } id ? ThemeCatalog.Resolve(id) : fallback;
        ApplyTheme(next.IsDark == IsDark ? fallback : next);
    }

    /// <summary>The theme Settings shows as chosen.</summary>
    [ObservableProperty] private string _themeId = App.CurrentTheme.Id;

    [RelayCommand]
    private void SelectTheme(string? id) => ApplyTheme(ThemeCatalog.Resolve(id));

    /// <summary>The layout Settings shows as chosen: Islands or Flat.</summary>
    public string LayoutId => Controls.Layout.Current.Id;

    /// <summary>Islands or Flat. Every page follows on the spot.</summary>
    [RelayCommand]
    private void SelectLayout(string? id)
    {
        var flat = id == Controls.Layout.Flat;
        if (Controls.Layout.Current.IsFlat == flat) return;

        Controls.Layout.Current.IsFlat = flat;
        App.Database.SaveSetting("Layout", Controls.Layout.Current.Id);
        OnPropertyChanged(nameof(LayoutId));
    }

    private void ApplyTheme(AppTheme theme)
    {
        if (theme.Id == App.CurrentTheme.Id) return;

        App.SetTheme(theme);
        App.Database.SaveSetting(theme.IsDark ? "LastDarkTheme" : "LastLightTheme", theme.Id);
        IsDark = theme.IsDark;
        ThemeId = theme.Id;

        // A note's colours are written into its page, so each one on screen is laid out again.
        foreach (var reader in NoteWindow.All) reader.Note.RefreshTheme();

        // Chip and status brushes are captured per item, so rebuild what is on screen.
        if (CurrentView is TaskDetailView { DataContext: TaskDetailViewModel detail })
        {
            detail.RefreshTheme();
            return;
        }

        _boards.Clear();
        _searchBoard = null;
        _dashboardVm = null;
        _trashVm = null;

        switch (CurrentPage)
        {
            case AppPage.Dashboard: NavigateToDashboardCommand.Execute(null); break;
            case AppPage.Trash: NavigateToTrashCommand.Execute(null); break;
            // Drawn entirely from the theme's tokens, so it recolours where it stands.
            case AppPage.Settings: break;
            case AppPage.About: NavigateToAboutCommand.Execute(null); break;
            case AppPage.Vault: _vaultVm?.RefreshTheme(); break;
            case AppPage.Note: OpenNotePage?.RefreshTheme(); break;
            default:
                NavigateToWorkspace(Workspaces.FirstOrDefault(w => w.Id == SelectedWorkspaceId)
                                    ?? Workspaces.FirstOrDefault());
                break;
        }
    }

    // ── Workspace management ────────────────────────────────────

    [RelayCommand]
    private void ShowAddWorkspace()
    {
        IsAddingWorkspace = true;
        NewWorkspaceName = string.Empty;
    }

    [RelayCommand]
    private void CancelAddWorkspace()
    {
        IsAddingWorkspace = false;
        NewWorkspaceName = string.Empty;
    }

    [RelayCommand]
    private void ConfirmAddWorkspace()
    {
        var name = NewWorkspaceName.Trim();
        if (name.Length == 0) return;

        var ws = new Workspace { Name = name, SortOrder = Workspaces.Count };
        App.Database.UpsertWorkspace(ws);

        IsAddingWorkspace = false;
        NewWorkspaceName = string.Empty;
        ReloadWorkspaces();
        NavigateToWorkspace(Workspaces.FirstOrDefault(w => w.Id == ws.Id.ToString()));
    }

    [RelayCommand]
    private void DeleteWorkspace(WorkspaceNavItem? item)
    {
        if (item?.Workspace == null) return;
        if (!Services.DialogService.Confirm(
                $"Delete workspace \"{item.Name}\"? Its tasks move to My Todos.", "Delete Workspace"))
            return;

        App.Database.DeleteWorkspace(item.Workspace.Id);
        _boards.Remove(item.Id!);
        ReloadWorkspaces();
        NavigateToWorkspace(Workspaces.FirstOrDefault());
    }

    /// <summary>
    /// Moves tasks into another workspace, from a drag onto the sidebar or a
    /// card menu. A moved card lands among the unplaced cards of its new
    /// board; where it sat on the board it left means nothing there.
    /// </summary>
    /// <remarks>
    /// Every board reloads when it is navigated to, so only the page on screen
    /// needs refreshing here.
    /// </remarks>
    public void MoveTasksToWorkspace(IEnumerable<TodoItem> items, string? workspaceId)
    {
        var parents = items.Where(t => t.WorkspaceId != workspaceId).ToList();
        if (parents.Count == 0) return;

        // A task takes everything beneath it along, so no subtask is left on
        // a board its parent has gone from.
        var moving = parents.Concat(App.Database.GetSubtaskTree(parents))
                            .Where(t => t.WorkspaceId != workspaceId)
                            .ToList();

        var snapshots = moving.Select(t => t.Snapshot()).ToList();
        foreach (var todo in moving)
        {
            todo.WorkspaceId = workspaceId;
            todo.SortOrder = App.Database.GetNextSortOrder(workspaceId, todo.Status);
            todo.IsPlaced = false;
            App.Database.UpsertTodo(todo);
        }

        var name = Workspaces.FirstOrDefault(w => w.Id == workspaceId)?.Name ?? "workspace";
        PushUndo($"move to {name}", SelectedWorkspaceId, () =>
        {
            foreach (var snapshot in snapshots) App.Database.UpsertTodo(snapshot);
        });

        RefreshCurrentPage();
    }

    // ── Finishing a task's subtasks along with it ──────────────

    /// <summary>
    /// Everything beneath <paramref name="finished"/>, however deep, that is
    /// not done yet. Trashed tasks are left out, and so is whatever hangs only
    /// beneath one: neither shows under the task any more.
    /// </summary>
    public static List<TodoItem> OpenSubtasksBeneath(IReadOnlyCollection<TodoItem> finished)
    {
        var parents = finished.Where(t => t.SubtaskIds.Count > 0).ToList();
        if (parents.Count == 0) return new List<TodoItem>();

        return App.Database.GetSubtaskTree(parents, includeTrashed: false)
                  .Where(t => t.Status != TodoStatus.Completed)
                  .ToList();
    }

    /// <summary>
    /// Once tasks have just gone to Done, asks whether everything still open
    /// beneath them should be done too, and does it on a yes. Nothing is asked
    /// when there is nothing left to finish.
    /// </summary>
    /// <remarks>
    /// The question waits until the move has landed and the code that made it
    /// has returned. A drop is handled while the drag is still in progress, and
    /// a dialog opened from there would sit on top of the drag.
    /// </remarks>
    public void OfferToFinishSubtasks(IReadOnlyCollection<TodoItem> finished, Action? afterwards = null)
    {
        var open = OpenSubtasksBeneath(finished);
        if (open.Count == 0) return;

        var question = FinishQuestion(finished, open);
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!DialogService.Ask(question, "Finish the subtasks too?", "Mark all done", "Leave them")) return;
            FinishSubtasks(open);
            afterwards?.Invoke();
        });
    }

    private static string FinishQuestion(IReadOnlyCollection<TodoItem> finished, List<TodoItem> open)
    {
        var direct = finished.SelectMany(t => t.SubtaskIds).ToHashSet(StringComparer.Ordinal);
        var nested = open.Count(t => !direct.Contains(t.IdText));
        var who = finished.Count == 1 ? $"\"{finished.First().Title}\" has" : "The tasks you moved have";
        var count = open.Count == 1 ? "1 subtask that isn't" : $"{open.Count} subtasks that aren't";

        if (nested == 0)
            return $"{who} {count} done yet. Mark {(open.Count == 1 ? "it" : "them")} done too?";
        return $"{who} {count} done yet, {nested} of them nested under other subtasks. Mark them all done too?";
    }

    /// <summary>
    /// Marks <paramref name="tasks"/> done as one step Undo takes back. Each is
    /// read afresh first, so a task finished or trashed meanwhile is left alone.
    /// </summary>
    public int FinishSubtasks(IEnumerable<TodoItem> tasks)
    {
        var fresh = tasks.Select(t => App.Database.GetTodoById(t.Id))
                         .OfType<TodoItem>()
                         .Where(t => !t.IsVault && t.Status is not (TodoStatus.Completed or TodoStatus.Trashed))
                         .ToList();
        if (fresh.Count == 0) return 0;

        var snapshots = fresh.Select(t => t.Snapshot()).ToList();
        foreach (var task in fresh)
        {
            // It changes column, so it lands by date and priority like any card moved by a button.
            task.IsPlaced = false;
            task.Status = TodoStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            App.Database.UpsertTodo(task);
        }

        PushUndo(fresh.Count > 1 ? "complete subtasks" : "complete subtask", SelectedWorkspaceId, () =>
        {
            foreach (var snapshot in snapshots) App.Database.UpsertTodo(snapshot);
        });
        RefreshCurrentPage();
        return fresh.Count;
    }

    // ── Renaming workspaces ─────────────────────────────────────

    [RelayCommand]
    private void StartRenameWorkspace(WorkspaceNavItem? item)
    {
        if (item == null) return;
        foreach (var other in Workspaces) other.IsRenaming = false;
        item.DraftName = item.Name;
        item.IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRenameWorkspace(WorkspaceNavItem? item)
    {
        if (item != null) item.IsRenaming = false;
    }

    /// <summary>
    /// Stores the new name. The default workspace has no record of its own, so
    /// its name lives in the settings.
    /// </summary>
    [RelayCommand]
    private void CommitRenameWorkspace(WorkspaceNavItem? item)
    {
        if (item is not { IsRenaming: true }) return;
        item.IsRenaming = false;

        var name = item.DraftName.Trim();
        if (name.Length == 0 || name == item.Name) return;

        var before = item.Name;
        var id = item.Id;
        Rename(item, name);
        PushUndo("rename", SelectedWorkspaceId, () =>
        {
            var row = Workspaces.FirstOrDefault(w => w.Id == id);
            if (row != null) Rename(row, before);
        });
    }

    private void Rename(WorkspaceNavItem item, string name)
    {
        if (item.Workspace != null)
        {
            item.Workspace.Name = name;
            App.Database.UpsertWorkspace(item.Workspace);
        }
        else
        {
            App.Database.SaveSetting("DefaultWorkspaceName", name);
        }

        item.Name = name;
        if (_boards.TryGetValue(item.Id ?? "", out var board)) board.WorkspaceName = name;
    }

    // ── Overlays ────────────────────────────────────────────────

    [RelayCommand]
    private void OpenAddTask()
    {
        var workspaceId = SelectedWorkspaceId;
        if (CurrentPage is not (AppPage.Board or AppPage.TaskDetail)) workspaceId = null;
        AddTask = new AddTaskViewModel(this, workspaceId, TodoStatus.Active);
    }

    public void OpenAddTask(string? workspaceId, TodoStatus status)
        => AddTask = new AddTaskViewModel(this, workspaceId, status);

    [RelayCommand]
    private void CloseAddTask() => AddTask = null;

    [RelayCommand]
    private void OpenAiImport()
    {
        var workspaceId = SelectedWorkspaceId;
        if (CurrentPage is not (AppPage.Board or AppPage.TaskDetail)) workspaceId = null;
        AiImport = new AiImportViewModel(this, workspaceId);
    }

    [RelayCommand]
    private void CloseAiImport()
    {
        AiImport?.Cancel();
        AiImport = null;
    }

    /// <summary>Suggests a kind label for each of <paramref name="tasks"/> that has none, for the user to check.</summary>
    public void OpenLabelSuggest(string? workspaceId, string scope, IReadOnlyCollection<TodoItem> tasks)
    {
        LabelSuggest?.Cancel();
        LabelSuggest = new LabelSuggestViewModel(this, workspaceId, scope, tasks);
        _ = LabelSuggest.RunAsync();
    }

    [RelayCommand]
    private void CloseLabelSuggest()
    {
        LabelSuggest?.Cancel();
        LabelSuggest = null;
    }

    public void OpenGallery(IReadOnlyList<GalleryImage> images, int index)
    {
        if (images.Count == 0) return;
        Gallery = new GalleryViewModel(this, images, index);
    }

    [RelayCommand]
    private void CloseGallery() => Gallery = null;

    public void OpenExport(string title, IReadOnlyList<Services.ExportGroup> groups)
    {
        if (groups.Sum(g => g.Items.Count) == 0)
        {
            Services.DialogService.Notify("There are no tasks to export here.", "Export");
            return;
        }
        Export = new ExportViewModel(this, title, groups);
    }

    [RelayCommand]
    private void CloseExport()
    {
        Export?.Cancel();
        Export = null;
    }

    /// <summary>Called after tasks are created from a modal.</summary>
    // ── Undo ────────────────────────────────────────────────────

    /// <summary>One reversible step. Running <c>Revert</c> puts things back.</summary>
    private sealed record UndoStep(string Label, string? WorkspaceId, Action Revert);

    private const int UndoDepth = 40;
    private readonly List<UndoStep> _undo = new();

    public bool CanUndo => _undo.Count > 0;

    public string UndoLabel => _undo.Count > 0 ? $"Undo {_undo[^1].Label}" : "Nothing to undo";

    /// <summary>
    /// Records how to reverse what just happened. Callers snapshot whatever
    /// they are about to change and write the snapshots back in
    /// <paramref name="revert"/>.
    /// </summary>
    public void PushUndo(string label, string? workspaceId, Action revert)
    {
        _undo.Add(new UndoStep(label, workspaceId, revert));
        if (_undo.Count > UndoDepth) _undo.RemoveAt(0);

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undo.Count == 0) return;

        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        step.Revert();

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));

        RefreshCurrentPage();
    }

    /// <summary>
    /// Re-reads the page in front of the user. Other cached boards are left
    /// stale on purpose: navigating to one reloads it anyway.
    /// </summary>
    public void RefreshCurrentPage()
    {
        switch (CurrentPage)
        {
            case AppPage.Board when CurrentView is BoardView { DataContext: BoardViewModel board }:
                board.Reload();
                break;
            case AppPage.Dashboard:
                _dashboardVm?.Reload();
                break;
            case AppPage.Trash:
                _trashVm?.Reload();
                break;
        }

        RefreshCounts();
    }

    public void AfterTasksCreated(string? workspaceId)
    {
        _boards.Remove(workspaceId ?? "");
        ReloadWorkspaces();
        if (CurrentPage is AppPage.Board)
            NavigateToWorkspace(Workspaces.FirstOrDefault(w => w.Id == workspaceId) ?? Workspaces.FirstOrDefault());
        else if (CurrentPage is AppPage.Dashboard)
            NavigateToDashboardCommand.Execute(null);
    }

    // ── Backup ──────────────────────────────────────────────────

    [RelayCommand]
    private void ExportBackup()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export MikuDo Backup",
            Filter = "MikuDo Backup|*.mikudo.bak|All files|*.*",
            FileName = $"mikudo-backup-{DateTime.Now:yyyy-MM-dd}",
            DefaultExt = ".mikudo.bak"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            System.IO.File.Copy(App.Database.GetDatabasePath(), dlg.FileName, overwrite: true);
            Services.DialogService.Notify($"Backup exported to:\n{dlg.FileName}", "Export Complete");
        }
        catch (Exception ex)
        {
            Services.DialogService.Notify($"Export failed:\n{ex.Message}", "Export Error");
        }
    }

    [RelayCommand]
    private void ImportBackup()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import MikuDo Backup",
            Filter = "MikuDo Backup|*.mikudo.bak;*.db|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        if (!Services.DialogService.Confirm(
                "This replaces ALL current data with the backup. This cannot be undone.\n\nContinue?",
                "Import Backup"))
            return;

        try
        {
            var destPath = App.Database.GetDatabasePath();
            App.Database.Dispose();
            System.IO.File.Copy(dlg.FileName, destPath, overwrite: true);
            App.Database = new Services.DatabaseService();

            _boards.Clear();
            _searchBoard = null;
            _vaultVm = null;
            _trashVm = null;
            _dashboardVm = null;
            _uses = null;
            _opened = null;
            Shortcuts.Reload();
            ReloadWorkspaces();
            NavigateToWorkspace(Workspaces.FirstOrDefault());
            Services.DialogService.Notify("Backup imported. Your data has been restored.", "Import Complete");
        }
        catch (Exception ex)
        {
            try { App.Database = new Services.DatabaseService(); } catch { }
            Services.DialogService.Notify($"Import failed:\n{ex.Message}", "Import Error");
        }
    }

    [RelayCommand]
    private void ToggleScreenshotMode() => ScreenshotAllScreens = !ScreenshotAllScreens;

    public string ScreenshotModeLabel => ScreenshotAllScreens ? "All Screens" : "Primary Monitor";

    partial void OnScreenshotAllScreensChanged(bool oldValue, bool newValue)
        => OnPropertyChanged(nameof(ScreenshotModeLabel));

    // ── Keyboard shortcuts ──────────────────────────────────────

    /// <summary>Ctrl+Numpad1 — attach a screenshot to the open task, or start a new one with it.</summary>
    [RelayCommand]
    private void ScreenshotTodo()
    {
        var png = Services.ScreenshotService.CaptureScreen(AppWindow, ScreenshotAllScreens);
        if (png.Length == 0) return;

        if (CurrentView is TaskDetailView { DataContext: TaskDetailViewModel detail })
        {
            detail.InsertScreenshot(png);
            return;
        }

        var vm = new TaskDetailViewModel(this, null, isVault: false, SelectedWorkspaceId, "Board")
        {
            Title = $"Screenshot {DateTime.Now:yyyy-MM-dd HH:mm}"
        };
        vm.InsertScreenshot(png);
        ShowDetail(vm);
    }

    /// <summary>Ctrl+N — open the Add Task dialog.</summary>
    [RelayCommand]
    private void QuickNewTask() => OpenAddTaskCommand.Execute(null);

    /// <summary>Ctrl+S — save the open task.</summary>
    [RelayCommand]
    private void SaveCurrent()
    {
        if (CurrentView is TaskDetailView { DataContext: TaskDetailViewModel detail })
            detail.SaveCommand.Execute(null);
        else if (OpenNotePage is { } note)
            note.SaveCommand.Execute(null);
    }

    /// <summary>Escape — close the top overlay, otherwise leave the detail page.</summary>
    [RelayCommand]
    private void GoBack()
    {
        if (Gallery != null) { Gallery = null; return; }
        if (Export != null) { CloseExportCommand.Execute(null); return; }
        if (AiImport != null) { CloseAiImportCommand.Execute(null); return; }
        if (LabelSuggest != null) { CloseLabelSuggestCommand.Execute(null); return; }
        if (MakeTasks != null) { CloseMakeTasksCommand.Execute(null); return; }
        if (AddTask != null) { AddTask = null; return; }
        if (IsAddingWorkspace) { CancelAddWorkspaceCommand.Execute(null); return; }

        var renaming = Workspaces.FirstOrDefault(w => w.IsRenaming);
        if (renaming != null) { renaming.IsRenaming = false; return; }

        if (CurrentView is TaskDetailView { DataContext: TaskDetailViewModel detail })
        {
            // Escape while dictating throws the dictation away, not the page.
            if (detail.IsDictating) detail.CancelDictationCommand.Execute(null);
            else detail.CancelCommand.Execute(null);
        }
    }
}
