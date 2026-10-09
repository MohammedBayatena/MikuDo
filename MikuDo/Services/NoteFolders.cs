using System.IO;
using System.Runtime.InteropServices;

namespace MikuDo.Services;

/// <summary>A folder opened under Notes, or one inside it, with the notes it holds.</summary>
public sealed class NoteFolderNode
{
    public string Path { get; init; } = string.Empty;
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Path;
    public List<NoteFolderNode> Folders { get; } = new();
    public List<string> Notes { get; } = new();

    /// <summary>Notes in this folder and every folder below it.</summary>
    public int Count { get; set; }
}

/// <summary>Finds the Markdown notes in a folder and the folders under it.</summary>
public static class NoteFolders
{
    /// <summary>Folders that hold tools' output rather than notes, never looked in.</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "packages", "__pycache__", "venv"
    };

    /// <summary>Past this many notes the rest are left out, so a huge folder cannot stall the sidebar.</summary>
    public const int MaxNotes = 5000;

    private const int MaxDepth = 12;

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
    };

    /// <summary>
    /// The notes under <paramref name="root"/>, keeping only the folders that
    /// hold some; null when the folder is gone. Runs off the UI thread.
    /// </summary>
    public static NoteFolderNode? Scan(string root)
    {
        if (!Directory.Exists(root)) return null;
        var budget = MaxNotes;
        return Walk(root, 0, ref budget);
    }

    private static NoteFolderNode Walk(string path, int depth, ref int budget)
    {
        var node = new NoteFolderNode { Path = path };
        try
        {
            if (depth < MaxDepth)
            {
                var folders = Directory.EnumerateDirectories(path, "*", Options).ToList();
                folders.Sort(Natural);
                foreach (var folder in folders)
                {
                    if (budget <= 0) break;
                    if (IsSkipped(System.IO.Path.GetFileName(folder))) continue;
                    var child = Walk(folder, depth + 1, ref budget);
                    if (child.Count > 0) node.Folders.Add(child);
                }
            }

            var notes = Directory.EnumerateFiles(path, "*", Options).Where(NoteFiles.IsNote).ToList();
            notes.Sort(Natural);
            foreach (var note in notes)
            {
                if (budget-- <= 0) break;
                node.Notes.Add(note);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read shows whatever was read before it failed.
        }

        node.Count = node.Notes.Count + node.Folders.Sum(f => f.Count);
        return node;
    }

    /// <summary>A folder never looked in: hidden by its dot, or full of tools' output.</summary>
    public static bool IsSkipped(string name) => name.StartsWith('.') || Skipped.Contains(name);

    /// <summary>
    /// True when <paramref name="path"/> lies inside a skipped folder under
    /// <paramref name="root"/>, where no change can alter the tree.
    /// </summary>
    public static bool InSkippedFolder(string root, string path)
    {
        var relative = System.IO.Path.GetRelativePath(root, path);
        var parts = relative.Split(System.IO.Path.DirectorySeparatorChar);
        for (var i = 0; i < parts.Length - 1; i++)
            if (IsSkipped(parts[i])) return true;
        return false;
    }

    /// <summary>Explorer's own order: "v2.10" after "v2.9", not before it.</summary>
    private static int Natural(string a, string b)
        => StrCmpLogicalW(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b));

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);
}
