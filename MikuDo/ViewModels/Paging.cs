namespace MikuDo.ViewModels;

/// <summary>
/// Long lists show a page of rows and the next page as the user scrolls to the
/// foot, where a row says how many are left and loads them once it is seen.
/// </summary>
/// <remarks>
/// Every page is cut from one order that ties nothing (see
/// <see cref="Models.TaskOrder"/>), so the pages join up exactly: no task twice,
/// none skipped.
/// </remarks>
public static class Paging
{
    public const int PageSize = 50;

    /// <summary>"Show 50 more · 70 left", or "Show the last 12".</summary>
    public static string MoreLabel(int remaining)
        => remaining <= PageSize ? $"Show the last {remaining}" : $"Show {PageSize} more · {remaining} left";
}
