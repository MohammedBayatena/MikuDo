namespace MikuDo.Models;

/// <summary>
/// Board column of a todo. <see cref="Active"/> is the "To do" column and stays
/// value 0 so existing records keep their column.
/// </summary>
public enum TodoStatus
{
    Active = 0,
    Completed = 1,
    Trashed = 2,
    Doing = 3
}
