using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

namespace MikuDo.ViewModels;

/// <summary>
/// How Quick Access matches and quotes text. A query matches a piece of text
/// when every word of it appears there, in any order and any case.
/// </summary>
public static partial class QuickSearch
{
    public static IReadOnlyList<string> Words(string query)
        => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static bool Has(string? text, IReadOnlyList<string> words)
        => words.Count > 0 && text != null && words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Where the first word that appears begins, or -1.</summary>
    private static int FirstHit(string text, IReadOnlyList<string> words)
    {
        var first = -1;
        foreach (var word in words)
        {
            var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && (first < 0 || at < first)) first = at;
        }
        return first;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    [GeneratedRegex(@"(\*\*|__|`|^#+\s*)", RegexOptions.Multiline)]
    private static partial Regex Marks();

    /// <summary>Markdown text as one plain line: emphasis marks gone, every run of spaces and line breaks one space.</summary>
    public static string Plain(string text) => Space().Replace(Marks().Replace(text, string.Empty), " ").Trim();

    /// <summary>
    /// <paramref name="text"/> as one line: whole when it is short, or else a
    /// piece around the first match, cut at word breaks and marked "…" where
    /// it was cut, so the match shows near the start of the line.
    /// </summary>
    public static string Quote(string text, IReadOnlyList<string> words, int lead = 34, int length = 110)
    {
        var plain = Plain(text);
        if (plain.Length <= length) return plain;
        var hit = FirstHit(plain, words);
        if (hit < 0) hit = 0;

        var start = Math.Max(0, hit - lead);
        if (start > 0)
        {
            var space = plain.IndexOf(' ', start);
            start = space >= 0 && space < hit ? space + 1 : start;
        }
        var end = Math.Min(plain.Length, start + length);
        if (end < plain.Length)
        {
            var space = plain.LastIndexOf(' ', end);
            if (space > hit) end = space;
        }

        var quote = plain[start..end].Trim();
        return (start > 0 ? "…" : string.Empty) + quote + (end < plain.Length ? "…" : string.Empty);
    }

    /// <summary>The first line of a description, for a task found by its title.</summary>
    public static string? FirstLine(string description)
    {
        foreach (var line in description.Split('\n'))
        {
            var plain = Plain(line);
            if (plain.Length > 0) return plain.Length > 140 ? plain[..140] + "…" : plain;
        }
        return null;
    }

    // ── Notes' text ─────────────────────────────────────────────

    /// <summary>Notes are read this far and no further: past it a file is not a note anyone writes by hand.</summary>
    private const long MaxNoteBytes = 1024 * 1024;

    private sealed record NoteText(DateTime Written, long Length, string[] Lines);

    /// <summary>Each note's lines as last read, kept while the file is unchanged.</summary>
    private static readonly ConcurrentDictionary<string, NoteText> Notes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The lines of a note, read again only when the file has changed; null when it cannot be read.</summary>
    public static string[]? NoteLines(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxNoteBytes) return null;
            if (Notes.TryGetValue(path, out var known) && known.Written == info.LastWriteTimeUtc && known.Length == info.Length)
                return known.Lines;

            var lines = File.ReadAllLines(path);
            Notes[path] = new NoteText(info.LastWriteTimeUtc, info.Length, lines);
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The first line of a note holding every word, as its 1-based number and text; null for none.</summary>
    public static (int Number, string Line)? LineWith(string path, IReadOnlyList<string> words)
    {
        var lines = NoteLines(path);
        if (lines == null) return null;
        for (var i = 0; i < lines.Length; i++)
            if (Has(lines[i], words)) return (i + 1, lines[i]);
        return null;
    }
}
