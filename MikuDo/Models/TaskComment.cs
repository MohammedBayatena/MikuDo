namespace MikuDo.Models;

/// <summary>A note left on a task. On a vault task the text is stored encrypted.</summary>
public class TaskComment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }

    /// <summary>Set on a vault task's comment: <see cref="Text"/> is ciphertext under this IV.</summary>
    public string? EncryptionIV { get; set; }

    public TaskComment Copy() => new()
    {
        Id = Id,
        Text = Text,
        CreatedAt = CreatedAt,
        EditedAt = EditedAt,
        EncryptionIV = EncryptionIV
    };
}
