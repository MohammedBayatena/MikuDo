using LiteDB;

namespace MikuDo.Models;

public class TodoItem
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public TodoPriority Priority { get; set; } = TodoPriority.None;
    public TodoStatus Status { get; set; } = TodoStatus.Active;

    public string? WorkspaceId { get; set; } // null = the default workspace

    public bool IsVault { get; set; }
    public string? EncryptionIV { get; set; }
    public string? EncryptionTitleIV { get; set; }

    public List<string> AttachedImages { get; set; } = new();
    public List<AttachedFile> Files { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    /// <summary>Ids of the tasks that make up this one. A subtask is a real task.</summary>
    public List<string> SubtaskIds { get; set; } = new();

    /// <summary>
    /// Free-text subtasks, kept only so the one-time migration in
    /// <see cref="Services.DatabaseService"/> can turn them into real tasks.
    /// Nothing else reads it.
    /// </summary>
    [BsonField("Subtasks")]
    public List<Subtask> LegacyTextSubtasks { get; set; } = new();

    /// <summary>Notes left on the task, oldest first.</summary>
    public List<TaskComment> Comments { get; set; } = new();

    /// <summary>The Markdown note this task was made from, if it was; its page links back to it.</summary>
    public string? NotePath { get; set; }

    /// <summary>Position within its board column. Lower sorts first.</summary>
    public double SortOrder { get; set; }

    /// <summary>
    /// The user put this card where it is, so <see cref="SortOrder"/> is theirs
    /// to keep. An unplaced card has a <see cref="SortOrder"/> too, but only as
    /// a slot the app handed out, and it sorts by date and priority instead.
    /// </summary>
    public bool IsPlaced { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? TrashedAt { get; set; }

    /// <summary>Which column to put the task back in when it is restored.</summary>
    public TodoStatus? StatusBeforeTrash { get; set; }

    /// <summary>
    /// A detached copy, lists included, for undo to write back later. It keeps
    /// the same <see cref="Id"/>, so storing it replaces the original record.
    /// </summary>
    public TodoItem Snapshot() => new()
    {
        Id = Id,
        Title = Title,
        Description = Description,
        Priority = Priority,
        Status = Status,
        WorkspaceId = WorkspaceId,
        IsVault = IsVault,
        EncryptionIV = EncryptionIV,
        EncryptionTitleIV = EncryptionTitleIV,
        AttachedImages = new List<string>(AttachedImages),
        Files = new List<AttachedFile>(Files),
        Tags = new List<string>(Tags),
        SubtaskIds = new List<string>(SubtaskIds),
        LegacyTextSubtasks = new List<Subtask>(LegacyTextSubtasks),
        Comments = Comments.Select(c => c.Copy()).ToList(),
        NotePath = NotePath,
        SortOrder = SortOrder,
        IsPlaced = IsPlaced,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        CompletedAt = CompletedAt,
        TrashedAt = TrashedAt,
        StatusBeforeTrash = StatusBeforeTrash
    };

    /// <summary>
    /// LiteDB writes whatever the stored document holds over the initialisers, so a
    /// record saved before a field existed comes back with it null. Everything that
    /// reads a todo goes through here first.
    /// </summary>
    public TodoItem Normalize()
    {
        Title ??= string.Empty;
        Description ??= string.Empty;
        AttachedImages ??= new List<string>();
        Files ??= new List<AttachedFile>();
        Tags ??= new List<string>();
        SubtaskIds ??= new List<string>();
        LegacyTextSubtasks ??= new List<Subtask>();
        Comments ??= new List<TaskComment>();

        AttachedImages.RemoveAll(string.IsNullOrWhiteSpace);
        Tags.RemoveAll(string.IsNullOrWhiteSpace);
        SubtaskIds.RemoveAll(string.IsNullOrWhiteSpace);
        Files.RemoveAll(f => f == null || string.IsNullOrEmpty(f.StorageId));
        LegacyTextSubtasks.RemoveAll(s => s == null);
        Comments.RemoveAll(c => c == null);

        foreach (var file in Files) file.Name ??= string.Empty;

        return this;
    }

    [BsonIgnore]
    public string IdText => Id.ToString();
}
