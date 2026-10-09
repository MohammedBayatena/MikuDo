using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>The NOTES part of the sidebar: the Recent group and each opened folder as a tree.</summary>
public partial class MainViewModel
{
    /// <summary>Everything under NOTES, one row per line, in the order shown.</summary>
    public ObservableCollection<SidebarNoteRow> NoteRows { get; } = new();

    /// <summary>A folder shows this many of its own notes at first, and this many more each time it is asked.</summary>
    public const int NotesPerBatch = 100;

    /// <summary>Folders asked to show more than the first batch of notes, and how many each shows.</summary>
    private readonly Dictionary<string, int> _notesShown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folders opened under Notes, in the order they were opened.</summary>
    public List<NoteFolder> OpenedFolders { get; } = new();

    /// <summary>Keys of the groups and folders the user left open: <see cref="RecentKey"/>, a folder's path, or <see cref="HiddenKey"/>.</summary>
    private readonly HashSet<string> _openRows = new(StringComparer.OrdinalIgnoreCase);

    private const string RecentKey = "recent";
    private const string HiddenSuffix = "|hidden";
    private static string HiddenKey(string folder) => folder + HiddenSuffix;

    /// <summary>The folder an open-row key belongs to.</summary>
    private static string KeyFolder(string key) => key.EndsWith(HiddenSuffix, StringComparison.Ordinal) ? key[..^HiddenSuffix.Length] : key;

    /// <summary>Changes to a folder settle for this long before it is looked through again.</summary>
    private static readonly TimeSpan RescanDelay = TimeSpan.FromMilliseconds(400);

    private void LoadNoteTree()
    {
        var open = App.Database.GetSetting("NoteTreeOpen");
        if (open == null) _openRows.Add(RecentKey);
        else
        {
            try
            {
                foreach (var key in JsonSerializer.Deserialize<List<string>>(open) ?? new()) _openRows.Add(key);
            }
            catch (JsonException) { }
        }

        var stored = App.Database.GetSetting("NoteFolders");
        if (!string.IsNullOrEmpty(stored))
        {
            try
            {
                foreach (var entry in JsonSerializer.Deserialize<List<StoredNoteFolder>>(stored) ?? new())
                {
                    if (string.IsNullOrWhiteSpace(entry.Path)) continue;
                    var folder = new NoteFolder(entry.Path);
                    foreach (var relative in entry.Hidden ?? new())
                        folder.Hidden.Add(Path.GetFullPath(Path.Combine(folder.Path, relative)));
                    OpenedFolders.Add(folder);
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
            {
                LogService.Error("The folders under Notes could not be read back", ex);
            }
        }

        RebuildNoteRows();
        foreach (var folder in OpenedFolders) Rescan(folder);
    }

    private void SaveNoteFolders()
        => App.Database.SaveSetting("NoteFolders", JsonSerializer.Serialize(OpenedFolders.Select(f =>
            new StoredNoteFolder(f.Path, f.Hidden.Select(h => Path.GetRelativePath(f.Path, h)).Order(StringComparer.OrdinalIgnoreCase).ToList()))));

    private void SaveOpenRows()
        => App.Database.SaveSetting("NoteTreeOpen", JsonSerializer.Serialize(_openRows.Order(StringComparer.OrdinalIgnoreCase).ToList()));

    // ── Building the rows ───────────────────────────────────────

    /// <summary>Lays the whole NOTES list out again, keeping what is open and what is active.</summary>
    private void RebuildNoteRows()
    {
        var rows = new List<SidebarNoteRow>();
        if (RecentNotes.Count > 0)
        {
            var group = new RecentGroupRow { Count = RecentNotes.Count, IsExpanded = _openRows.Contains(RecentKey) };
            rows.Add(group);
            if (group.IsExpanded) AddContents(rows, group);
        }
        foreach (var folder in OpenedFolders)
        {
            var row = RootRow(folder);
            rows.Add(row);
            if (row.IsExpanded) AddContents(rows, row);
        }
        ShowRows(rows);
    }

    /// <summary>
    /// Puts <paramref name="rows"/> on show by changing only what differs: a
    /// row already shown that looks the same stays, with what is laid out for
    /// it, so a look through a folder or a hidden folder costs only the rows
    /// that changed.
    /// </summary>
    private void ShowRows(List<SidebarNoteRow> rows)
    {
        var shown = new Dictionary<string, SidebarNoteRow>();
        foreach (var row in NoteRows) shown.TryAdd(row.Key, row);
        for (var i = 0; i < rows.Count; i++)
        {
            if (!shown.Remove(rows[i].Key, out var kept)) continue;
            if (ReferenceEquals(kept, rows[i])) continue;
            if (!kept.LooksLike(rows[i])) continue;
            kept.TakeStateFrom(rows[i]);
            rows[i] = kept;
        }

        var wanted = new HashSet<SidebarNoteRow>(rows, ReferenceEqualityComparer.Instance);
        for (var i = NoteRows.Count - 1; i >= 0; i--)
            if (!wanted.Contains(NoteRows[i])) NoteRows.RemoveAt(i);

        for (var i = 0; i < rows.Count; i++)
        {
            if (i < NoteRows.Count && ReferenceEquals(NoteRows[i], rows[i])) continue;
            var at = -1;
            for (var j = i + 1; j < NoteRows.Count; j++)
                if (ReferenceEquals(NoteRows[j], rows[i])) { at = j; break; }
            if (at >= 0) NoteRows.Move(at, i);
            else NoteRows.Insert(i, rows[i]);
        }
        SpaceRoots(NoteRows);
    }

    private FolderRow RootRow(NoteFolder folder) => new()
    {
        Folder = folder,
        Node = folder.Tree,
        Path = folder.Path,
        Name = folder.Name,
        IsRoot = true,
        Count = folder.Tree == null ? 0 : VisibleCount(folder, folder.Tree),
        IsMissing = folder.Scanned && folder.Tree == null,
        IsExpanded = _openRows.Contains(folder.Path)
    };

    /// <summary>Adds the rows that show while <paramref name="parent"/> is open.</summary>
    private void AddContents(List<SidebarNoteRow> rows, SidebarNoteRow parent)
    {
        var depth = parent.Depth + 1;
        switch (parent)
        {
            case RecentGroupRow:
                rows.AddRange(RecentNotes);
                break;

            case FolderRow { IsRoot: true, Node: null } root:
                if (root.Folder.Scanned)
                    rows.Add(new EmptyFolderRow { Depth = depth, Folder = root.Path, IsMissing = true });
                break;

            case FolderRow { IsRoot: true, Node.Count: 0 } root:
                rows.Add(new EmptyFolderRow { Depth = depth, Folder = root.Path });
                break;

            case FolderRow { Node: { } node } folder:
                AddFolder(rows, folder.Folder, node, depth, folder.IsDimmed);
                break;

            case HiddenGroupRow hidden:
            {
                var folders = hidden.Node.Folders.Where(f => hidden.Folder.Hidden.Contains(f.Path)).ToList();
                foreach (var sub in folders) AddSubfolder(rows, hidden.Folder, sub, depth, dimmed: true, hiddenItem: true);
                foreach (var note in hidden.Node.Notes.Where(hidden.Folder.Hidden.Contains))
                    rows.Add(NoteRowFor(hidden.Folder, note, depth, align: folders.Count > 0, dimmed: true, hiddenItem: true));
                break;
            }
        }
    }

    /// <summary>A folder's own rows: its folders, then its notes, then what was hidden from it.</summary>
    private void AddFolder(List<SidebarNoteRow> rows, NoteFolder folder, NoteFolderNode node, int depth, bool dimmed)
    {
        var folders = node.Folders.Where(f => !folder.Hidden.Contains(f.Path)).ToList();
        var notes = node.Notes.Where(n => !folder.Hidden.Contains(n)).ToList();
        var hiddenFolders = node.Folders.Count - folders.Count;
        var hiddenNotes = node.Notes.Count - notes.Count;
        var align = folders.Count > 0 || hiddenFolders + hiddenNotes > 0;

        foreach (var sub in folders) AddSubfolder(rows, folder, sub, depth, dimmed, hiddenItem: false);
        var limit = _notesShown.GetValueOrDefault(node.Path, NotesPerBatch);
        foreach (var note in notes.Take(limit)) rows.Add(NoteRowFor(folder, note, depth, align, dimmed, hiddenItem: false));
        if (notes.Count > limit)
            rows.Add(new MoreNotesRow
            {
                Depth = depth,
                Folder = folder,
                Path = node.Path,
                Remaining = notes.Count - limit,
                Next = Math.Min(NotesPerBatch, notes.Count - limit),
                AlignWithFolders = align,
                IsDimmed = dimmed
            });

        if (hiddenFolders + hiddenNotes == 0) return;
        var group = new HiddenGroupRow
        {
            Depth = depth,
            Folder = folder,
            Node = node,
            IsDimmed = dimmed,
            Count = hiddenNotes + node.Folders.Where(f => folder.Hidden.Contains(f.Path)).Sum(f => f.Count),
            IsExpanded = _openRows.Contains(HiddenKey(node.Path))
        };
        rows.Add(group);
        if (group.IsExpanded) AddContents(rows, group);
    }

    private void AddSubfolder(List<SidebarNoteRow> rows, NoteFolder folder, NoteFolderNode node, int depth, bool dimmed, bool hiddenItem)
    {
        var row = new FolderRow
        {
            Depth = depth,
            Folder = folder,
            Node = node,
            Path = node.Path,
            Name = node.Name,
            IsDimmed = dimmed,
            IsHiddenItem = hiddenItem,
            Count = hiddenItem ? node.Count : VisibleCount(folder, node),
            IsExpanded = _openRows.Contains(node.Path)
        };
        rows.Add(row);
        if (row.IsExpanded) AddContents(rows, row);
    }

    private NoteRow NoteRowFor(NoteFolder folder, string path, int depth, bool align, bool dimmed, bool hiddenItem)
    {
        var open = OpenNotePage;
        var active = open != null && string.Equals(open.Path, path, StringComparison.OrdinalIgnoreCase);
        return new NoteRow
        {
            Depth = depth,
            Path = path,
            Folder = folder,
            AlignWithFolders = align,
            IsDimmed = dimmed,
            IsHiddenItem = hiddenItem,
            IsActive = active,
            IsDirty = active && open!.IsDirty
        };
    }

    /// <summary>Notes in a folder and below it, leaving out what was hidden.</summary>
    private static int VisibleCount(NoteFolder folder, NoteFolderNode node)
    {
        var count = 0;
        foreach (var note in node.Notes)
            if (!folder.Hidden.Contains(note)) count++;
        foreach (var sub in node.Folders)
            if (!folder.Hidden.Contains(sub.Path)) count += VisibleCount(folder, sub);
        return count;
    }

    /// <summary>A top-level row sits a little further from an open group above it than from a shut one.</summary>
    private static void SpaceRoots(IList<SidebarNoteRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Depth != 0) continue;
            var space = i == 0 ? 0 : rows[i - 1].Depth > 0 ? 5 : 2;
            if (rows[i].Spacing.Top != space) rows[i].Spacing = new Thickness(0, space, 0, 0);
        }
    }

    /// <summary>Keeps the Recent rows in step with <see cref="RecentNotes"/> without laying out the folders again.</summary>
    private void SyncRecentRows()
    {
        var group = NoteRows.Count > 0 ? NoteRows[0] as RecentGroupRow : null;
        if (group == null || RecentNotes.Count == 0)
        {
            RebuildNoteRows();
            return;
        }

        group.Count = RecentNotes.Count;
        if (!group.IsExpanded) return;

        var start = 1;
        var end = start;
        while (end < NoteRows.Count && NoteRows[end].Depth > 0) end++;
        for (var i = 0; i < RecentNotes.Count; i++)
        {
            var want = RecentNotes[i];
            var at = start + i;
            if (at < end && ReferenceEquals(NoteRows[at], want)) continue;

            var found = -1;
            for (var j = at + 1; j < end; j++)
                if (ReferenceEquals(NoteRows[j], want)) { found = j; break; }
            if (found >= 0) NoteRows.Move(found, at);
            else
            {
                NoteRows.Insert(at, want);
                end++;
            }
        }
        while (end > start + RecentNotes.Count) NoteRows.RemoveAt(--end);
        SpaceRoots(NoteRows);
    }

    /// <summary>Marks the row of the note on screen, and its unsaved dot, wherever it shows.</summary>
    private void MarkNoteRows()
    {
        var open = OpenNotePage;
        foreach (var row in RecentNotes) Mark(row);
        foreach (var row in NoteRows)
            if (row is NoteRow { Folder: not null } note) Mark(note);

        void Mark(NoteRow row)
        {
            var active = open != null && SamePath(row.Path, open.Path);
            row.IsActive = active;
            row.IsDirty = active && open!.IsDirty;
        }
    }

    // ── Opening and shutting ────────────────────────────────────

    /// <summary>Opens or shuts a group or folder, adding or taking away only the rows beneath it.</summary>
    [RelayCommand]
    private void ToggleNoteRow(SidebarNoteRow? row)
    {
        bool expanded;
        switch (row)
        {
            case RecentGroupRow group:
                expanded = group.IsExpanded = !group.IsExpanded;
                SetOpen(RecentKey, expanded);
                break;
            case FolderRow folder:
                expanded = folder.IsExpanded = !folder.IsExpanded;
                SetOpen(folder.Path, expanded);
                if (folder.IsRoot && folder.Node == null) Rescan(folder.Folder);
                break;
            case HiddenGroupRow hidden:
                expanded = hidden.IsExpanded = !hidden.IsExpanded;
                SetOpen(HiddenKey(hidden.Node.Path), expanded);
                break;
            default:
                return;
        }
        SaveOpenRows();

        var index = NoteRows.IndexOf(row);
        if (index < 0) return;
        if (expanded)
        {
            var rows = new List<SidebarNoteRow>();
            AddContents(rows, row);
            for (var i = 0; i < rows.Count; i++) NoteRows.Insert(index + 1 + i, rows[i]);
        }
        else
        {
            var end = index + 1;
            while (end < NoteRows.Count && NoteRows[end].Depth > row.Depth) end++;
            for (var i = end - 1; i > index; i--) NoteRows.RemoveAt(i);
        }
        SpaceRoots(NoteRows);
    }

    /// <summary>Shows the next batch of a folder's notes.</summary>
    [RelayCommand]
    private void ShowMoreNotes(MoreNotesRow? row)
    {
        if (row == null) return;
        _notesShown[row.Path] = _notesShown.GetValueOrDefault(row.Path, NotesPerBatch) + NotesPerBatch;
        RebuildNoteRows();
    }

    private void SetOpen(string key, bool open)
    {
        if (open) _openRows.Add(key);
        else _openRows.Remove(key);
    }

    /// <summary>Shuts a folder and everything inside it, so it opens again one level at a time.</summary>
    [RelayCommand]
    private void CollapseNoteFolder(FolderRow? row)
    {
        if (row == null) return;
        _openRows.RemoveWhere(key => !string.Equals(key, row.Path, StringComparison.OrdinalIgnoreCase)
                                     && NoteTreePaths.IsUnder(KeyFolder(key), row.Path));
        if (row.IsExpanded) ToggleNoteRow(row);
        else SaveOpenRows();
    }

    /// <summary>Opens every folder from the opened one down to <paramref name="path"/>, so it shows in the sidebar.</summary>
    [RelayCommand]
    private void RevealNoteFolder(string? path)
    {
        if (path == null) return;
        var root = OpenedFolders.Where(f => f.Holds(path)).MaxBy(f => f.Path.Length);
        if (root == null) return;

        _openRows.Add(root.Path);
        var walk = root.Path;
        foreach (var part in Path.GetRelativePath(root.Path, path).Split(Path.DirectorySeparatorChar))
        {
            if (part is "." or "") continue;
            var next = Path.Combine(walk, part);
            // A hidden folder shows inside its parent's Hidden group, so that opens too.
            if (root.Hidden.Contains(next)) _openRows.Add(HiddenKey(walk));
            walk = next;
            _openRows.Add(walk);
        }
        SaveOpenRows();
        RebuildNoteRows();
    }

    // ── Opened folders ──────────────────────────────────────────

    [RelayCommand]
    private void OpenNoteFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Open a folder of notes", Multiselect = false };
        if (dialog.ShowDialog(AppWindow) == true) AddNoteFolder(dialog.FolderName);
    }

    /// <summary>Puts a folder under Notes, open, and looks through it. A folder already there just opens.</summary>
    public void AddNoteFolder(string path)
    {
        try { path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        if (!Directory.Exists(path)) return;

        var folder = OpenedFolders.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        if (folder == null)
        {
            folder = new NoteFolder(path);
            OpenedFolders.Add(folder);
            SaveNoteFolders();
        }
        _openRows.Add(folder.Path);
        SaveOpenRows();
        RebuildNoteRows();
        Rescan(folder);
        RefreshCrumbs();
    }

    /// <summary>Takes an opened folder off the sidebar. Nothing on disk is touched.</summary>
    [RelayCommand]
    private void RemoveNoteFolder(FolderRow? row)
    {
        if (row is not { IsRoot: true }) return;
        var folder = row.Folder;
        OpenedFolders.Remove(folder);
        folder.ScanVersion++;
        folder.Watcher?.Dispose();
        folder.Watcher = null;

        // Keys inside another opened folder stay: that folder still shows them.
        _openRows.RemoveWhere(key => NoteTreePaths.IsUnder(KeyFolder(key), folder.Path)
                                     && !OpenedFolders.Any(f => f.Holds(KeyFolder(key))));
        SaveNoteFolders();
        SaveOpenRows();
        RebuildNoteRows();
        RefreshCrumbs();
    }

    /// <summary>Asks for a name and makes a note in <paramref name="folder"/>, then opens it ready to write.</summary>
    [RelayCommand]
    private void NewNoteIn(string? folder)
    {
        if (folder == null || !Directory.Exists(folder)) return;
        RevealNoteFolder(folder);
        CreateNote(folder);
    }

    [RelayCommand]
    private void ShowNoteFolderInExplorer(string? folder)
    {
        if (folder == null || !Directory.Exists(folder)) return;
        try
        {
            Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch (Exception ex)
        {
            LogService.Error($"Could not show {folder} in Explorer", ex);
        }
    }

    // ── Hiding from the tree ────────────────────────────────────

    /// <summary>Moves a note or folder into its parent's Hidden group. The file itself is left alone.</summary>
    [RelayCommand]
    private void HideInNoteTree(SidebarNoteRow? row) => SetHidden(row, hidden: true);

    /// <summary>Brings a note or folder back out of its parent's Hidden group.</summary>
    [RelayCommand]
    private void ShowInNoteTree(SidebarNoteRow? row) => SetHidden(row, hidden: false);

    private void SetHidden(SidebarNoteRow? row, bool hidden)
    {
        var (folder, path) = row switch
        {
            FolderRow { IsRoot: false } f => (f.Folder, f.Path),
            NoteRow { Folder: { } owner } n => (owner, n.Path),
            _ => (null, null)
        };
        if (folder == null || path == null) return;

        var changed = hidden ? folder.Hidden.Add(path) : folder.Hidden.Remove(path);
        if (!changed) return;
        SaveNoteFolders();
        RebuildNoteRows();
    }

    /// <summary>Brings everything in a Hidden group back into its folder's tree.</summary>
    [RelayCommand]
    private void ShowAllInNoteTree(HiddenGroupRow? row)
    {
        if (row == null) return;
        var inside = row.Node.Folders.Select(f => f.Path).Concat(row.Node.Notes).ToList();
        var changed = false;
        foreach (var path in inside) changed |= row.Folder.Hidden.Remove(path);
        _openRows.Remove(HiddenKey(row.Node.Path));
        SaveOpenRows();
        if (!changed) return;
        SaveNoteFolders();
        RebuildNoteRows();
    }

    // ── Looking through folders ─────────────────────────────────

    /// <summary>Looks through a folder off the UI thread, then lays the rows out again.</summary>
    private async void Rescan(NoteFolder folder)
    {
        var version = ++folder.ScanVersion;
        NoteFolderNode? tree;
        try
        {
            // With no UI thread to come back to, the scan runs here, so the rows are only ever touched in one place.
            tree = SynchronizationContext.Current == null
                ? NoteFolders.Scan(folder.Path)
                : await Task.Run(() => NoteFolders.Scan(folder.Path));
        }
        catch (Exception ex)
        {
            LogService.Error($"Could not look through {folder.Path}", ex);
            tree = null;
        }
        if (version != folder.ScanVersion || !OpenedFolders.Contains(folder)) return;

        folder.Tree = tree;
        folder.Scanned = true;
        folder.KnownFolders = KnownFolders(tree);
        if (tree == null)
        {
            // A watcher on a folder that is gone hears nothing more; a new one starts if it comes back.
            folder.Watcher?.Dispose();
            folder.Watcher = null;
        }
        Watch(folder);
        RebuildNoteRows();
    }

    private static HashSet<string> KnownFolders(NoteFolderNode? tree)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<NoteFolderNode>();
        if (tree != null) stack.Push(tree);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            known.Add(node.Path);
            foreach (var sub in node.Folders) stack.Push(sub);
        }
        return known;
    }

    /// <summary>Watches a folder for notes and folders coming and going; edits inside a note do not count.</summary>
    private void Watch(NoteFolder folder)
    {
        if (folder.Watcher != null || folder.Tree == null) return;
        var context = SynchronizationContext.Current;
        if (context == null) return;

        try
        {
            var watcher = new FileSystemWatcher(folder.Path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024
            };

            void Changed(string path)
            {
                if (Matters(folder, path)) context.Post(_ => RescanSoon(folder), null);
            }

            watcher.Created += (_, e) => Changed(e.FullPath);
            watcher.Deleted += (_, e) => Changed(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                if (Matters(folder, e.FullPath) || Matters(folder, e.OldFullPath)) context.Post(_ => RescanSoon(folder), null);
            };
            // Too many changes at once to list: look through the whole folder again.
            watcher.Error += (_, _) => context.Post(_ => RescanSoon(folder), null);
            watcher.EnableRaisingEvents = true;
            folder.Watcher = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
        {
            LogService.Error($"Could not watch {folder.Path} for changes", ex);
        }
    }

    /// <summary>Runs on the watcher's thread: only notes and the folders that hold them change the tree.</summary>
    private static bool Matters(NoteFolder folder, string path)
    {
        try
        {
            if (NoteFolders.InSkippedFolder(folder.Path, path)) return false;
            if (NoteFiles.IsNote(path)) return true;
            if (NoteFolders.IsSkipped(Path.GetFileName(path))) return false;
            return Directory.Exists(path) || folder.KnownFolders.Contains(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return false;
        }
    }

    /// <summary>Waits for a burst of changes, such as a checkout, to settle before looking again.</summary>
    private async void RescanSoon(NoteFolder folder)
    {
        var version = ++folder.ChangeVersion;
        await Task.Delay(RescanDelay);
        if (version == folder.ChangeVersion && OpenedFolders.Contains(folder)) Rescan(folder);
    }

    // ── The path above a note ───────────────────────────────────

    /// <summary>The folders from the deepest opened folder holding the note down to the note's own.</summary>
    public IReadOnlyList<NoteCrumb> CrumbsFor(string notePath)
    {
        var dir = Path.GetDirectoryName(notePath);
        if (dir == null) return Array.Empty<NoteCrumb>();
        var root = OpenedFolders.Where(f => f.Holds(dir)).MaxBy(f => f.Path.Length);
        if (root == null) return Array.Empty<NoteCrumb>();

        var crumbs = new List<NoteCrumb> { new(root.Name, root.Path, true) };
        var walk = root.Path;
        foreach (var part in Path.GetRelativePath(root.Path, dir).Split(Path.DirectorySeparatorChar))
        {
            if (part is "." or "") continue;
            walk = Path.Combine(walk, part);
            crumbs.Add(new NoteCrumb(part, walk, false));
        }
        return crumbs;
    }

    private void RefreshCrumbs()
    {
        if (OpenNotePage is { } note) note.Crumbs = CrumbsFor(note.Path);
    }
}
