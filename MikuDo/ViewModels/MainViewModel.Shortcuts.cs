using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;

namespace MikuDo.ViewModels;

/// <summary>
/// SHORTCUTS in the sidebar: what was opened lately, tasks and notes alike, and
/// the tasks and notes pinned there to keep them a click away. A row of squares
/// unless the user asks for the list. Vault tasks never show: their titles are
/// not readable here.
/// </summary>
/// <remarks>
/// Pins, and what was opened lately, are each kept as one list, the latest
/// first: a task by its id, a note by "note:" and its path. A task is a
/// <see cref="ShortcutTask"/>, a note a <see cref="ShortcutNoteRow"/>.
/// </remarks>
public partial class MainViewModel
{
    private const int ShortcutRecentLimit = 12;
    private const string NotePin = "note:";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowShortcutList), nameof(ShowShortcutSquares))]
    private bool _isShortcutsOpen = App.Database.GetSetting("SidebarShortcutsOpen") != "False";

    /// <summary>Squares unless the user asked for the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowShortcutList), nameof(ShowShortcutSquares))]
    private bool _isShortcutsCompact = App.Database.GetSetting("SidebarShortcutsCompact") != "False";

    public bool ShowShortcutList => IsShortcutsOpen && !IsShortcutsCompact;
    public bool ShowShortcutSquares => IsShortcutsOpen && IsShortcutsCompact;

    /// <summary>Recent in the list is open on what was opened lately; shut, as Recent under NOTES starts.</summary>
    [ObservableProperty] private bool _isRecentShortcutsOpen = App.Database.GetSetting("ShortcutsRecentOpen") == "True";

    /// <summary>The task or note opened last that is not pinned; null when there is none, or Recent was cleared.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentShortcut))]
    private object? _recentShortcut;

    public bool HasRecentShortcut => RecentShortcut != null;

    /// <summary>The tasks and notes opened lately that are not pinned, newest first, for the bar over the Recent square.</summary>
    [ObservableProperty] private IReadOnlyList<object> _recentBar = Array.Empty<object>();

    /// <summary>Every pinned task and note, the latest pinned first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinnedShortcuts), nameof(PinnedShortcutCount))]
    private IReadOnlyList<object> _pinnedShortcuts = Array.Empty<object>();

    public bool HasPinnedShortcuts => PinnedShortcuts.Count > 0;
    public int PinnedShortcutCount => PinnedShortcuts.Count;

    /// <summary>The pins the row of squares has room for, after the Recent one.</summary>
    [ObservableProperty] private IReadOnlyList<object> _pinnedSquares = Array.Empty<object>();

    /// <summary>Pins left out of the row of squares, behind its More square.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMorePinned))]
    private int _morePinned;

    public bool HasMorePinned => MorePinned > 0;

    /// <summary>Raised whenever a task or a note is pinned or unpinned, from anywhere.</summary>
    public event EventHandler? PinsChanged;

    private List<string>? _pins;
    private List<string>? _recentEntries;

    private List<string> Pins => _pins ??= ReadIds("ShortcutPins");
    private List<string> RecentEntries => _recentEntries ??= ReadIds("ShortcutRecent");

    private static List<string> ReadIds(string key)
    {
        var stored = App.Database.GetSetting(key);
        if (string.IsNullOrEmpty(stored)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(stored) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    private static void SaveIds(string key, List<string> ids) => App.Database.SaveSetting(key, JsonSerializer.Serialize(ids));

    /// <summary>Whether two entries name the same task, or the same note's file.</summary>
    private static bool SameEntry(string a, string b)
        => a.StartsWith(NotePin, StringComparison.Ordinal) && b.StartsWith(NotePin, StringComparison.Ordinal)
            ? SamePathQuietly(a[NotePin.Length..], b[NotePin.Length..])
            : a == b;

    partial void OnIsShortcutsOpenChanged(bool value)
        => App.Database.SaveSetting("SidebarShortcutsOpen", value ? "True" : "False");

    partial void OnIsShortcutsCompactChanged(bool value)
        => App.Database.SaveSetting("SidebarShortcutsCompact", value ? "True" : "False");

    partial void OnIsRecentShortcutsOpenChanged(bool value)
        => App.Database.SaveSetting("ShortcutsRecentOpen", value ? "True" : "False");

    [RelayCommand]
    private void ToggleRecentShortcuts() => IsRecentShortcutsOpen = !IsRecentShortcutsOpen;

    [RelayCommand]
    private void ToggleShortcutsSection() => IsShortcutsOpen = !IsShortcutsOpen;

    [RelayCommand]
    private void ToggleShortcutsCompact()
    {
        IsShortcutsCompact = !IsShortcutsCompact;
        IsShortcutsOpen = true;
    }

    public bool IsPinned(string? taskId) => taskId != null && Pins.Contains(taskId);

    public bool IsNotePinned(string path) => Pins.Any(p => SameEntry(p, NotePin + path));

    /// <summary>
    /// Pins <paramref name="tasks"/>, ahead of everything pinned before and in
    /// the order given, or unpins them. Vault tasks are left out.
    /// </summary>
    public void SetPinned(IEnumerable<TodoItem> tasks, bool pin)
    {
        var ids = tasks.Where(t => !t.IsVault).Select(t => t.IdText).Distinct().ToList();
        if (ids.Count == 0) return;

        Pins.RemoveAll(ids.Contains);
        if (pin) Pins.InsertRange(0, ids);
        PinsSaved();
    }

    /// <summary>Pins the note at <paramref name="path"/> ahead of everything pinned before, or unpins it.</summary>
    public void SetNotePinned(string path, bool pin)
    {
        var entry = NotePin + Path.GetFullPath(path);
        Pins.RemoveAll(p => SameEntry(p, entry));
        if (pin) Pins.Insert(0, entry);
        PinsSaved();
    }

    private void PinsSaved()
    {
        SaveIds("ShortcutPins", Pins);
        RefreshShortcuts();
        PinsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts a task, or a note by "note:" and its path, first among what was opened lately.</summary>
    private void RememberShortcutRecent(string entry)
    {
        RecentEntries.RemoveAll(e => SameEntry(e, entry));
        RecentEntries.Insert(0, entry);
        if (RecentEntries.Count > ShortcutRecentLimit) RecentEntries.RemoveRange(ShortcutRecentLimit, RecentEntries.Count - ShortcutRecentLimit);
        SaveIds("ShortcutRecent", RecentEntries);
        RefreshShortcuts();
    }

    /// <summary>Opens a shortcut: a task's page, or a note.</summary>
    [RelayCommand]
    private void OpenShortcut(object? shortcut)
    {
        if (shortcut is ShortcutTask task) OpenTaskAnywhere(task.Item);
        else if (shortcut is NoteRow note) OpenNote(note.Path);
    }

    [RelayCommand]
    private void PinShortcut(object? shortcut)
    {
        if (shortcut is ShortcutTask task) SetPinned(new[] { task.Item }, true);
        else if (shortcut is NoteRow note) SetNotePinned(note.Path, true);
    }

    [RelayCommand]
    private void UnpinShortcut(object? shortcut)
    {
        if (shortcut is ShortcutTask task) SetPinned(new[] { task.Item }, false);
        else if (shortcut is NoteRow note) SetNotePinned(note.Path, false);
    }

    /// <summary>Pins a note to SHORTCUTS, or unpins it, from its menu anywhere in the sidebar.</summary>
    [RelayCommand]
    private void ToggleNotePin(NoteRow? note)
    {
        if (note != null) SetNotePinned(note.Path, !IsNotePinned(note.Path));
    }

    /// <summary>Empties Recent. The tasks and notes themselves are left alone.</summary>
    [RelayCommand]
    private void ClearRecentShortcut()
    {
        if (RecentEntries.Count == 0) return;
        RecentEntries.Clear();
        SaveIds("ShortcutRecent", RecentEntries);
        RefreshShortcuts();
    }

    /// <summary>Unpins every task and note, which Undo takes back.</summary>
    [RelayCommand]
    private void ClearPinnedShortcuts()
    {
        if (Pins.Count == 0) return;
        var was = Pins.ToList();
        Pins.Clear();
        PinsSaved();

        PushUndo("clear pinned", null, () =>
        {
            Pins.Clear();
            Pins.AddRange(was);
            PinsSaved();
        });
    }

    /// <summary>
    /// Reads the shortcuts again: titles, lists and trashings change under
    /// them. A task in the trash stays pinned and comes back if restored; one
    /// deleted for good is forgotten. A note whose file has gone stays, marked
    /// missing, as it does under Recent.
    /// </summary>
    public void RefreshShortcuts()
    {
        var names = Workspaces.ToDictionary(w => w.Id ?? string.Empty, w => w.Name);
        string Where(TodoItem t) => names.GetValueOrDefault(t.WorkspaceId ?? string.Empty, "Workspace");

        TodoItem? Find(string id)
        {
            try { return App.Database.GetTodoById(new LiteDB.ObjectId(id)); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { return null; }
        }

        // What an entry stands for, if it can be shown; deleted tasks are gathered to be forgotten.
        var gone = new List<string>();
        object? Shortcut(string entry, bool pinned)
        {
            if (entry.StartsWith(NotePin, StringComparison.Ordinal))
            {
                var path = entry[NotePin.Length..];
                return new ShortcutNoteRow { Path = path, IsPinned = pinned, IsMissing = !File.Exists(path) };
            }
            if (Find(entry) is not { } task)
            {
                gone.Add(entry);
                return null;
            }
            return task.IsVault || task.Status == TodoStatus.Trashed ? null : new ShortcutTask(task, pinned, Where(task));
        }

        var pinnedNow = Pins.Select(p => Shortcut(p, true)).OfType<object>().ToList();
        var recents = RecentEntries.Where(e => !Pins.Any(p => SameEntry(p, e)))
                                   .Select(e => Shortcut(e, false)).OfType<object>()
                                   .Take(RecentBarLimit).ToList();
        if (gone.Count > 0)
        {
            Pins.RemoveAll(gone.Contains);
            RecentEntries.RemoveAll(gone.Contains);
            SaveIds("ShortcutPins", Pins);
            SaveIds("ShortcutRecent", RecentEntries);
        }

        if (!recents.Select(ShortcutLook).SequenceEqual(RecentBar.Select(ShortcutLook)))
        {
            RecentBar = recents;
            RecentShortcut = recents.FirstOrDefault();
        }
        if (!pinnedNow.Select(ShortcutLook).SequenceEqual(PinnedShortcuts.Select(ShortcutLook))) PinnedShortcuts = pinnedNow;
        SyncShortcutSquares();
        MarkShortcuts();
    }

    /// <summary>What a shortcut's row or square shows, so an unchanged one can stay as it is.</summary>
    private static string ShortcutLook(object shortcut) => shortcut switch
    {
        ShortcutTask task => task.Look,
        ShortcutNoteRow note => $"note|{note.Path}|{note.IsPinned}|{note.IsMissing}",
        _ => string.Empty
    };

    /// <summary>
    /// Fills the row of squares: Recent takes the first column, and the pins
    /// as many of the rest as they fit, the last counting any left over.
    /// </summary>
    private void SyncShortcutSquares()
    {
        var room = WorkspaceSquareCount - 1;
        var all = PinnedShortcuts;
        var shown = all.Count <= room ? all.ToList() : all.Take(room - 1).ToList();
        if (!shown.SequenceEqual(PinnedSquares)) PinnedSquares = shown;
        MorePinned = all.Count - shown.Count;
    }

    /// <summary>Marks the shortcut whose page is on screen: a task's, or a note's.</summary>
    private void MarkShortcuts()
    {
        var open = CurrentPage == AppPage.TaskDetail ? OpenTaskPage?.TaskId : null;
        foreach (var shortcut in RecentBar.Concat(PinnedShortcuts))
        {
            if (shortcut is ShortcutTask task) task.IsActive = task.Id == open;
            else if (shortcut is NoteRow note) MarkNote(note);
        }
    }
}
