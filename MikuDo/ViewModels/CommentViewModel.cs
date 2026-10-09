using CommunityToolkit.Mvvm.ComponentModel;
using MikuDo.Models;

namespace MikuDo.ViewModels;

/// <summary>One comment on the task page, with its in-place edit.</summary>
public partial class CommentViewModel : ObservableObject
{
    public string Id { get; }
    public DateTime CreatedAt { get; }

    [ObservableProperty] private string _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhenLabel))]
    private DateTime? _editedAt;

    [ObservableProperty] private bool _isEditing;

    /// <summary>The text being edited, kept apart so Cancel leaves the comment as it was.</summary>
    [ObservableProperty] private string _draft = string.Empty;

    /// <summary>"Today 14:32", "Yesterday 09:10", "3 Oct, 14:32"; "· edited" once changed.</summary>
    public string WhenLabel => When(CreatedAt) + (EditedAt != null ? " · edited" : "");

    /// <summary>The full date and time, for the tooltip.</summary>
    public string FullDate => CreatedAt.ToLocalTime().ToString("f");

    /// <param name="text">The readable text: on a vault task, already decrypted.</param>
    public CommentViewModel(TaskComment comment, string text)
    {
        Id = comment.Id;
        CreatedAt = comment.CreatedAt;
        _text = text;
        _editedAt = comment.EditedAt;
    }

    /// <summary>The comment as plain text, for storing.</summary>
    public TaskComment ToModel() => new()
    {
        Id = Id,
        Text = Text,
        CreatedAt = CreatedAt,
        EditedAt = EditedAt
    };

    private static string When(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var time = local.ToString("t");

        if (local.Date == DateTime.Today) return $"Today {time}";
        if (local.Date == DateTime.Today.AddDays(-1)) return $"Yesterday {time}";
        return local.Year == DateTime.Today.Year
            ? $"{local:d MMM}, {time}"
            : local.ToString("d MMM yyyy");
    }
}
