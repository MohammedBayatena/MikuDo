using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>What Quick Access looks through: everything, or one kind only.</summary>
public enum QuickMode { Everything, Tasks, Commands, Notes, Settings }

// ── Rows ────────────────────────────────────────────────────────

/// <summary>
/// A row of the list. Rows are kept from one search to the next: a new list
/// is laid over the one on screen row by row, and a row of the same kind takes the
/// new one's content in place, so its container and template are used again
/// rather than built anew on every key press.
/// </summary>
public abstract class QuickRow : ObservableObject
{
    /// <summary>Takes <paramref name="other"/>'s content when it is the same kind of row. False when it is not.</summary>
    public bool Take(QuickRow other)
    {
        if (other.GetType() != GetType()) return false;
        CopyFrom(other);
        OnPropertyChanged(string.Empty);
        return true;
    }

    protected abstract void CopyFrom(QuickRow other);
}

/// <summary>A group's heading: "TASKS 12", "COMMANDS · used most".</summary>
public sealed class QuickHeader : QuickRow
{
    public QuickHeader(string title, string? note = null, string? count = null)
    {
        Title = title;
        Note = note;
        Count = count;
    }

    public string Title { get; private set; }
    public string? Note { get; private set; }
    public string? Count { get; private set; }

    /// <summary>The note as it follows the title, a space before it.</summary>
    public string NoteText => Note == null ? string.Empty : " " + Note;

    protected override void CopyFrom(QuickRow other)
    {
        var header = (QuickHeader)other;
        (Title, Note, Count) = (header.Title, header.Note, header.Count);
    }
}

/// <summary>A row that can be picked out with the arrow keys and opened.</summary>
public abstract class QuickChoice : QuickRow
{
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>Enter, or a click; <c>alternate</c> is Ctrl held with it.</summary>
    public required Action<bool> Activate { get; set; }

    protected override void CopyFrom(QuickRow other) => Activate = ((QuickChoice)other).Activate;
}

/// <summary>"Show all 12 tasks", under the first few.</summary>
public sealed class QuickShowAll : QuickChoice
{
    public required string Text { get; set; }

    protected override void CopyFrom(QuickRow other)
    {
        base.CopyFrom(other);
        Text = ((QuickShowAll)other).Text;
    }
}

/// <summary>One thing Quick Access found or offers.</summary>
public class QuickItem : QuickChoice
{
    public Geometry? Icon { get; set; }
    public required string Title { get; set; }

    /// <summary>The words to pick out in the title and the quote; null when the title did not match.</summary>
    public IReadOnlyList<string>? TitleWords { get; set; }
    public IReadOnlyList<string>? SnippetWords { get; set; }

    /// <summary>Lighter words after the title: a note's folder, a task's workspace and column.</summary>
    public string? Detail { get; set; }
    public Brush? DetailDot { get; set; }

    /// <summary>Where the match was: "in title", "line 18".</summary>
    public string? Chip { get; set; }

    /// <summary>A second line quoting the match.</summary>
    public string? Snippet { get; set; }
    public bool HasSnippet => Snippet != null;

    // The right-hand side
    public string? Hint { get; set; }
    public string? Prefix { get; set; }
    public IReadOnlyList<string>? Caps { get; set; }
    public bool HasCaps => Caps is { Count: > 0 };

    /// <summary>"no shortcut", for a command without keys.</summary>
    public bool NoShortcut { get; set; }

    /// <summary>A command's group, in the commands-only list.</summary>
    public string? Category { get; set; }

    /// <summary>The page on screen as a place to search. It is offered first but not picked out, so Enter still opens what was typed.</summary>
    public bool IsPageFind { get; set; }

    /// <summary>Offers "Assign…" while picked out or pointed at: a command without keys, in the commands-only list.</summary>
    public bool CanAssign { get; set; }
    public Action? Assign { get; set; }

    public Brush? Dot { get; set; }
    public string? Place { get; set; }

    /// <summary>The words on the right, whichever the row has: a hint, a task's workspace, a group, or that there are no keys.</summary>
    public string? Aside => Hint ?? Place ?? (NoShortcut ? "no shortcut" : null);

    /// <summary>A task's workspace reads stronger than the rest.</summary>
    public bool AsideStrong => Hint == null && Place != null;

    protected override void CopyFrom(QuickRow other)
    {
        base.CopyFrom(other);
        var item = (QuickItem)other;
        Icon = item.Icon;
        Title = item.Title;
        TitleWords = item.TitleWords;
        SnippetWords = item.SnippetWords;
        Detail = item.Detail;
        DetailDot = item.DetailDot;
        Chip = item.Chip;
        Snippet = item.Snippet;
        Hint = item.Hint;
        Prefix = item.Prefix;
        Caps = item.Caps;
        NoShortcut = item.NoShortcut;
        Category = item.Category;
        IsPageFind = item.IsPageFind;
        CanAssign = item.CanAssign;
        Assign = item.Assign;
        Dot = item.Dot;
        Place = item.Place;
    }
}

/// <summary>A command in the commands-only list, laid out with its group and a way to give it keys.</summary>
public sealed class QuickCommandRow : QuickItem { }

/// <summary>Shown alone when nothing matches.</summary>
public sealed class QuickEmpty : QuickRow
{
    public QuickEmpty(string text) => Text = text;
    public string Text { get; private set; }

    protected override void CopyFrom(QuickRow other) => Text = ((QuickEmpty)other).Text;
}

/// <summary>One hint in the strip along the bottom: keys or prefixes, then what they do.</summary>
public sealed record QuickHint(IReadOnlyList<string> Caps, IReadOnlyList<string> Prefixes, string Text)
{
    public bool HasCaps => Caps.Count > 0;
    public bool HasPrefixes => Prefixes.Count > 0;
}

/// <summary>
/// The list under the search bar. Empty, it offers the ways to search, the
/// commands run most and what was opened last; typed into, it shows what
/// matches, grouped by kind. A prefix (# &gt; n: ?) or Tab narrows it to one kind.
/// </summary>
public sealed partial class QuickAccessViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _typing;
    private CancellationTokenSource? _noteSearch;

    public QuickAccessViewModel(MainViewModel main)
    {
        _main = main;
        _typing = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(70) };
        _typing.Tick += (_, _) =>
        {
            _typing.Stop();
            Rebuild();
        };
        Shortcuts.Changed += () => OnPropertyChanged(nameof(OpenCaps));
    }

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private QuickMode _mode;
    /// <summary>The rows shown, patched in place from one search to the next.</summary>
    public System.Collections.ObjectModel.ObservableCollection<QuickRow> Rows { get; } = new();
    [ObservableProperty] private IReadOnlyList<QuickHint> _hints = Array.Empty<QuickHint>();
    [ObservableProperty] private int _resultCount;

    /// <summary>The row picked out; Enter opens it.</summary>
    [ObservableProperty] private QuickChoice? _selected;

    public bool HasQuery => Query.Trim().Length > 0;

    /// <summary>Esc, as the strip along the bottom draws it.</summary>
    public IReadOnlyList<string> EscCaps { get; } = new[] { "Esc" };

    /// <summary>The keys that open Quick Access, shown in the closed bar.</summary>
    public IReadOnlyList<string> OpenCaps => Shortcuts.ChordFor("quick.open")?.Caps ?? Array.Empty<string>();

    /// <summary>The list has closed; the window takes the caret out of the bar.</summary>
    public event Action? Closed;
    public bool HasMode => Mode != QuickMode.Everything;
    public string ResultLabel => ResultCount == 1 ? "1 result" : $"{ResultCount} results";

    public string ModePrefix => Prefix(Mode);

    public string ModeName => Mode switch
    {
        QuickMode.Tasks => "Tasks",
        QuickMode.Commands => "Commands",
        QuickMode.Notes => "Notes",
        QuickMode.Settings => "Settings",
        _ => string.Empty
    };

    public string Placeholder => Mode switch
    {
        QuickMode.Tasks => "Type a task's name or words in it",
        QuickMode.Commands => "Type a command",
        QuickMode.Notes => "Type a note's name or words in it",
        QuickMode.Settings => "Type a setting",
        _ => "Search tasks, notes, settings and commands"
    };

    /// <summary>Asked of the window: put the caret in the search bar.</summary>
    public event Action? FocusRequested;

    /// <summary>Asked of the window: bring the picked-out row into view.</summary>
    public event Action<QuickChoice>? SelectedShown;

    private static string Prefix(QuickMode mode) => mode switch
    {
        QuickMode.Tasks => "#",
        QuickMode.Commands => ">",
        QuickMode.Notes => "n:",
        QuickMode.Settings => "?",
        _ => string.Empty
    };

    // ── Opening and closing ─────────────────────────────────────

    /// <summary>Opens the list in <paramref name="mode"/>; already open, it switches to that mode.</summary>
    public void Open(QuickMode mode)
    {
        var wasOpen = IsOpen;
        _snapshot = null;
        Mode = mode;
        IsOpen = true;
        if (!wasOpen && !_main.IsSearchBoardShown) SetQuery(string.Empty);
        Rebuild();
        FocusRequested?.Invoke();
    }

    /// <summary>Closes the list. What was typed goes, unless the search board on screen is showing it.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        _typing.Stop();
        _noteSearch?.Cancel();
        IsOpen = false;
        Mode = QuickMode.Everything;
        SetQuery(_main.IsSearchBoardShown ? _main.GlobalSearch : string.Empty);
        // The rows stay, out of sight, so the next opening fills the same ones.
        Selected = null;
        _snapshot = null;
        Closed?.Invoke();
    }

    /// <summary>
    /// The search board took up or dropped a search: the closed bar shows what
    /// the board is searching for, so the × can end it.
    /// </summary>
    public void ShowBoardQuery(string query)
    {
        if (!IsOpen) SetQuery(query);
    }

    /// <summary>The bar's ×: empties it, and leaves the search board if that is on screen.</summary>
    public void Clear()
    {
        SetQuery(string.Empty);
        _main.ClearGlobalSearchCommand.Execute(null);
        if (IsOpen) Rebuild();
    }

    private bool _settingQuery;

    private void SetQuery(string text)
    {
        _settingQuery = true;
        Query = text;
        _settingQuery = false;
    }

    partial void OnQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasQuery));
        if (_settingQuery) return;

        if (!IsOpen)
        {
            // Typing into the closed bar opens it with what was typed.
            IsOpen = true;
            Mode = QuickMode.Everything;
        }

        // A prefix typed at the start narrows the list to one kind, and is not part of the search.
        if (Mode == QuickMode.Everything)
        {
            foreach (var mode in new[] { QuickMode.Tasks, QuickMode.Commands, QuickMode.Notes, QuickMode.Settings })
            {
                var prefix = Prefix(mode);
                if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                Mode = mode;
                SetQuery(value[prefix.Length..].TrimStart());
                Rebuild();
                return;
            }
        }

        _typing.Stop();
        _typing.Start();
    }

    partial void OnModeChanged(QuickMode value)
    {
        OnPropertyChanged(nameof(HasMode));
        OnPropertyChanged(nameof(ModePrefix));
        OnPropertyChanged(nameof(ModeName));
        OnPropertyChanged(nameof(Placeholder));
    }

    partial void OnResultCountChanged(int value) => OnPropertyChanged(nameof(ResultLabel));

    partial void OnSelectedChanged(QuickChoice? oldValue, QuickChoice? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue == null) return;
        newValue.IsSelected = true;
        if (!_quietly) SelectedShown?.Invoke(newValue);
    }

    // ── Keys ────────────────────────────────────────────────────

    private IEnumerable<QuickChoice> Choices => Rows.OfType<QuickChoice>();

    /// <summary>Picks out the row <paramref name="step"/> rows on, round at the ends.</summary>
    public void Move(int step)
    {
        var choices = Choices.ToList();
        if (choices.Count == 0) return;
        var at = Selected == null ? -1 : choices.IndexOf(Selected);
        if (at < 0)
        {
            Selected = step > 0 ? choices[0] : choices[^1];
            return;
        }
        var next = at + step;
        // A long step stops at the end; a single step goes round.
        if (Math.Abs(step) > 1) next = Math.Clamp(next, 0, choices.Count - 1);
        else next = ((next % choices.Count) + choices.Count) % choices.Count;
        Selected = choices[next];
    }

    public void ActivateSelected(bool alternate) => Selected?.Activate(alternate);

    private static readonly QuickMode[] ModeCycle =
        { QuickMode.Everything, QuickMode.Tasks, QuickMode.Commands, QuickMode.Notes, QuickMode.Settings };

    /// <summary>Tab: the next kind; Shift+Tab: the one before.</summary>
    public void NextMode(int step)
    {
        var at = Array.IndexOf(ModeCycle, Mode);
        Mode = ModeCycle[((at + step) % ModeCycle.Length + ModeCycle.Length) % ModeCycle.Length];
        Rebuild();
    }

    /// <summary>Backspace in an empty bar: back from one kind to everything. False when there was nowhere to go back to.</summary>
    public bool Back()
    {
        if (Mode == QuickMode.Everything || HasQuery) return false;
        Mode = QuickMode.Everything;
        Rebuild();
        return true;
    }

    // ── Building the list ───────────────────────────────────────

    private const int TaskPreview = 4;
    private const int NotePreview = 3;
    private const int SettingPreview = 3;
    private const int CommandPreview = 4;
    private const int ListLimit = 60;

    /// <summary>The tasks there are to search, read once per opening: every keystroke after that searches memory.</summary>
    private Dictionary<string, TodoItem>? _snapshot;

    private Dictionary<string, TodoItem> Tasks => _snapshot ??= App.Database.GetTodoLookup();

    public void Rebuild()
    {
        _typing.Stop();
        _noteSearch?.Cancel();
        _noteSearch = null;
        if (!IsOpen) return;

        var words = QuickSearch.Words(Query);
        var rows = new List<QuickRow>();
        var count = 0;

        switch (Mode)
        {
            case QuickMode.Everything when words.Count == 0:
                Starting(rows);
                break;
            case QuickMode.Everything:
                count = Everything(rows, words);
                break;
            case QuickMode.Tasks:
                count = TaskList(rows, words);
                break;
            case QuickMode.Commands:
                count = CommandList(rows, words);
                break;
            case QuickMode.Notes:
                count = NoteList(rows, words);
                break;
            case QuickMode.Settings:
                count = SettingList(rows, words);
                break;
        }

        ResultCount = count;
        Show(Finish(rows, words));
        Hints = HintsFor();
        _quietly = true;
        Selected = Choices.FirstOrDefault(c => c is not QuickItem { IsPageFind: true }) ?? Choices.FirstOrDefault();
        _quietly = false;
        RowsReplaced?.Invoke();
    }

    /// <summary>
    /// Lays <paramref name="next"/> over the rows shown. For each new row the
    /// first row of its kind not yet used is moved up to its place and takes its
    /// content; only when there is none is a row added. What is left over goes.
    /// Groups appear and disappear as a search changes, so rows shift; moving
    /// keeps each one's container, where replacing would build it again.
    /// </summary>
    private void Show(List<QuickRow> next)
    {
        for (var i = 0; i < next.Count; i++)
        {
            var kind = next[i].GetType();
            var at = -1;
            for (var j = i; j < Rows.Count; j++)
            {
                if (Rows[j].GetType() != kind) continue;
                at = j;
                break;
            }

            if (at < 0) Rows.Insert(i, next[i]);
            else
            {
                if (at != i) Rows.Move(at, i);
                Rows[i].Take(next[i]);
            }
        }
        while (Rows.Count > next.Count) Rows.RemoveAt(Rows.Count - 1);
    }

    /// <summary>Set while a new list picks out its first row: that needs no scrolling to.</summary>
    private bool _quietly;

    /// <summary>A new list is up: the view goes back to its top.</summary>
    public event Action? RowsReplaced;

    /// <summary>The list as shown: an empty one says so.</summary>
    private List<QuickRow> Finish(List<QuickRow> rows, IReadOnlyList<string> words)
    {
        if (rows.Count == 0) rows.Add(new QuickEmpty(Nothing(words)));
        return rows;
    }

    private string Nothing(IReadOnlyList<string> words)
    {
        var what = Mode switch
        {
            QuickMode.Tasks => "No task",
            QuickMode.Commands => "No command",
            QuickMode.Notes => "No note",
            QuickMode.Settings => "No setting",
            _ => "Nothing"
        };
        return words.Count == 0 ? $"{what} to show yet" : $"{what} matches “{Query.Trim()}”";
    }

    // ── Just opened ─────────────────────────────────────────────

    private void Starting(List<QuickRow> rows)
    {
        rows.Add(new QuickHeader("SEARCH IN"));
        if (FindHereRow(null, Array.Empty<string>()) is { } here) rows.Add(here);
        rows.Add(ModeRow(QuickMode.Everything, "Everything", "IconSearch", "quick.open", "tasks, notes, settings, commands"));
        rows.Add(ModeRow(QuickMode.Tasks, "Go to a task", "IconHash", "quick.tasks"));
        rows.Add(ModeRow(QuickMode.Commands, "Run a command", "IconPrompt", "quick.commands"));
        rows.Add(ModeRow(QuickMode.Notes, "Notes", "IconNoteSmall", "quick.notes"));
        rows.Add(ModeRow(QuickMode.Settings, "Settings", "IconSettings", "quick.settings"));

        rows.Add(new QuickHeader("COMMANDS", "· used most"));
        foreach (var command in _main.MostUsedCommands(6))
            rows.Add(CommandRow(command, words: null, useHint: true));

        var recent = RecentRows(3);
        if (recent.Count == 0) return;
        rows.Add(new QuickHeader("RECENTLY OPENED"));
        rows.AddRange(recent);
    }

    /// <summary>
    /// The page on screen as a place to search: "This note" with nothing
    /// typed, "Find “deadline” in this note" with something. Picked, it opens
    /// the page's own find with the words in it. None where the page has no find.
    /// It heads the list, but what was searched for is what Enter opens.
    /// </summary>
    private QuickItem? FindHereRow(string? text, IReadOnlyList<string> words)
    {
        if (_main.FindHost is not { } host) return null;
        var noun = host.FindNoun;
        return new QuickItem
        {
            Icon = Icon("IconSearch"),
            Title = text == null ? $"This {noun}"
                  : noun == "board" ? $"Search this board for “{text}”"
                  : $"Find “{text}” in this {noun}",
            TitleWords = text == null ? null : words,
            IsPageFind = true,
            Hint = _main.FindPlace() is { Length: > 0 } place ? Shorten(place, 36) : null,
            Caps = Shortcuts.ChordFor("find.open")?.Caps,
            Activate = _ =>
            {
                Close();
                _main.FindHere(text);
            }
        };
    }

    private static string Shorten(string text, int most) => text.Length <= most ? text : text[..(most - 1)].TrimEnd() + "…";

    private QuickItem ModeRow(QuickMode mode, string title, string icon, string commandId, string? hint = null) => new()
    {
        Icon = Icon(icon),
        Title = title,
        Hint = hint,
        Prefix = mode == QuickMode.Everything ? null : Prefix(mode),
        Caps = Shortcuts.ChordFor(commandId)?.Caps,
        Activate = _ =>
        {
            if (Mode == mode) return;
            Mode = mode;
            Rebuild();
        }
    };

    private List<QuickItem> RecentRows(int limit)
    {
        var rows = new List<QuickItem>();
        foreach (var opened in _main.RecentlyOpened)
        {
            if (rows.Count == limit) break;
            if (opened.Kind == MainViewModel.TaskKind && Tasks.TryGetValue(opened.Key, out var task))
            {
                rows.Add(TaskRow(task, words: Array.Empty<string>(), recent: true));
            }
            else if (opened.Kind == MainViewModel.NoteKind && File.Exists(opened.Key))
            {
                rows.Add(NoteRow(opened.Key, Array.Empty<string>(), recent: true));
            }
        }
        return rows;
    }

    // ── Everything ──────────────────────────────────────────────

    private int Everything(List<QuickRow> rows, IReadOnlyList<string> words)
    {
        var tasks = FindTasks(words);
        var notes = FindNotesByName(words);
        var settings = FindSettings(words);
        var commands = FindCommands(words);

        List<QuickRow> Compose(List<NoteMatch> noteList)
        {
            var list = new List<QuickRow>();
            if (FindHereRow(Query.Trim(), words) is { } here)
            {
                list.Add(new QuickHeader($"THIS {_main.FindHost?.FindNoun.ToUpperInvariant()}"));
                list.Add(here);
            }
            Group(list, "TASKS", tasks.Count, tasks.Take(TaskPreview).Select(t => TaskRow(t.Task, words, match: t)),
                  tasks.Count > TaskPreview ? ShowAllTasks(tasks.Count) : null);
            Group(list, "NOTES", noteList.Count, noteList.Take(NotePreview).Select(n => NoteRow(n, words)),
                  noteList.Count > NotePreview ? ShowAllOf(QuickMode.Notes, noteList.Count, "notes") : null);
            Group(list, "SETTINGS", settings.Count, settings.Take(SettingPreview).Select(s => SettingRow(s, words)),
                  settings.Count > SettingPreview ? ShowAllOf(QuickMode.Settings, settings.Count, "settings") : null);
            Group(list, "COMMANDS", commands.Count, commands.Take(CommandPreview).Select(c => CommandRow(c, words, useHint: false)),
                  commands.Count > CommandPreview ? ShowAllOf(QuickMode.Commands, commands.Count, "commands") : null);
            return list;
        }

        // Notes found by name show at once; those found by a line inside join as the files are read.
        rows.AddRange(Compose(notes));
        SearchNoteLines(words, notes, found => Compose(notes.Concat(found).ToList()));
        var count = tasks.Count + notes.Count + settings.Count + commands.Count;
        // Under the page's own find, the list still says that nothing else matched.
        if (count == 0 && rows.Count > 0) rows.Add(new QuickEmpty($"No task, note, setting or command matches “{Query.Trim()}”"));
        return count;
    }

    private static void Group(List<QuickRow> rows, string title, int count, IEnumerable<QuickRow> items, QuickRow? more)
    {
        if (count == 0) return;
        rows.Add(new QuickHeader(title, count: count.ToString()));
        rows.AddRange(items);
        if (more != null) rows.Add(more);
    }

    private QuickShowAll ShowAllTasks(int count) => new()
    {
        Text = $"Show all {count} tasks",
        Activate = _ =>
        {
            var query = Query.Trim();
            Close();
            _main.ShowSearchBoard(query);
        }
    };

    private QuickShowAll ShowAllOf(QuickMode mode, int count, string what) => new()
    {
        Text = $"Show all {count} {what}",
        Activate = _ =>
        {
            Mode = mode;
            Rebuild();
        }
    };

    // ── Tasks ───────────────────────────────────────────────────

    /// <summary>Where a task matched, best first: its title, its description, a subtask, a comment, a label.</summary>
    /// <summary>
    /// Where a task matched. The quote is made only for the rows shown: a word
    /// as common as "the" matches hundreds of tasks, and only four are listed.
    /// </summary>
    private sealed record TaskMatch(TodoItem Task, int Rank, string? Chip, Func<string?>? Quote, bool SnippetHasMatch)
    {
        public string? Snippet => Quote?.Invoke();
    }

    private List<TaskMatch> FindTasks(IReadOnlyList<string> words)
    {
        var found = new List<TaskMatch>();
        if (words.Count == 0) return found;

        foreach (var task in Tasks.Values)
        {
            if (Match(task, words) is { } match) found.Add(match);
        }
        return found.OrderBy(m => m.Rank)
                    .ThenBy(m => m.Task.Status == TodoStatus.Completed ? 1 : 0)
                    .ThenByDescending(m => m.Task.UpdatedAt)
                    .ToList();
    }

    private TaskMatch? Match(TodoItem task, IReadOnlyList<string> words)
    {
        if (QuickSearch.Has(task.Title, words))
            return new TaskMatch(task, 0, "in title", () => QuickSearch.FirstLine(task.Description), false);

        if (QuickSearch.Has(task.Description, words))
            return new TaskMatch(task, 1, "in description", () => QuickSearch.Quote(task.Description, words), true);

        foreach (var id in task.SubtaskIds)
        {
            if (!Tasks.TryGetValue(id, out var sub) || !QuickSearch.Has(sub.Title, words)) continue;
            var state = sub.Status == TodoStatus.Completed ? "done" : "not done";
            return new TaskMatch(task, 2, "in subtask", () => $"Subtask: {QuickSearch.Plain(sub.Title)} · {state}", true);
        }

        foreach (var comment in task.Comments)
        {
            if (comment.EncryptionIV != null || !QuickSearch.Has(comment.Text, words)) continue;
            var day = comment.CreatedAt.ToLocalTime().ToString("d MMM");
            var text = comment.Text;
            return new TaskMatch(task, 3, "in comment", () => $"Comment, {day}: {QuickSearch.Quote(text, words, lead: 24, length: 90)}", true);
        }

        var labels = string.Join(", ", task.Tags);
        if (QuickSearch.Has(labels, words))
            return new TaskMatch(task, 4, "in labels", () => $"Labels: {labels}", true);

        return null;
    }

    private int TaskList(List<QuickRow> rows, IReadOnlyList<string> words)
    {
        List<TaskMatch> found;
        if (words.Count == 0)
        {
            // Nothing typed: what was opened last, then whatever changed last.
            var opened = _main.RecentlyOpened.Where(o => o.Kind == MainViewModel.TaskKind).Select(o => o.Key).ToList();
            found = Tasks.Values
                         .OrderBy(t => opened.IndexOf(t.IdText) is var at && at >= 0 ? at : int.MaxValue)
                         .ThenBy(t => t.Status == TodoStatus.Completed ? 1 : 0)
                         .ThenByDescending(t => t.UpdatedAt)
                         .Select(t => new TaskMatch(t, 0, null, null, false))
                         .ToList();
        }
        else
        {
            found = FindTasks(words);
        }

        Group(rows, "TASKS", found.Count, found.Take(ListLimit).Select(m => TaskRow(m.Task, words, match: m)),
              found.Count > ListLimit && words.Count > 0 ? ShowAllTasks(found.Count) : null);
        return found.Count;
    }

    private QuickItem TaskRow(TodoItem task, IReadOnlyList<string> words, TaskMatch? match = null, bool recent = false)
    {
        var workspace = _main.WorkspaceName(task.WorkspaceId);
        return new QuickItem
        {
            Icon = Icon("IconTaskSquare"),
            Title = task.Title.Length > 0 ? task.Title : "Untitled",
            TitleWords = match?.Rank == 0 ? words : null,
            Chip = match?.Chip,
            Snippet = match?.Snippet,
            SnippetWords = match?.SnippetHasMatch == true ? words : null,
            Detail = recent ? $"{workspace} · {Palette.ColumnName(task.Status)}" : null,
            DetailDot = recent ? Palette.ColumnDot(task.Status) : null,
            Hint = recent ? "recently opened" : null,
            Dot = recent ? null : Palette.ColumnDot(task.Status),
            Place = recent ? null : workspace,
            Activate = _ =>
            {
                Close();
                _main.OpenTaskAnywhere(task);
            }
        };
    }

    // ── Notes ───────────────────────────────────────────────────

    /// <summary>Every note Quick Access knows of: Recent first, then each opened folder's, hidden ones left out.</summary>
    private List<string> KnownNotes()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        void Add(string path)
        {
            if (seen.Add(path)) notes.Add(path);
        }

        foreach (var recent in _main.RecentNotes) Add(recent.Path);
        foreach (var folder in _main.OpenedFolders)
        {
            if (folder.Tree is not { } tree) continue;
            var stack = new Stack<NoteFolderNode>();
            stack.Push(tree);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                foreach (var note in node.Notes)
                    if (!folder.Hidden.Contains(note)) Add(note);
                for (var i = node.Folders.Count - 1; i >= 0; i--)
                    if (!folder.Hidden.Contains(node.Folders[i].Path)) stack.Push(node.Folders[i]);
            }
        }
        return notes;
    }

    /// <summary>A note found, by its name or by a line in it.</summary>
    private sealed record NoteMatch(string Path, int Line, string? Text);

    private List<NoteMatch> FindNotesByName(IReadOnlyList<string> words)
        => words.Count == 0
            ? new List<NoteMatch>()
            : KnownNotes().Where(p => QuickSearch.Has(Path.GetFileName(p), words)).Select(p => new NoteMatch(p, 0, null)).ToList();

    /// <summary>
    /// Reads the notes whose names did not match, off the UI thread, and hands
    /// back the lines that do: <paramref name="apply"/> builds the new list.
    /// </summary>
    private void SearchNoteLines(IReadOnlyList<string> words, List<NoteMatch> byName,
                                 Func<List<NoteMatch>, List<QuickRow>> apply)
    {
        if (words.Count == 0) return;
        var named = byName.Select(n => n.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = KnownNotes().Where(p => !named.Contains(p)).ToList();
        if (candidates.Count == 0) return;

        var cancel = new CancellationTokenSource();
        _noteSearch = cancel;
        var dispatcher = Dispatcher.CurrentDispatcher;
        Task.Run(() =>
        {
            var found = new List<NoteMatch>();
            foreach (var path in candidates)
            {
                if (cancel.IsCancellationRequested) return;
                if (QuickSearch.LineWith(path, words) is { } line) found.Add(new NoteMatch(path, line.Number, line.Line));
                if (found.Count >= ListLimit) break;
            }
            if (found.Count == 0 || cancel.IsCancellationRequested) return;

            dispatcher.BeginInvoke(() =>
            {
                if (cancel.IsCancellationRequested || !IsOpen) return;
                var selectedAt = Selected == null ? -1 : Choices.ToList().IndexOf(Selected);
                ResultCount += found.Count;
                Show(Finish(apply(found), words));
                var choices = Choices.ToList();
                _quietly = true;
                Selected = choices.Count == 0 ? null : choices[Math.Clamp(selectedAt, 0, choices.Count - 1)];
                _quietly = false;
            });
        });
    }

    private int NoteList(List<QuickRow> rows, IReadOnlyList<string> words)
    {
        if (words.Count == 0)
        {
            var all = KnownNotes();
            Group(rows, "NOTES", all.Count, all.Take(ListLimit).Select(p => NoteRow(new NoteMatch(p, 0, null), words)), null);
            return all.Count;
        }

        var byName = FindNotesByName(words);
        Group(rows, "NOTES", byName.Count, byName.Take(ListLimit).Select(n => NoteRow(n, words)), null);
        SearchNoteLines(words, byName, found =>
        {
            var all = byName.Concat(found).ToList();
            var group = new List<QuickRow>();
            Group(group, "NOTES", all.Count, all.Take(ListLimit).Select(n => NoteRow(n, words)), null);
            return group;
        });
        return byName.Count;
    }

    private QuickItem NoteRow(NoteMatch match, IReadOnlyList<string> words) => NoteRow(match.Path, words, match: match);

    /// <summary>A folder's label short enough for the row: the folders nearest the note are the ones kept.</summary>
    private static string ShortFolder(string label, int most = 34)
    {
        if (label.Length <= most) return label;
        var parts = label.Split(Path.DirectorySeparatorChar);
        var kept = parts[^1];
        for (var i = parts.Length - 2; i >= 0 && kept.Length + parts[i].Length + 1 <= most - 2; i--) kept = parts[i] + Path.DirectorySeparatorChar + kept;
        return "…" + Path.DirectorySeparatorChar + kept;
    }

    private QuickItem NoteRow(string path, IReadOnlyList<string> words, bool recent = false, NoteMatch? match = null)
    {
        var folder = ShortFolder(_main.NoteFolderLabel(path));
        return new QuickItem
        {
            Icon = Icon("IconNoteSmall"),
            Title = Path.GetFileName(path),
            TitleWords = match is { Line: 0 } ? words : null,
            Chip = match is { Line: > 0 } ? $"line {match.Line}" : null,
            Snippet = match is { Line: > 0, Text: { } line } ? QuickSearch.Quote(line, words) : null,
            SnippetWords = words,
            Detail = recent ? folder : null,
            Hint = recent ? "recently opened" : folder,
            Activate = alternate =>
            {
                Close();
                if (alternate) _main.OpenNoteInReader(path);
                else _main.OpenNote(path);
            }
        };
    }

    // ── Settings ────────────────────────────────────────────────

    private sealed record SettingMatch(SettingEntry Entry, bool InName);

    private List<SettingMatch> FindSettings(IReadOnlyList<string> words)
    {
        var found = new List<SettingMatch>();
        foreach (var entry in SettingEntry.All)
        {
            if (words.Count == 0) found.Add(new SettingMatch(entry, false));
            else if (QuickSearch.Has(entry.Name + " " + entry.Area, words)) found.Add(new SettingMatch(entry, true));
            else if (QuickSearch.Has(entry.Description, words)) found.Add(new SettingMatch(entry, false));
        }
        return found.OrderBy(m => m.InName ? 0 : 1).ToList();
    }

    private int SettingList(List<QuickRow> rows, IReadOnlyList<string> words)
    {
        var found = FindSettings(words);
        Group(rows, "SETTINGS", found.Count, found.Select(s => SettingRow(s, words)), null);
        return found.Count;
    }

    private QuickItem SettingRow(SettingMatch match, IReadOnlyList<string> words)
    {
        var entry = match.Entry;
        var inDescription = words.Count > 0 && !match.InName;
        var now = entry.Now(_main);
        return new QuickItem
        {
            Icon = Icon("IconSettings"),
            Title = entry.Name,
            TitleWords = match.InName ? words : null,
            Chip = inDescription ? "in description" : null,
            Snippet = inDescription ? QuickSearch.Quote(entry.Description, words) : null,
            SnippetWords = words,
            Hint = now == null ? entry.Area : $"{entry.Area} · now {now}",
            Activate = _ =>
            {
                Close();
                _main.OpenSettingsAt(entry.Section);
            }
        };
    }

    // ── Commands ────────────────────────────────────────────────

    /// <summary>The commands-only list starts with these, before the ones used less.</summary>
    private static readonly string[] EverydayCommands =
    {
        "board.newtask", "note.new", "note.open", "note.folder", "app.save", "theme.toggle", "layout.switch",
        "window.sidebar", "nav.vault", "nav.trash", "capture.screenshot", "app.settings", "app.shortcuts"
    };

    private List<AppCommand> FindCommands(IReadOnlyList<string> words)
        => _main.Commands.Where(c => QuickSearch.Has(c.Name + " " + c.Category, words)).ToList();

    private int CommandList(List<QuickRow> rows, IReadOnlyList<string> words)
    {
        IEnumerable<AppCommand> commands;
        if (words.Count == 0)
        {
            // The ones run most first, then the everyday ones, then the rest as the catalogue lists them.
            var everyday = EverydayCommands.Select(_main.Command).OfType<AppCommand>();
            commands = _main.UsedCommands().Concat(everyday).Concat(_main.Commands).Distinct();
        }
        else
        {
            commands = FindCommands(words);
        }

        var list = commands.ToList();
        Group(rows, "COMMANDS", list.Count, list.Select(c => CommandRow(c, words, useHint: false, listed: true)), null);
        return list.Count;
    }

    /// <summary>
    /// A command's row. Just opened, the list shows its short name with what it
    /// will do ("Sun → Moon"); in the commands-only list, its group, and for one
    /// without keys a way to give it some.
    /// </summary>
    private QuickItem CommandRow(AppCommand command, IReadOnlyList<string>? words, bool useHint, bool listed = false)
    {
        var hint = useHint ? HintFor(command) : null;
        var row = listed ? new QuickCommandRow
        {
            Icon = command.Icon,
            Title = command.Name,
            TitleWords = words,
            Caps = command.Caps,
            Category = command.Category,
            CanAssign = !command.HasChord,
            Assign = () =>
            {
                Close();
                _main.OpenSettingsAt(SettingsSections.Shortcuts, command.Id);
            },
            Activate = alternate =>
            {
                Close();
                if (alternate) _main.OpenSettingsAt(SettingsSections.Shortcuts, command.Id);
                else _main.RunCommand(command.Id);
            }
        } : null;
        if (row != null) return row;

        return new QuickItem
        {
            Icon = command.Icon,
            Title = useHint ? command.Def.Name : command.Name,
            TitleWords = words,
            Hint = hint,
            Caps = command.Caps,
            NoShortcut = !command.HasChord && hint == null,
            Activate = alternate =>
            {
                Close();
                if (alternate) _main.OpenSettingsAt(SettingsSections.Shortcuts, command.Id);
                else _main.RunCommand(command.Id);
            }
        };
    }

    /// <summary>What a command will do from here, where that is worth saying.</summary>
    private string? HintFor(AppCommand command) => command.Id switch
    {
        "theme.toggle" => $"{App.CurrentTheme.Name} → {_main.ToggleThemeTarget().Name}",
        "layout.switch" => Controls.Layout.Current.IsFlat ? "Flat → Islands" : "Islands → Flat",
        "capture.screens" => _main.ScreenshotAllScreens ? "Every screen → Primary monitor" : "Primary monitor → Every screen",
        _ => null
    };

    // ── The strip along the bottom ─────────────────────────────

    private static readonly string[] None = Array.Empty<string>();
    private static readonly QuickHint MoveHint = new(new[] { "↑", "↓" }, None, "move");
    private static readonly QuickHint OpenHint = new(new[] { "Enter" }, None, "open");
    private static readonly QuickHint BackHint = new(new[] { "Backspace" }, None, "back");
    private static readonly QuickHint WindowHint = new(new[] { "Ctrl", "Enter" }, None, "open in a new window");

    private static readonly QuickHint[] StartingHints =
        { MoveHint, OpenHint, new(new[] { "Tab" }, None, "next mode"), new(None, new[] { "#", ">", "n:", "?" }, "one kind only") };
    private static readonly QuickHint[] ResultHints = { MoveHint, OpenHint, WindowHint, new(None, new[] { "#" }, "tasks only") };
    private static readonly QuickHint[] CommandHints =
        { MoveHint, new(new[] { "Enter" }, None, "run"), new(new[] { "Ctrl", "Enter" }, None, "set shortcut"), BackHint };
    private static readonly QuickHint[] NoteHints = { MoveHint, OpenHint, WindowHint, BackHint };
    private static readonly QuickHint[] OneKindHints = { MoveHint, OpenHint, BackHint };

    /// <summary>The keys that work in the list as it is now. "Esc close" ends every strip, on its right.</summary>
    private IReadOnlyList<QuickHint> HintsFor() => Mode switch
    {
        QuickMode.Everything when !HasQuery => StartingHints,
        QuickMode.Everything => ResultHints,
        QuickMode.Commands => CommandHints,
        QuickMode.Notes => NoteHints,
        _ => OneKindHints
    };

    private static Geometry? Icon(string key) => System.Windows.Application.Current.TryFindResource(key) as Geometry;
}
