using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>A folder opened under Notes, and what MikuDo knows about it.</summary>
public sealed class NoteFolder
{
    public NoteFolder(string path) => Path = path;

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;

    /// <summary>Files and folders kept out of the tree, as full paths; they show under Hidden instead.</summary>
    public HashSet<string> Hidden { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the last look through the folder found; null before it, or when the folder is gone.</summary>
    public NoteFolderNode? Tree { get; set; }

    public bool Scanned { get; set; }

    /// <summary>Every folder the last scan kept, so a deleted one can be told from a deleted file.</summary>
    public HashSet<string> KnownFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal FileSystemWatcher? Watcher { get; set; }
    internal int ScanVersion { get; set; }
    internal int ChangeVersion { get; set; }

    /// <summary>True for the folder itself and anything inside it.</summary>
    public bool Holds(string path) => NoteTreePaths.IsUnder(path, Path);
}

/// <summary>One folder in the path above a note, shown over its name.</summary>
public sealed record NoteCrumb(string Name, string Path, bool IsFirst);

/// <summary>How an opened folder is kept in settings: hidden items relative to the folder.</summary>
public sealed record StoredNoteFolder(string Path, List<string>? Hidden);

public static class NoteTreePaths
{
    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or lies inside it.</summary>
    public static bool IsUnder(string path, string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(folder);
        if (string.Equals(Path.TrimEndingDirectorySeparator(path), root, StringComparison.OrdinalIgnoreCase)) return true;
        return path.Length > root.Length + 1
               && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               && (path[root.Length] == Path.DirectorySeparatorChar || root.EndsWith(Path.DirectorySeparatorChar));
    }
}

/// <summary>
/// One row under NOTES in the sidebar. The tree is shown as a flat list of
/// rows, each indented by its depth, so opening a folder only inserts the rows
/// beneath it rather than rebuilding the list.
/// </summary>
public abstract partial class SidebarNoteRow : ObservableObject
{
    /// <summary>Pixels each level of the tree sits to the right of the one above it.</summary>
    public const double Step = 23;

    public int Depth { get; init; }

    /// <summary>Shown inside a Hidden group: drawn in the faint text colour.</summary>
    public bool IsDimmed { get; init; }

    /// <summary>The room above a top-level row: more after an open group than after a shut one.</summary>
    [ObservableProperty] private Thickness _spacing;

    /// <summary>Where the row starts: one step per level, past the guide line of the level above.</summary>
    public Thickness Indent => new(Depth == 0 ? 0 : Step * Depth + 1, 0, 0, Depth == 0 ? 0 : 1);

    /// <summary>Names the row across rebuilds: the same key is the same row.</summary>
    internal abstract string Key { get; }

    /// <summary>
    /// True when <paramref name="fresh"/> would draw just like this row apart
    /// from what <see cref="TakeStateFrom"/> copies, so this row, and what is
    /// already laid out for it, can stay.
    /// </summary>
    internal virtual bool LooksLike(SidebarNoteRow fresh)
        => fresh.GetType() == GetType() && fresh.Depth == Depth && fresh.IsDimmed == IsDimmed;

    /// <summary>Takes the state that can change on a row while it is on show.</summary>
    internal virtual void TakeStateFrom(SidebarNoteRow fresh) { }
}

/// <summary>The Recent group: notes opened lately, wherever they live.</summary>
public partial class RecentGroupRow : SidebarNoteRow
{
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private int _count;

    internal override string Key => "recent";

    internal override void TakeStateFrom(SidebarNoteRow fresh)
    {
        var row = (RecentGroupRow)fresh;
        IsExpanded = row.IsExpanded;
        Count = row.Count;
    }
}

/// <summary>A folder: one opened under Notes, or one inside it.</summary>
public partial class FolderRow : SidebarNoteRow
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCount))]
    private bool _isExpanded;

    public required NoteFolder Folder { get; init; }

    /// <summary>What the scan found in it; null for an opened folder not yet looked through, or gone.</summary>
    public NoteFolderNode? Node { get; set; }

    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsRoot { get; init; }

    /// <summary>Kept out of the tree by the user; it sits in its parent's Hidden group.</summary>
    public bool IsHiddenItem { get; init; }

    /// <summary>Notes in it and below that are not hidden.</summary>
    public int Count { get; init; }

    /// <summary>An opened folder that is no longer on disk.</summary>
    public bool IsMissing { get; init; }

    /// <summary>An opened folder always shows its count; one inside it, only while shut.</summary>
    public bool ShowCount => Count > 0 && (IsRoot || !IsExpanded);

    internal override string Key => $"f|{Folder.Path}|{Path}";

    internal override bool LooksLike(SidebarNoteRow fresh)
        => base.LooksLike(fresh) && fresh is FolderRow row && row.Folder == Folder && row.Name == Name && row.Count == Count
           && row.IsRoot == IsRoot && row.IsHiddenItem == IsHiddenItem && row.IsMissing == IsMissing;

    internal override void TakeStateFrom(SidebarNoteRow fresh)
    {
        var row = (FolderRow)fresh;
        Node = row.Node;
        IsExpanded = row.IsExpanded;
    }
}

/// <summary>Holds what the user kept out of one folder's tree.</summary>
public partial class HiddenGroupRow : SidebarNoteRow
{
    [ObservableProperty] private bool _isExpanded;

    public required NoteFolder Folder { get; init; }
    public required NoteFolderNode Node { get; set; }
    public int Count { get; init; }

    internal override string Key => $"h|{Folder.Path}|{Node.Path}";

    internal override bool LooksLike(SidebarNoteRow fresh)
        => base.LooksLike(fresh) && fresh is HiddenGroupRow row && row.Folder == Folder && row.Count == Count;

    internal override void TakeStateFrom(SidebarNoteRow fresh)
    {
        var row = (HiddenGroupRow)fresh;
        Node = row.Node;
        IsExpanded = row.IsExpanded;
    }
}

/// <summary>A note, in the Recent group or in a folder.</summary>
public partial class NoteRow : SidebarNoteRow
{
    [ObservableProperty] private bool _isActive;

    /// <summary>The note is open with changes not yet saved to its file.</summary>
    [ObservableProperty] private bool _isDirty;

    /// <summary>The file is no longer where it was: moved, renamed or deleted.</summary>
    [ObservableProperty] private bool _isMissing;

    public string Path { get; init; } = string.Empty;
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>The opened folder the note was found in; null for a note in Recent.</summary>
    public NoteFolder? Folder { get; init; }

    public bool IsHiddenItem { get; init; }

    /// <summary>Beside folders, a note's icon lines up with the folders' icons, past their chevrons.</summary>
    public bool AlignWithFolders { get; init; }

    internal override string Key => Folder == null ? $"r|{Path}" : $"n|{Folder.Path}|{Path}";

    internal override bool LooksLike(SidebarNoteRow fresh)
        => base.LooksLike(fresh) && fresh is NoteRow row && row.Folder == Folder
           && row.IsHiddenItem == IsHiddenItem && row.AlignWithFolders == AlignWithFolders;

    internal override void TakeStateFrom(SidebarNoteRow fresh)
    {
        var row = (NoteRow)fresh;
        IsActive = row.IsActive;
        IsDirty = row.IsDirty;
        IsMissing = row.IsMissing;
    }
}

/// <summary>
/// Stands after the first notes of a folder holding a great many, offering the
/// next batch, so opening such a folder never lays out hundreds of rows at once.
/// </summary>
public class MoreNotesRow : SidebarNoteRow
{
    public required NoteFolder Folder { get; init; }

    /// <summary>The folder whose notes these are.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Notes not on show yet.</summary>
    public int Remaining { get; init; }

    /// <summary>How many the next click shows.</summary>
    public int Next { get; init; }

    public bool AlignWithFolders { get; init; }

    public string Label => $"Show {Next} more";

    internal override string Key => $"m|{Folder.Path}|{Path}";

    internal override bool LooksLike(SidebarNoteRow fresh)
        => base.LooksLike(fresh) && fresh is MoreNotesRow row && row.Remaining == Remaining && row.Next == Next
           && row.AlignWithFolders == AlignWithFolders;
}

/// <summary>Stands under an opened folder with no notes in it, or one that is gone.</summary>
public class EmptyFolderRow : SidebarNoteRow
{
    public string Folder { get; init; } = string.Empty;
    public bool IsMissing { get; init; }

    internal override string Key => $"e|{Folder}";

    internal override bool LooksLike(SidebarNoteRow fresh)
        => base.LooksLike(fresh) && fresh is EmptyFolderRow row && row.IsMissing == IsMissing;
}
