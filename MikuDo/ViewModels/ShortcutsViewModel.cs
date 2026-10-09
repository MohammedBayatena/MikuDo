using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>A group's strip in the list: GENERAL, BOARD, TASK PAGE, NOTES, WINDOWS.</summary>
public sealed record ShortcutGroup(string Title);

/// <summary>
/// One command's row. Its keys can be changed in place: the row waits for the
/// new keys, and keys another command holds are only taken once the user
/// says so.
/// </summary>
public sealed partial class ShortcutRow : ObservableObject
{
    private readonly ShortcutsViewModel _owner;

    public ShortcutRow(ShortcutsViewModel owner, AppCommand command)
    {
        _owner = owner;
        Command = command;
        command.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    public AppCommand Command { get; }
    public string Name => Command.Def.Name;
    public string Description => IsRecording ? Problem ?? "Press the new shortcut, then Enter" : Command.Description;

    // ── Recording ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description), nameof(IsIdle), nameof(ShowReset))]
    private bool _isRecording;

    /// <summary>The keys pressed so far; modifiers alone show while they are held.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapturedCaps), nameof(HasCaptured))]
    private KeyChord? _captured;

    /// <summary>Why the keys pressed cannot be used, shown in place of the description.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description), nameof(HasProblem))]
    private string? _problem;

    public IReadOnlyList<string> CapturedCaps => Captured?.Caps ?? Array.Empty<string>();
    public bool HasCaptured => CapturedCaps.Count > 0;
    public bool HasProblem => IsRecording && Problem != null;

    // ── Keys someone else holds ─────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(ShowReset), nameof(ConflictCaps), nameof(ConflictName), nameof(ConflictBreak))]
    private bool _isConflict;

    private KeyChord? _wanted;
    private CommandDef? _holder;

    public IReadOnlyList<string> ConflictCaps => _wanted?.Caps ?? Array.Empty<string>();
    public string ConflictName => _holder?.Name ?? string.Empty;

    /// <summary>What follows the other command's name: a name ending in "…" needs no full stop after it.</summary>
    public string ConflictBreak => ConflictName.EndsWith('…') ? " " : ". ";

    /// <summary>Neither waiting for keys nor asking about them: the row shows its keys and its pencil.</summary>
    public bool IsIdle => !IsRecording && !IsConflict;

    /// <summary>"Reset to …" shows on a changed command that is neither waiting nor asking.</summary>
    public bool ShowReset => IsIdle && Command.IsChanged;

    public bool IsLast { get; set; }

    [RelayCommand]
    private void Edit() => _owner.StartRecording(this);

    /// <summary>A key went down while the row waits. Enter saves what was pressed before it; Esc gives up.</summary>
    public void Press(KeyChord chord)
    {
        if (chord.Key == System.Windows.Input.Key.Escape && chord.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            Cancel();
            return;
        }
        if (chord.Key == System.Windows.Input.Key.Enter && chord.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            Save();
            return;
        }

        Captured = chord;
        Problem = KeyChord.IsModifierKey(chord.Key) ? null : chord.Problem;
    }

    /// <summary>Modifiers let go of before a key: the box empties again.</summary>
    public void Release(KeyChord held)
    {
        if (Captured is { } shown && KeyChord.IsModifierKey(shown.Key)) Captured = held.Modifiers == 0 ? null : held;
    }

    [RelayCommand]
    private void Save()
    {
        if (Captured is not { } chord || KeyChord.IsModifierKey(chord.Key)) return;
        if (chord.Problem is { } problem)
        {
            Problem = problem;
            return;
        }

        IsRecording = false;
        Captured = null;
        Problem = null;
        _owner.Recording = null;

        if (chord == Command.Chord) return;
        if (Shortcuts.Holder(chord) is { } holder && holder.Id != Command.Id)
        {
            _wanted = chord;
            _holder = holder;
            IsConflict = true;
            _owner.Conflict = this;
            return;
        }
        Shortcuts.Set(Command.Id, chord);
    }

    [RelayCommand]
    public void Cancel()
    {
        IsRecording = false;
        Captured = null;
        Problem = null;
        if (_owner.Recording == this) _owner.Recording = null;
    }

    /// <summary>Gives this command the keys, and leaves the one that had them without.</summary>
    [RelayCommand]
    private void Replace()
    {
        if (_wanted is { } chord) Shortcuts.Move(Command.Id, chord);
        DropConflict();
    }

    [RelayCommand]
    public void DropConflict()
    {
        IsConflict = false;
        _wanted = null;
        _holder = null;
        if (_owner.Conflict == this) _owner.Conflict = null;
    }

    [RelayCommand]
    private void Reset() => Shortcuts.Reset(Command.Id);
}

/// <summary>Which rows the list shows.</summary>
public enum ShortcutFilter { All, Changed, None }

/// <summary>Settings → Keyboard shortcuts: every command, grouped, with its keys.</summary>
public sealed partial class ShortcutsViewModel : ObservableObject
{
    private readonly List<ShortcutRow> _rows;

    /// <summary>
    /// One list for the life of the app: Settings opens again onto the rows
    /// it built the first time, rather than building them all anew.
    /// </summary>
    public ShortcutsViewModel(MainViewModel main)
    {
        _rows = main.Commands.Select(c => new ShortcutRow(this, c)).ToList();
        Shortcuts.Changed += OnShortcutsChanged;
        Rebuild();
    }

    /// <summary>
    /// Settings opened again: the section folded, and once opened every row
    /// shown, nothing waiting for keys or asking about them.
    /// </summary>
    public void Fresh()
    {
        IsExpanded = false;
        Search = string.Empty;
        Filter = ShortcutFilter.All;
    }

    /// <summary>
    /// The section shows its list. Folded, Settings shows one row for it, so
    /// the keys of every command do not crowd the settings themselves.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FoldLabel))]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) return;
        Recording?.Cancel();
        Conflict?.DropConflict();
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    public string FoldLabel => IsExpanded ? "Hide" : $"Show all {_rows.Count}";

    public string ChangedNote => $"{Shortcuts.ChangedCount} changed";

    private void OnShortcutsChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ChangedLabel));
        OnPropertyChanged(nameof(NoneLabel));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangedNote));
        if (Filter != ShortcutFilter.All || Search.Length > 0) Rebuild();
    }

    /// <summary>Groups and rows, in the order shown.</summary>
    [ObservableProperty] private IReadOnlyList<object> _items = Array.Empty<object>();

    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private ShortcutFilter _filter;

    public string Summary
    {
        get
        {
            var with = _rows.Count(r => r.Command.HasChord);
            return $"{_rows.Count} commands · {with} with a shortcut";
        }
    }

    public string ChangedLabel => $"Changed · {Shortcuts.ChangedCount}";
    public string NoneLabel => $"No shortcut · {_rows.Count(r => !r.Command.HasChord)}";
    public bool HasChanges => Shortcuts.ChangedCount > 0;
    public bool IsEmpty => Items.Count == 0;

    /// <summary>The row waiting for keys, if one is; only one waits at a time.</summary>
    public ShortcutRow? Recording { get; set; }

    /// <summary>The row asking whether to take keys from another command.</summary>
    public ShortcutRow? Conflict { get; set; }

    partial void OnSearchChanged(string value) => Rebuild();
    partial void OnFilterChanged(ShortcutFilter value) => Rebuild();
    partial void OnItemsChanged(IReadOnlyList<object> value) => OnPropertyChanged(nameof(IsEmpty));

    [RelayCommand]
    private void SetFilter(string? filter) => Filter = Enum.TryParse<ShortcutFilter>(filter, out var f) ? f : ShortcutFilter.All;

    [RelayCommand]
    private void ResetAll()
    {
        if (!HasChanges) return;
        var count = Shortcuts.ChangedCount;
        if (!DialogService.Confirm(
                count == 1 ? "Put the changed shortcut back as it started?" : $"Put all {count} changed shortcuts back as they started?",
                "Reset all shortcuts"))
            return;
        Recording?.Cancel();
        Conflict?.DropConflict();
        Shortcuts.ResetAll();
    }

    public void StartRecording(ShortcutRow row)
    {
        if (Recording != null && Recording != row) Recording.Cancel();
        Conflict?.DropConflict();
        Recording = row;
        row.Captured = null;
        row.Problem = null;
        row.IsRecording = true;
    }

    /// <summary>Opens the list on one command, waiting for its new keys.</summary>
    public ShortcutRow? Record(string commandId)
    {
        IsExpanded = true;
        Search = string.Empty;
        Filter = ShortcutFilter.All;
        var row = _rows.FirstOrDefault(r => r.Command.Id == commandId);
        if (row != null) StartRecording(row);
        return row;
    }

    /// <summary>
    /// Keys pressed in the search box look for the command that has them, as
    /// the words of a shortcut: "Ctrl O" finds Open file….
    /// </summary>
    public void SearchByKeys(KeyChord chord) => Search = chord.Label;

    private static readonly (ShortcutArea Area, string Title)[] Groups =
    {
        (ShortcutArea.General, "GENERAL"), (ShortcutArea.Board, "BOARD"), (ShortcutArea.TaskPage, "TASK PAGE"),
        (ShortcutArea.Notes, "NOTES"), (ShortcutArea.Windows, "WINDOWS")
    };

    private void Rebuild()
    {
        var words = QuickSearch.Words(Search);
        bool Shown(ShortcutRow row)
        {
            if (Filter == ShortcutFilter.Changed && !row.Command.IsChanged) return false;
            if (Filter == ShortcutFilter.None && row.Command.HasChord) return false;
            if (words.Count == 0) return true;
            return QuickSearch.Has(row.Name + " " + row.Command.Description, words)
                   || (row.Command.Chord is { } chord && string.Equals(chord.Label, Search.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var items = new List<object>();
        ShortcutRow? last = null;
        foreach (var (area, title) in Groups)
        {
            var rows = _rows.Where(r => r.Command.Area == area && (Shown(r) || r == Recording || r == Conflict)).ToList();
            if (rows.Count == 0) continue;
            items.Add(new ShortcutGroup(title));
            foreach (var row in rows)
            {
                row.IsLast = false;
                items.Add(row);
                last = row;
            }
        }
        if (last != null) last.IsLast = true;
        Items = items;
    }
}
