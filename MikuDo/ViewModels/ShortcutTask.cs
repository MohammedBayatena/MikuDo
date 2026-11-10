using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>
/// A task under SHORTCUTS in the sidebar: the one opened last, one of the
/// others opened lately, or one pinned there; and what its preview tells.
/// </summary>
public partial class ShortcutTask : ObservableObject
{
    private const int ExcerptLength = 150;

    /// <summary>The task's page is the one on screen.</summary>
    [ObservableProperty] private bool _isActive;

    public TodoItem Item { get; }
    public bool IsPinned { get; }

    /// <summary>What a shortcut is, for the colours of its square: "task" here, "note" for a note.</summary>
    public string Kind => "task";
    public string Id => Item.IdText;
    public string Title => string.IsNullOrWhiteSpace(Item.Title) ? "Untitled task" : Item.Title.Trim();
    public bool IsDone => Item.Status == TodoStatus.Completed;

    /// <summary>The workspace and the list the task is in.</summary>
    public string Where { get; }

    public string PriorityText => Item.Priority == TodoPriority.None ? string.Empty : $"{Item.Priority} priority";
    public bool HasPriority => Item.Priority != TodoPriority.None;
    public System.Windows.Media.Brush PriorityBrush => Palette.Priority(Item.Priority).Foreground;

    /// <summary>The start of the description as plain text, its pictures and voice memos left out.</summary>
    public string Excerpt { get; }

    public bool HasExcerpt => Excerpt.Length > 0;

    /// <summary>When the task was made, and when its words were last edited.</summary>
    public string Dates => TaskDates.Label(Item);

    /// <summary>When the task was made, as briefly as a card says it: "14:32", "Mon", "3 Oct".</summary>
    public string CreatedShort => When.Brief(Item.CreatedAt);

    public bool HasEdited => TaskDates.WasEdited(Item);

    /// <summary>When its words were last edited, as briefly; empty if they never were.</summary>
    public string EditedShort => HasEdited ? When.Brief(Item.EditedAt!.Value) : string.Empty;

    /// <summary>The whole title, then where the task is: the tooltip of a row in the list.</summary>
    public string Tip => $"{Title}\n{Where}";

    public ShortcutTask(TodoItem item, bool isPinned, string workspace)
    {
        Item = item;
        IsPinned = isPinned;
        Where = $"{workspace} · {Palette.ColumnName(item.Status)}";
        var words = Rules().Replace(ListMarks().Replace(Media().Replace(item.Description ?? string.Empty, " "), string.Empty), string.Empty);
        Excerpt = Shorten(QuickSearch.Plain(words));
    }

    private static string Shorten(string text)
    {
        if (text.Length <= ExcerptLength) return text;
        var cut = text.LastIndexOf(' ', ExcerptLength);
        return text[..(cut > ExcerptLength / 2 ? cut : ExcerptLength)].TrimEnd() + "…";
    }

    [GeneratedRegex(@"!?\[[^\]]*\]\((?:img|voice)://[^)]*\)")]
    private static partial Regex Media();

    /// <summary>A list item's mark at the start of a line, and its checkbox if it has one.</summary>
    [GeneratedRegex(@"^[ \t]*(?:[-*+]|\d+[.)])[ \t]+(?:\[[ xX]\][ \t]+)?", RegexOptions.Multiline)]
    private static partial Regex ListMarks();

    /// <summary>A run of three or more dashes, stars or underscores starting a line: a rule, not words.</summary>
    [GeneratedRegex(@"^[ \t]*([-*_])\1{2,}[ \t]*", RegexOptions.Multiline)]
    private static partial Regex Rules();

    /// <summary>What a row or square shows, so an unchanged one can stay as it is.</summary>
    internal string Look => $"{Id}|{IsPinned}|{Title}|{Item.Status}|{Item.WorkspaceId}|{Item.Priority}|{Item.EditedAt?.Ticks}";
}
