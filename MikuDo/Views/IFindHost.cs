namespace MikuDo.Views;

/// <summary>
/// A page that can be searched in: a note or a task, with the find bar, or a
/// board, with its own search box. Quick Access offers it as "This note",
/// "This task" or "This board", and Ctrl+F opens it.
/// </summary>
public interface IFindHost
{
    /// <summary>What the page is, as Quick Access names it: "note", "task" or "board".</summary>
    string FindNoun { get; }

    /// <summary>
    /// Opens the page's search with <paramref name="text"/> in it when given.
    /// False when the page has nothing to search just now, so the key goes on.
    /// </summary>
    bool OpenFind(string? text = null);

    /// <summary>The next match (+1) or the one before (-1). False when the page has no matches to step through.</summary>
    bool FindStep(int step);
}
