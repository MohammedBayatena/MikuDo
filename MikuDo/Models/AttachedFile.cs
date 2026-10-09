namespace MikuDo.Models;

/// <summary>A non-image, non-audio attachment kept in LiteDB file storage.</summary>
public class AttachedFile
{
    /// <summary>LiteDB FileStorage id.</summary>
    public string StorageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }

    public string SizeLabel => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:0.#} KB",
        _ => $"{Size / (1024.0 * 1024.0):0.#} MB"
    };
}
