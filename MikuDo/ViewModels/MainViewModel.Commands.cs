using System.IO;
using System.Text.Json;
using System.Windows;
using MikuDo.Models;
using MikuDo.Services;
using MikuDo.Views;

namespace MikuDo.ViewModels;

/// <summary>
/// The commands Quick Access lists and keys run, what each does here, and
/// what Quick Access remembers between uses: which commands get run most and
/// what was opened last.
/// </summary>
public partial class MainViewModel
{
    private List<AppCommand>? _commands;
    private QuickAccessViewModel? _quickAccess;

    public IReadOnlyList<AppCommand> Commands => _commands ??= BuildCommands();

    public QuickAccessViewModel QuickAccess => _quickAccess ??= new QuickAccessViewModel(this);

    private ShortcutsViewModel? _keyboardShortcuts;

    /// <summary>Settings → Keyboard shortcuts, kept from one visit to the next.</summary>
    public ShortcutsViewModel KeyboardShortcuts => _keyboardShortcuts ??= new ShortcutsViewModel(this);

    public AppCommand? Command(string id) => Commands.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// Runs whatever <paramref name="chord"/> is set to. False when no command
    /// has it or the one that does had nothing to do here, so the key goes on.
    /// </summary>
    public bool RunShortcut(KeyChord chord)
        => Shortcuts.Holder(chord) is { } def && RunCommand(def.Id);

    public bool RunCommand(string id)
    {
        if (Command(id) is not { } command || !command.Run()) return false;
        CountUse(id);
        return true;
    }

    private List<AppCommand> BuildCommands()
    {
        AppCommand C(string id, Func<bool> run, Func<string>? name = null) => new(Shortcuts.Find(id)!, run, name);
        bool Do(Action action) { action(); return true; }

        var commands = new List<AppCommand>
        {
            C("quick.open", () => Do(() => QuickAccess.Open(QuickMode.Everything))),
            C("quick.tasks", () => Do(() => QuickAccess.Open(QuickMode.Tasks))),
            C("quick.commands", () => Do(() => QuickAccess.Open(QuickMode.Commands))),
            C("quick.settings", () => Do(() => QuickAccess.Open(QuickMode.Settings))),
            C("find.open", () => FindHere(), FindCommandName),
            C("find.next", () => FindHost is { } host && host.FindStep(1)),
            C("find.previous", () => FindHost is { } host && host.FindStep(-1)),
            C("app.save", () => (OpenTaskPage != null || OpenNotePage != null) && Do(SaveCurrent)),
            C("app.undo", () => CanUndo && Do(Undo)),
            C("app.settings", () => Do(NavigateToSettings)),
            C("app.shortcuts", () => Do(() => OpenSettingsAt(SettingsSections.Shortcuts))),
            C("theme.toggle", () => Do(ToggleTheme)),
            C("theme.choose", () => Do(() => OpenSettingsAt(SettingsSections.Theme))),
            C("layout.switch", () => Do(() => SelectLayout(Controls.Layout.Current.IsFlat ? Controls.Layout.Islands : Controls.Layout.Flat)),
              () => $"Switch layout to {(Controls.Layout.Current.IsFlat ? "Islands" : "Flat")}"),
            C("nav.dashboard", () => Do(NavigateToDashboard)),
            C("nav.vault", () => Do(NavigateToVault)),
            C("nav.trash", () => Do(NavigateToTrash)),
            C("nav.about", () => Do(NavigateToAbout)),
            C("capture.screenshot", () => Do(ScreenshotTodo)),
            C("capture.screens", () => Do(ToggleScreenshotMode),
              () => ScreenshotAllScreens ? "Take screenshots of the primary monitor only" : "Take screenshots of every screen"),

            C("board.newtask", () => Do(QuickNewTask)),
            C("board.rename", () => CurrentView is BoardView board && board.RenameFromKeyboard()),
            C("board.kanban", () => Do(() => ShowBoardAs("Kanban"))),
            C("board.list", () => Do(() => ShowBoardAs("List"))),
            C("board.table", () => Do(() => ShowBoardAs("Table"))),
            C("board.import", () => Do(OpenAiImport)),
            C("board.export", () => Do(() => ShownBoard()?.ExportBoardCommand.Execute(null))),
            C("board.newworkspace", () => Do(StartNewWorkspace)),
            C("board.nextworkspace", () => Do(() => StepWorkspace(1))),
            C("board.previousworkspace", () => Do(() => StepWorkspace(-1))),

            C("task.back", () => Do(GoBack)),
            C("task.completesubtasks", () => OpenTaskPage is { } page && Do(() => page.CompleteSubtasksCommand.Execute(null))),
            C("task.attachimage", () => OpenTaskPage is { } page && Do(() => page.AttachImageCommand.Execute(null))),
            C("task.attachfile", () => OpenTaskPage is { } page && Do(() => page.AttachFileCommand.Execute(null))),
            C("task.panel", () => OpenTaskPage is { } page && Do(() => page.ToggleSidebarCommand.Execute(null))),

            C("note.new", () => Do(NewNote)),
            C("note.open", () => Do(OpenNoteFile)),
            C("note.folder", () => Do(OpenNoteFolder)),
            C("quick.notes", () => Do(() => QuickAccess.Open(QuickMode.Notes))),
            C("note.popout", () => OpenNotePage != null && Do(PopOutNote)),
            C("note.edit", () => OpenNotePage is { } note && Do(() => note.EditorMode = "Edit")),
            C("note.split", () => OpenNotePage is { } note && Do(() => note.EditorMode = "Split")),
            C("note.preview", () => OpenNotePage is { } note && Do(() => note.EditorMode = "Preview")),
            C("note.maketasks", () => OpenNotePage is { } note
                                      && Do(() => note.MakeTasksCommand.Execute(note.HasOpenItems ? "items" : "whole"))),
            C("note.clearrecent", () => Do(ClearRecentNotes)),

            C("window.sidebar", () => Do(ToggleSidebar)),
            // A reader window closes itself on these keys; the main window has none to close.
            C("window.closereader", () => false),
            C("window.minimize", () => AppWindow is { } w && Do(() => w.WindowState = WindowState.Minimized)),
            C("window.maximize", () => AppWindow is { } w && Do(() =>
                w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized)),
        };

        // Every command in the catalogue has its doing here, in the catalogue's order.
        commands = Shortcuts.All.Select(def => commands.First(c => c.Id == def.Id)).ToList();

        Shortcuts.Changed += () => { foreach (var command in commands) command.Refresh(); };
        Controls.Layout.Current.PropertyChanged += (_, _) => Command("layout.switch")?.Refresh();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScreenshotAllScreens)) Command("capture.screens")?.Refresh();
        };
        return commands;
    }

    // ── What a few commands do ─────────────────────────────────

    /// <summary>The page on screen, when it can be searched in: a note, a task or a board.</summary>
    public IFindHost? FindHost => CurrentView as IFindHost;

    /// <summary>
    /// Opens the search of the page on screen, looking for <paramref name="text"/>
    /// when given: the find bar of a note or a task, or a board's search box.
    /// On Settings, Quick Access searches the settings. False with nothing to search.
    /// </summary>
    public bool FindHere(string? text = null)
    {
        if (CurrentPage == AppPage.Settings)
        {
            QuickAccess.Open(QuickMode.Settings);
            return true;
        }
        return FindHost is { } host && host.OpenFind(text);
    }

    /// <summary>The find command as it reads from here: "Find in this note", "Search this board".</summary>
    private string FindCommandName() => FindHost?.FindNoun switch
    {
        "note" => "Find in this note",
        "task" => "Find in this task",
        "board" => "Search this board",
        _ => CurrentPage == AppPage.Settings ? "Search settings" : "Find in the page"
    };

    /// <summary>What the page on screen is called, for Quick Access to name beside "This note": the file, the task, the board.</summary>
    public string? FindPlace() => FindHost?.FindNoun switch
    {
        "note" => OpenNotePage?.FileName,
        "task" => OpenTaskPage is { } task ? (string.IsNullOrWhiteSpace(task.Title) ? "Untitled" : task.Title) : null,
        "board" => ShownBoard()?.WorkspaceName,
        _ => null
    };

    /// <summary>The board on screen, if a board is.</summary>
    private BoardViewModel? ShownBoard()
        => CurrentView is BoardView { DataContext: BoardViewModel board } ? board : null;

    /// <summary>Switches the board on screen to a view; away from a board, the last one opened comes back first.</summary>
    private void ShowBoardAs(string mode)
    {
        if (ShownBoard() == null)
            NavigateToWorkspace(Workspaces.FirstOrDefault(w => w.Id == LastWorkspaceId) ?? Workspaces.FirstOrDefault());
        ShownBoard()?.SetViewModeCommand.Execute(mode);
    }

    /// <summary>The sidebar's own way to add a workspace, opened up wherever the sidebar was folded.</summary>
    private void StartNewWorkspace()
    {
        IsSidebarCollapsed = false;
        ShowAllWorkspaces();
        ShowAddWorkspace();
    }

    /// <summary>The workspace above or below the one last opened, round at the ends.</summary>
    private void StepWorkspace(int step)
    {
        if (Workspaces.Count == 0) return;
        var current = Workspaces.ToList().FindIndex(w => w.Id == (CurrentPage is AppPage.Board or AppPage.TaskDetail ? SelectedWorkspaceId : LastWorkspaceId));
        var next = current < 0 ? 0 : ((current + step) % Workspaces.Count + Workspaces.Count) % Workspaces.Count;
        NavigateToWorkspace(Workspaces[next]);
    }

    /// <summary>The theme Toggle theme goes to from here.</summary>
    public AppTheme ToggleThemeTarget()
    {
        var key = IsDark ? "LastLightTheme" : "LastDarkTheme";
        var fallback = IsDark ? ThemeCatalog.Sun : ThemeCatalog.Moon;
        var next = App.Database.GetSetting(key) is { } id ? ThemeCatalog.Resolve(id) : fallback;
        return next.IsDark == IsDark ? fallback : next;
    }

    // ── Opening what Quick Access found ────────────────────────

    public string WorkspaceName(string? workspaceId)
        => Workspaces.FirstOrDefault(w => w.Id == workspaceId)?.Name ?? App.Database.GetDefaultWorkspaceName();

    /// <summary>
    /// Opens a task's page from wherever the user is. The page on screen is
    /// left first, as any navigation leaves it; the task on screen already
    /// stays as it is.
    /// </summary>
    public void OpenTaskAnywhere(TodoItem task)
    {
        if (OpenTaskPage?.TaskId == task.IdText) return;
        if (App.Database.GetTodoById(task.Id) is not { } fresh || fresh.Status == TodoStatus.Trashed) return;
        if (!LeaveCurrentPage()) return;

        SelectedWorkspaceId = fresh.WorkspaceId;
        LastWorkspaceId = fresh.WorkspaceId;
        OpenTask(fresh, isVault: false, WorkspaceName(fresh.WorkspaceId));
    }

    /// <summary>A note in a reader window; the one on the Notes page moves there.</summary>
    public void OpenNoteInReader(string path)
    {
        if (OpenNotePage is { } shown && SamePathQuietly(shown.Path, path)) PopOutNote();
        else NoteWindow.Open(this, path);
    }

    /// <summary>
    /// Where a note lives, as Quick Access names it: from the opened folder
    /// that holds it down to its own ("Releases\v2.4"), or else just the folder
    /// it is in.
    /// </summary>
    public string NoteFolderLabel(string notePath)
    {
        var dir = Path.GetDirectoryName(notePath) ?? string.Empty;
        var root = OpenedFolders.Where(f => f.Holds(dir)).MaxBy(f => f.Path.Length);
        if (root == null) return Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : dir;

        var inside = Path.GetRelativePath(root.Path, dir);
        return inside == "." ? root.Name : Path.Combine(root.Name, inside);
    }

    /// <summary>True while the board of search results across every workspace is on screen.</summary>
    public bool IsSearchBoardShown => CurrentView is BoardView { DataContext: BoardViewModel { AllWorkspaces: true } };

    /// <summary>Every task that matches, on the search board.</summary>
    public void ShowSearchBoard(string query)
    {
        GlobalSearch = query;
        if (IsSearchBoardShown) QuickAccess.ShowBoardQuery(query);
    }

    /// <summary>Leaving the search board ends the search, so the bar does not go on showing it.</summary>
    partial void OnCurrentViewChanged(object? value)
    {
        if (GlobalSearch.Length == 0 || IsSearchBoardShown) return;
        _workspaceBeforeSearch = null;
        _searchNavigating = true;
        GlobalSearch = string.Empty;
        _searchNavigating = false;
        QuickAccess.ShowBoardQuery(string.Empty);
    }

    // ── Settings, opened at a part of the page ─────────────────

    /// <summary>
    /// Opens Settings scrolled to <paramref name="section"/>. With
    /// <paramref name="recordFor"/>, the keyboard shortcuts wait for that
    /// command's new keys straight away.
    /// </summary>
    public void OpenSettingsAt(string section, string? recordFor = null)
    {
        if (CurrentPage != AppPage.Settings) NavigateToSettings();
        if (CurrentPage != AppPage.Settings || _settingsView?.DataContext is not SettingsViewModel settings) return;
        settings.Show(section, recordFor);
    }

    // ── Most used ───────────────────────────────────────────────

    private Dictionary<string, int>? _uses;

    private Dictionary<string, int> Uses => _uses ??= ReadJson<Dictionary<string, int>>("CommandUses") ?? new();

    private void CountUse(string id)
    {
        Uses[id] = Uses.GetValueOrDefault(id) + 1;
        App.Database.SaveSetting("CommandUses", JsonSerializer.Serialize(Uses));
    }

    /// <summary>Until a few have been run, these stand in, in this order.</summary>
    private static readonly string[] StartingCommands =
        { "board.newtask", "note.new", "note.open", "theme.toggle", "layout.switch", "nav.vault" };

    /// <summary>
    /// The commands run most, for Quick Access to offer first. The ways into
    /// Quick Access itself are left out: it shows those above, under SEARCH IN.
    /// </summary>
    public IReadOnlyList<AppCommand> MostUsedCommands(int count)
        => UsedCommands().Concat(StartingCommands.Select(Command).OfType<AppCommand>()).Distinct().Take(count).ToList();

    /// <summary>Only the commands that have been run, most run first; the ways into Quick Access are left out.</summary>
    public IReadOnlyList<AppCommand> UsedCommands()
        => Uses.Where(u => u.Value > 0 && !u.Key.StartsWith("quick.", StringComparison.Ordinal))
               .OrderByDescending(u => u.Value)
               .Select(u => Command(u.Key)).OfType<AppCommand>()
               .ToList();

    // ── Opened lately ──────────────────────────────────────────

    /// <summary>A task or a note opened lately: a task by its id, a note by its path.</summary>
    public sealed record OpenedItem(string Kind, string Key);

    public const string TaskKind = "task";
    public const string NoteKind = "note";
    private const int OpenedLimit = 12;

    private List<OpenedItem>? _opened;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<OpenedItem> RecentlyOpened => _opened ??= ReadJson<List<OpenedItem>>("QuickRecent") ?? new();

    public void RememberOpened(string kind, string key)
    {
        var list = (List<OpenedItem>)RecentlyOpened;
        list.RemoveAll(o => o.Kind == kind && (kind == NoteKind ? SamePathQuietly(o.Key, key) : o.Key == key));
        list.Insert(0, new OpenedItem(kind, key));
        if (list.Count > OpenedLimit) list.RemoveRange(OpenedLimit, list.Count - OpenedLimit);
        App.Database.SaveSetting("QuickRecent", JsonSerializer.Serialize(list));
    }

    private static bool SamePathQuietly(string a, string b)
    {
        try { return SamePath(a, b); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static T? ReadJson<T>(string key) where T : class
    {
        var stored = App.Database.GetSetting(key);
        if (string.IsNullOrEmpty(stored)) return null;
        try { return JsonSerializer.Deserialize<T>(stored); }
        catch (JsonException) { return null; }
    }
}
