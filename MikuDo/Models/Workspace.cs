using LiteDB;

namespace MikuDo.Models;

public class Workspace
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();

    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "\uE8F1"; // Default folder icon
    public string Color { get; set; } = "#cba6f7"; // Default accent
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
