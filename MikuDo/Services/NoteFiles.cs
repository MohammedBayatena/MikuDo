using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MikuDo.Services;

/// <summary>A note's text as read from disk, and how to write it back the same way.</summary>
public sealed record NoteText(string Text, string LineEnding, Encoding Encoding);

/// <summary>A heading in a note, for the outline beside the preview.</summary>
public sealed record NoteHeading(int Level, string Text, int Line);

/// <summary>
/// A line of a note that can become a task: a checklist item, or under one,
/// any list item indented beneath it, which becomes its subtask.
/// </summary>
public sealed class NoteItem
{
    public string Text { get; init; } = string.Empty;
    public bool Done { get; init; }
    public int Line { get; init; }

    /// <summary>The heading the item sits under, if any.</summary>
    public string? Heading { get; init; }

    public List<NoteItem> Children { get; } = new();
}

/// <summary>Reading, writing and taking apart Markdown notes on disk.</summary>
public static class NoteFiles
{
    public static readonly string[] Extensions = { ".md", ".markdown", ".mdown", ".mkd" };

    public static bool IsNote(string? path)
        => !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The file's text, with the encoding it was saved in (its byte order mark
    /// kept or left off as found) and the line break it mostly uses.
    /// </summary>
    public static NoteText Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding encoding;
        int skip;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            (encoding, skip) = (new UTF8Encoding(true), 3);
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            (encoding, skip) = (new UnicodeEncoding(false, true), 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            (encoding, skip) = (new UnicodeEncoding(true, true), 2);
        else
            (encoding, skip) = (new UTF8Encoding(false), 0);

        var text = encoding.GetString(bytes, skip, bytes.Length - skip);
        var crlf = Regex.Matches(text, "\r\n").Count;
        var lf = text.Count(c => c == '\n') - crlf;
        return new NoteText(text, crlf > lf ? "\r\n" : "\n", encoding);
    }

    /// <summary>The text with every line break made <paramref name="lineEnding"/>.</summary>
    public static string WithLineEndings(string text, string lineEnding)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", lineEnding);

    /// <summary>
    /// Writes the note in its own encoding and line breaks. It is written beside
    /// the file and swapped in, so a failed write never leaves half a note.
    /// </summary>
    public static void Write(string path, string text, NoteText format)
    {
        var temp = path + ".mikudo-saving";
        File.WriteAllText(temp, WithLineEndings(text, format.LineEnding), format.Encoding);
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }

    // ── Taking a note apart ─────────────────────────────────────

    private static readonly Regex HeadingLine = new(@"^(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex ChecklistLine = new(@"^(\s*)(?:[-*+]|\d+[.)])\s+\[( |x|X)\]\s+(.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex ListLine = new(@"^(\s*)(?:[-*+]|\d+[.)])\s+(?:\[( |x|X)\]\s+)?(.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex Fence = new(@"^\s*(```|~~~)", RegexOptions.Compiled);

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

    /// <summary>The note's headings in order, leaving out anything inside a code block.</summary>
    public static List<NoteHeading> Headings(string text)
    {
        var headings = new List<NoteHeading>();
        var inCode = false;
        var lines = Lines(text);
        for (var i = 0; i < lines.Length; i++)
        {
            if (Fence.IsMatch(lines[i])) { inCode = !inCode; continue; }
            if (inCode) continue;
            var m = HeadingLine.Match(lines[i]);
            if (m.Success) headings.Add(new NoteHeading(m.Groups[1].Length, Inline(m.Groups[2].Value), i));
        }
        return headings;
    }

    /// <summary>
    /// The note's checklist items, each with whatever list items are indented
    /// beneath it as its subtasks. Code blocks are skipped.
    /// </summary>
    public static List<NoteItem> Checklist(string text)
    {
        var items = new List<NoteItem>();
        var inCode = false;
        string? heading = null;
        (NoteItem Item, int Indent)? parent = null;

        var lines = Lines(text);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (Fence.IsMatch(line)) { inCode = !inCode; continue; }
            if (inCode) continue;

            var h = HeadingLine.Match(line);
            if (h.Success)
            {
                heading = Inline(h.Groups[2].Value);
                parent = null;
                continue;
            }

            var list = ListLine.Match(line);
            if (!list.Success)
            {
                if (line.Trim().Length > 0 && !char.IsWhiteSpace(line[0])) parent = null;
                continue;
            }

            var indent = Indent(list.Groups[1].Value);
            var done = list.Groups[2].Success && list.Groups[2].Value is "x" or "X";

            if (parent is { } p && indent > p.Indent)
            {
                p.Item.Children.Add(new NoteItem { Text = Inline(list.Groups[3].Value), Done = done, Line = i, Heading = heading });
                continue;
            }

            if (ChecklistLine.IsMatch(line))
            {
                var item = new NoteItem { Text = Inline(list.Groups[3].Value), Done = done, Line = i, Heading = heading };
                items.Add(item);
                parent = (item, indent);
            }
            else
            {
                parent = null;
            }
        }
        return items;
    }

    /// <summary>
    /// Each non-blank line of a passage as an item, its list marker or heading
    /// marks taken off; lines indented under one become its subtasks.
    /// </summary>
    public static List<NoteItem> Passage(string text, int firstLine)
    {
        var items = new List<NoteItem>();
        (NoteItem Item, int Indent)? parent = null;

        var lines = Lines(text);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0 || Fence.IsMatch(line)) continue;

            var list = ListLine.Match(line);
            var indent = Indent(line[..(line.Length - line.TrimStart().Length)]);
            var body = list.Success ? list.Groups[3].Value : HeadingLine.Match(line) is { Success: true } h ? h.Groups[2].Value : line.Trim();
            var done = list.Success && list.Groups[2].Success && list.Groups[2].Value is "x" or "X";
            var item = new NoteItem { Text = Inline(body), Done = done, Line = firstLine + i };

            if (parent is { } p && indent > p.Indent) p.Item.Children.Add(item);
            else
            {
                items.Add(item);
                parent = (item, indent);
            }
        }
        return items;
    }

    /// <summary>The first heading's text, else the file name without its extension.</summary>
    public static string TitleOf(string text, string path)
        => Headings(text).FirstOrDefault(h => h.Level == 1)?.Text
           ?? Headings(text).FirstOrDefault()?.Text
           ?? Path.GetFileNameWithoutExtension(path);

    private static int Indent(string whitespace) => whitespace.Sum(c => c == '\t' ? 4 : 1);

    private static readonly Regex Emphasis = new(@"(\*\*|__|\*|_|~~|`)(.+?)\1", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);

    /// <summary>Plain text for a title: links become their text and emphasis marks go.</summary>
    private static string Inline(string text) => Emphasis.Replace(Link.Replace(text, "$1"), "$2").Trim();
}
