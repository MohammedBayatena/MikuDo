using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MikuDo.ViewModels;

/// <summary>The sidebar's own state: its width, and how its sections are folded.</summary>
public partial class MainViewModel
{
    public const double DefaultSidebarWidth = 236;

    /// <summary>Narrow enough to give the page room, wide enough still for a row of three squares.</summary>
    public const double MinSidebarWidth = 210;

    public const double MaxSidebarWidth = 440;

    /// <summary>
    /// How wide a square's column is, in every row alike so squares and their
    /// names line up: the square, and under it room for "Dashboard", the
    /// longest name a square always shows in full.
    /// </summary>
    public const double SquareColumn = 56;

    /// <summary>
    /// What the sidebar keeps for itself beside a row of squares: its border
    /// and its padding either side, less the step left a row takes so its
    /// first square stands under the section's heading. The scroll bar runs in
    /// the padding, so it takes nothing more.
    /// </summary>
    private const double SquareRowChrome = 1 + 14 * 2 - 1;

    /// <summary>
    /// Squares that fit across the sidebar as it is now wide, never fewer than
    /// three, so Dashboard, Vault and Trash always share one row. With more
    /// workspaces than that, the last square counts the rest.
    /// </summary>
    public int WorkspaceSquareCount => Math.Max(3, (int)((SidebarWidth - SquareRowChrome) / SquareColumn));

    public double SquareTileWidth => SquareColumn;

    /// <summary>The open sidebar's width, as last dragged.</summary>
    [ObservableProperty] private double _sidebarWidth = StoredSidebarWidth();

    /// <summary>Dashboard, Vault and Trash as one row of squares rather than a list.</summary>
    [ObservableProperty] private bool _isPagesCompact = App.Database.GetSetting("SidebarPagesCompact") == "True";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWorkspaceList), nameof(ShowWorkspaceSquares))]
    private bool _isWorkspacesOpen = App.Database.GetSetting("SidebarWorkspacesOpen") != "False";

    /// <summary>Workspaces as one row of squares rather than the full list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWorkspaceList), nameof(ShowWorkspaceSquares))]
    private bool _isWorkspacesCompact = App.Database.GetSetting("SidebarWorkspacesCompact") == "True";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoteList), nameof(ShowNoteSquares))]
    private bool _isNotesOpen = App.Database.GetSetting("SidebarNotesOpen") != "False";

    /// <summary>
    /// Notes as one row of squares rather than the tree: the note opened last,
    /// whose square shows the others opened lately while pointed at, then the
    /// opened folders.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoteList), nameof(ShowNoteSquares))]
    private bool _isNotesCompact = App.Database.GetSetting("SidebarNotesCompact") == "True";

    public bool ShowNoteList => IsNotesOpen && !IsNotesCompact;
    public bool ShowNoteSquares => IsNotesOpen && IsNotesCompact;

    /// <summary>How many notes opened lately the bar over the Recent square shows.</summary>
    public const int RecentBarLimit = 5;

    /// <summary>The note opened last, on the Recent square; null when Recent is empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLatestNote))]
    private NoteRow? _latestNote;

    public bool HasLatestNote => LatestNote != null;

    /// <summary>The notes opened lately, newest first, for the bar over the Recent square.</summary>
    [ObservableProperty] private IReadOnlyList<NoteRow> _recentNoteBar = Array.Empty<NoteRow>();

    /// <summary>The opened folders in the row of squares, after the Recent one, as many as fit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolderSquares))]
    private IReadOnlyList<FolderTile> _folderSquares = Array.Empty<FolderTile>();

    public bool HasFolderSquares => FolderSquares.Count > 0;

    /// <summary>Opened folders left out of the row of squares, counted on its last square.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreFolders))]
    private int _moreFolders;

    public bool HasMoreFolders => MoreFolders > 0;

    public bool ShowWorkspaceList => IsWorkspacesOpen && !IsWorkspacesCompact;
    public bool ShowWorkspaceSquares => IsWorkspacesOpen && IsWorkspacesCompact;

    /// <summary>The workspaces in the row of squares: the first few, always with the active one among them.</summary>
    [ObservableProperty] private IReadOnlyList<WorkspaceNavItem> _workspaceSquares = Array.Empty<WorkspaceNavItem>();

    /// <summary>Workspaces left out of the row of squares, counted on its last square.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreWorkspaces))]
    private int _moreWorkspaces;

    public bool HasMoreWorkspaces => MoreWorkspaces > 0;

    private static double StoredSidebarWidth()
        => double.TryParse(App.Database.GetSetting("SidebarWidth"), NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            ? Math.Clamp(width, MinSidebarWidth, MaxSidebarWidth)
            : DefaultSidebarWidth;

    public void SaveSidebarWidth()
        => App.Database.SaveSetting("SidebarWidth", SidebarWidth.ToString("0", CultureInfo.InvariantCulture));

    partial void OnIsWorkspacesOpenChanged(bool value)
        => App.Database.SaveSetting("SidebarWorkspacesOpen", value ? "True" : "False");

    partial void OnIsWorkspacesCompactChanged(bool value)
        => App.Database.SaveSetting("SidebarWorkspacesCompact", value ? "True" : "False");

    partial void OnIsNotesOpenChanged(bool value)
        => App.Database.SaveSetting("SidebarNotesOpen", value ? "True" : "False");

    partial void OnIsNotesCompactChanged(bool value)
        => App.Database.SaveSetting("SidebarNotesCompact", value ? "True" : "False");

    partial void OnIsPagesCompactChanged(bool value)
        => App.Database.SaveSetting("SidebarPagesCompact", value ? "True" : "False");

    [RelayCommand]
    private void TogglePagesCompact() => IsPagesCompact = !IsPagesCompact;

    [RelayCommand]
    private void ToggleWorkspacesSection() => IsWorkspacesOpen = !IsWorkspacesOpen;

    [RelayCommand]
    private void ToggleNotesSection() => IsNotesOpen = !IsNotesOpen;

    [RelayCommand]
    private void ToggleNotesCompact()
    {
        IsNotesCompact = !IsNotesCompact;
        IsNotesOpen = true;
    }

    /// <summary>The whole tree again, from the row of squares.</summary>
    [RelayCommand]
    private void ShowAllNotes()
    {
        IsNotesCompact = false;
        IsNotesOpen = true;
    }

    [RelayCommand]
    private void ToggleWorkspacesCompact()
    {
        IsWorkspacesCompact = !IsWorkspacesCompact;
        IsWorkspacesOpen = true;
    }

    /// <summary>The full list again, from the row of squares.</summary>
    [RelayCommand]
    private void ShowAllWorkspaces()
    {
        IsWorkspacesCompact = false;
        IsWorkspacesOpen = true;
    }

    /// <summary>A wider or narrower sidebar fits more or fewer squares.</summary>
    partial void OnSidebarWidthChanged(double value)
    {
        SyncWorkspaceSquares();
        SyncShortcutSquares();
        SyncNoteSquares();
    }

    /// <summary>
    /// Fills the row of note squares: the note opened last takes the first
    /// column, and the opened folders as many of the rest as they fit, the
    /// last counting any left over.
    /// </summary>
    private void SyncNoteSquares()
    {
        if (!ReferenceEquals(LatestNote, RecentNotes.FirstOrDefault())) LatestNote = RecentNotes.FirstOrDefault();
        var bar = RecentNotes.Take(RecentBarLimit).ToList();
        if (!bar.SequenceEqual(RecentNoteBar)) RecentNoteBar = bar;

        var roots = NoteRows.OfType<FolderRow>().Where(r => r.IsRoot).ToDictionary(r => r.Folder);
        var tiles = OpenedFolders.Select(f => new FolderTile(f, roots.TryGetValue(f, out var row) ? row.Count : 0)).ToList();
        var room = WorkspaceSquareCount - 1;
        var shown = tiles.Count <= room ? tiles : tiles.Take(room - 1).ToList();
        if (!shown.Select(t => t.Look).SequenceEqual(FolderSquares.Select(t => t.Look))) FolderSquares = shown;
        MoreFolders = tiles.Count - shown.Count;
    }

    /// <summary>
    /// An opened folder, from its square: the tree comes back with the folder
    /// open. The view brings it into sight.
    /// </summary>
    [RelayCommand]
    private void ShowNoteFolder(NoteFolder? folder)
    {
        if (folder == null) return;
        IsNotesCompact = false;
        IsNotesOpen = true;
        if (NoteRows.OfType<FolderRow>().FirstOrDefault(r => r.IsRoot && r.Folder == folder) is { IsExpanded: false } row)
            ToggleNoteRow(row);
    }

    /// <summary>Fills the row of squares; the set changes only when a workspace comes, goes or becomes active.</summary>
    private void SyncWorkspaceSquares()
    {
        var all = Workspaces;
        List<WorkspaceNavItem> shown;
        if (all.Count <= WorkspaceSquareCount)
        {
            shown = all.ToList();
        }
        else
        {
            shown = all.Take(WorkspaceSquareCount - 1).ToList();
            if (all.FirstOrDefault(w => w.IsActive) is { } active && !shown.Contains(active)) shown[^1] = active;
        }

        if (!shown.SequenceEqual(WorkspaceSquares)) WorkspaceSquares = shown;
        MoreWorkspaces = all.Count - shown.Count;
    }
}
