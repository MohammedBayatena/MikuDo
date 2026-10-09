using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;

namespace MikuDo.ViewModels;

/// <summary>A coloured label pill.</summary>
public class TagChip
{
    public string Label { get; }
    public Brush Background { get; }
    public Brush Foreground { get; }

    public TagChip(string label)
    {
        Label = label;
        var colors = Palette.Label(label);
        Background = colors.Background;
        Foreground = colors.Foreground;
    }
}

/// <summary>
/// A subtask shown under its parent while the board groups by parent. One row
/// per place the task appears: a task linked under two parents gets two.
/// </summary>
public partial class SubRow : ObservableObject
{
    private readonly TaskCardViewModel _owner;

    public TaskCardViewModel Card { get; }

    /// <summary>The ids from the top card down to this one, which names this place in the tree.</summary>
    public string Path { get; }

    public bool IsExpanded { get; }
    public bool HasChildren => Card.GroupChildren.Count > 0;
    public Thickness Indent { get; }

    public SubRow(TaskCardViewModel owner, TaskCardViewModel card, int depth, string path, bool isExpanded)
    {
        _owner = owner;
        Card = card;
        Path = path;
        IsExpanded = isExpanded;
        Indent = new Thickness(depth * 14, 0, 0, 0);
    }

    [RelayCommand]
    private void Toggle() => _owner.ToggleSubRow(Path);
}

/// <summary>One task as it appears on the board, in any of the three views.</summary>
public partial class TaskCardViewModel : ObservableObject
{
    [ObservableProperty] private bool _isMenuOpen;
    [ObservableProperty] private bool _isDragging;
    [ObservableProperty] private bool _isSelected;

    /// <summary>Drop marker: the dragged card would land above this one.</summary>
    [ObservableProperty] private bool _dropBefore;

    /// <summary>Drop marker: the dragged card would land below this one.</summary>
    [ObservableProperty] private bool _dropAfter;

    /// <summary>Drop marker: the dragged card would become this one's subtask.</summary>
    [ObservableProperty] private bool _dropInto;

    /// <summary>A card is dragged over this one and could be linked beneath it: the link strip shows beside its grip.</summary>
    [ObservableProperty] private bool _linkZoneShown;

    /// <summary>The title is being edited on the card itself.</summary>
    [ObservableProperty] private bool _isRenaming;

    /// <summary>The title as typed so far while renaming.</summary>
    [ObservableProperty] private string _renameText = string.Empty;

    public TodoItem Item { get; private set; }
    public BoardViewModel Board { get; }

    public string Title => Item.Title;
    public string Description { get; private set; } = string.Empty;
    public List<TagChip> Tags { get; private set; } = EmptyTags;

    public TodoStatus Status => Item.Status;

    public TodoPriority Priority => Item.Priority;
    public bool HasPriority => Item.Priority != TodoPriority.None;
    public string PriorityName => PriorityText(Item.Priority);
    public Brush PriorityForeground => Palette.Priority(Item.Priority).Foreground;
    public Brush PriorityBackground => Palette.Priority(Item.Priority).Background;

    public static string PriorityText(TodoPriority priority)
        => priority == TodoPriority.None ? "No priority" : priority.ToString();
    public string StatusName => Palette.ColumnName(Item.Status);
    public Brush StatusForeground => Palette.StatusForeground(Item.Status);

    public bool IsDone => Item.Status == TodoStatus.Completed;

    /// <summary>False in the last column: there is nowhere further right to go.</summary>
    public bool CanAdvance => Item.Status != Palette.BoardColumns[^1];

    /// <summary>False in the first column: there is nowhere further left to go.</summary>
    public bool CanRetreat => Item.Status != Palette.BoardColumns[0];

    public int SubtaskCount { get; private set; }
    public int SubtaskDone { get; private set; }
    public string ProgressLabel => SubtaskCount > 0 ? $"{SubtaskDone}/{SubtaskCount}" : "—";

    /// <summary>"2/3", or nothing at all when the task has no subtasks: for a row, where a dash would only be noise.</summary>
    public string SubtaskFraction => SubtaskCount > 0 ? ProgressLabel : string.Empty;
    public bool HasOpenSubtasks => SubtaskDone < SubtaskCount;

    public int AttachmentCount => Item.AttachedImages.Count + Item.Files.Count;
    public int CommentCount => Item.Comments.Count;

    /// <summary>"3 subtasks" / "2 attachments" — the small line under a card.</summary>
    public string SubtitleLabel =>
        SubtaskCount > 0 ? Plural(SubtaskCount, "subtask")
        : AttachmentCount > 0 ? Plural(AttachmentCount, "attachment")
        : "No subtasks";

    /// <summary>The corner label counts attachments, there being no subtasks to count: it takes the paperclip.</summary>
    public bool SubtitleIsAttachments => SubtaskCount == 0 && AttachmentCount > 0;

    // The two columns this card is not in, in board order, for its ⋯ menu.
    public string OtherColumnA => Item.Status == TodoStatus.Active ? "Doing" : "To do";
    public string OtherColumnB => Item.Status == TodoStatus.Completed ? "Doing" : "Done";
    public string MoveToALabel => $"Move to {OtherColumnA}";
    public string MoveToBLabel => $"Move to {OtherColumnB}";

    public TaskCardViewModel(BoardViewModel board, TodoItem item,
                            IReadOnlyDictionary<string, TodoItem>? lookup = null)
    {
        Board = board;
        Item = item;
        Derive(lookup);
    }

    /// <summary>
    /// Points this row at the freshest copy of its task.
    /// </summary>
    /// <remarks>
    /// A reload reads new <see cref="TodoItem"/> instances out of the database,
    /// so a row that kept the instance it was built with would hand stale
    /// objects to whatever acted on it. Re-deriving the display text is the
    /// expensive half and only earns its cost when the task actually changed.
    /// Every write moves <c>UpdatedAt</c>, so that settles it; status and
    /// priority are compared as well because they change what the row shows
    /// even without any other edit. The subtask count is compared too: ticking
    /// a subtask writes the subtask, not this task, yet changes this row's
    /// "1/3".
    /// </remarks>
    public void Adopt(TodoItem item, IReadOnlyDictionary<string, TodoItem>? lookup)
    {
        var progress = SubtaskLinks.Progress(item, lookup);
        var unchanged = Item.UpdatedAt == item.UpdatedAt && Item.Status == item.Status
                        && Item.Priority == item.Priority
                        && progress == (SubtaskDone, SubtaskCount);
        Item = item;
        if (unchanged) return;

        Derive(lookup, progress);

        // Almost every property here is computed from the task, so the bindings
        // are told to re-read the lot rather than named one by one.
        OnPropertyChanged(string.Empty);
    }

    private void Derive(IReadOnlyDictionary<string, TodoItem>? lookup, (int Done, int Total)? progress = null)
    {
        Description = Summarize(Item.Description);
        Tags = Item.Tags.Count == 0
            ? EmptyTags
            : Item.Tags.Select(t => new TagChip(t)).ToList();

        var (done, total) = progress ?? SubtaskLinks.Progress(Item, lookup);
        SubtaskDone = done;
        SubtaskCount = total;
    }

    /// <summary>Shared so the common untagged card allocates no list of its own.</summary>
    private static readonly List<TagChip> EmptyTags = new();

    // ── Grouped under this card ─────────────────────────────────

    private static readonly IReadOnlyList<TaskCardViewModel> NoChildren = Array.Empty<TaskCardViewModel>();

    /// <summary>Which nested rows are open, by their path, so a rebuild keeps them open.</summary>
    private readonly HashSet<string> _openPaths = new(StringComparer.Ordinal);

    /// <summary>This task's subtasks on the board, while the board groups by parent; empty otherwise.</summary>
    public IReadOnlyList<TaskCardViewModel> GroupChildren { get; private set; } = NoChildren;

    public bool HasGroupChildren => GroupChildren.Count > 0;
    /// <summary>"Show 3 subtasks" while folded, "Hide subtasks" while open.</summary>
    public string GroupLabel => IsGroupExpanded ? "Hide subtasks"
        : GroupChildren.Count == 1 ? "Show 1 subtask" : $"Show {GroupChildren.Count} subtasks";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupLabel))]
    private bool _isGroupExpanded;

    /// <summary>The rows under this card while it is expanded, deeper levels included where open.</summary>
    public ObservableCollection<SubRow> SubRows { get; } = new();

    partial void OnIsGroupExpandedChanged(bool value) => RebuildSubRows();

    [RelayCommand]
    private void ToggleGroup() => IsGroupExpanded = !IsGroupExpanded;

    public void SetGroupChildren(IReadOnlyList<TaskCardViewModel> children)
    {
        if (children.Count == 0 && GroupChildren.Count == 0) return;

        GroupChildren = children.Count == 0 ? NoChildren : children;
        OnPropertyChanged(nameof(GroupChildren));
        OnPropertyChanged(nameof(HasGroupChildren));
        OnPropertyChanged(nameof(GroupLabel));
    }

    public void ToggleSubRow(string path)
    {
        if (!_openPaths.Remove(path)) _openPaths.Add(path);
        RebuildSubRows();
    }

    /// <summary>
    /// Lays the subtree out as a flat list of indented rows. Links can form a
    /// loop, so a task already on the path down to a row is not shown again
    /// beneath it.
    /// </summary>
    public void RebuildSubRows()
    {
        if (!IsGroupExpanded || GroupChildren.Count == 0)
        {
            if (SubRows.Count > 0) SubRows.Clear();
            return;
        }

        var rows = new List<SubRow>();
        var onPath = new HashSet<string>(StringComparer.Ordinal) { Item.IdText };

        void Walk(TaskCardViewModel parent, int depth, string path)
        {
            foreach (var child in parent.GroupChildren)
            {
                var id = child.Item.IdText;
                if (!onPath.Add(id)) continue;

                var childPath = path + "/" + id;
                var open = _openPaths.Contains(childPath);
                rows.Add(new SubRow(this, child, depth, childPath, open));
                if (open) Walk(child, depth + 1, childPath);
                onPath.Remove(id);
            }
        }

        Walk(this, 0, Item.IdText);

        SubRows.Clear();
        foreach (var row in rows) SubRows.Add(row);
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    private static readonly Regex MediaRe = new(@"!?\[[^\]]*\]\((?:img|voice)://\d+\)", RegexOptions.Compiled);
    private static readonly Regex MarkupRe = new(@"[#*`>_]|^\s*[-+]\s+", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>First meaningful line of the description, stripped of markdown.</summary>
    private static string Summarize(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        var text = MediaRe.Replace(description, " ");
        foreach (var raw in text.Split('\n'))
        {
            var line = MarkupRe.Replace(raw, "").Trim();
            if (line.Length == 0) continue;
            return line.Length <= 110 ? line : line[..110].TrimEnd() + "…";
        }
        return string.Empty;
    }
}
