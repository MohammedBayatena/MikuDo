using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace MikuDo.Controls;

/// <summary>
/// The markdown editor's formatting bar and shortcuts: wrap a selection in
/// emphasis, turn lines into lists, quotes or headings, insert blocks, and
/// carry a list on to the next line.
/// </summary>
/// <remarks>
/// Every change goes through the box's selection rather than by assigning its
/// whole text, so each one is a single step Ctrl+Z takes back and the caret
/// stays where the user is working. Applying a format to text that already has
/// it takes it off again, as GitHub's editor does.
/// </remarks>
public static class MarkdownEditing
{
    /// <summary>What Enter inserts in a WPF text box, so new lines match the ones typed.</summary>
    private const string Nl = "\r\n";

    /// <summary>Any list marker, keeping the indentation in front of it.</summary>
    private static readonly Regex ListMarker =
        new(@"^([ \t]*)(?:[-*+][ \t]+\[[ xX]\][ \t]+|[-*+][ \t]+|\d+[.)][ \t]+)", RegexOptions.Compiled);

    private static readonly Regex Bullet = new(@"^[ \t]*[-*+][ \t]+(?!\[[ xX]\])", RegexOptions.Compiled);
    private static readonly Regex Numbered = new(@"^[ \t]*\d+[.)][ \t]+", RegexOptions.Compiled);
    private static readonly Regex Task = new(@"^[ \t]*[-*+][ \t]+\[[ xX]\][ \t]+", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^#{1,6}[ \t]+", RegexOptions.Compiled);
    private static readonly Regex Quote = new(@"^>[ \t]?", RegexOptions.Compiled);
    private static readonly Regex Indent = new(@"^[ \t]*", RegexOptions.Compiled);

    /// <summary>A list line up to its content: indentation, then the marker.</summary>
    private static readonly Regex Continuable =
        new(@"^([ \t]*)(?:([-*+])[ \t]+\[[ xX]\][ \t]+|([-*+])[ \t]+|(\d+)([.)])[ \t]+)", RegexOptions.Compiled);

    public static void Apply(TextBox box, string action)
    {
        switch (action)
        {
            case "bold": Wrap(box, "**", "bold text"); break;
            case "italic": Wrap(box, "_", "italic text"); break;
            case "strike": Wrap(box, "~~", "struck text"); break;
            case "code": Wrap(box, "`", "code"); break;
            case "link": Link(box); break;
            case "heading": Lines(box, Heading, l => Heading.Replace(l, "", 1), (l, _) => "### " + l); break;
            case "quote": Lines(box, Quote, l => Quote.Replace(l, "", 1), (l, _) => "> " + l); break;
            case "bullet": Lines(box, Bullet, StripList, (l, _) => Marked(l, "- ")); break;
            case "number": Lines(box, Numbered, StripList, (l, n) => Marked(l, $"{n}. ")); break;
            case "task": Lines(box, Task, StripList, (l, _) => Marked(l, "- [ ] ")); break;
            case "codeblock": Fence(box); break;
            case "rule": Block(box, "---", 3); break;
            case "table": Block(box, "| Column | Column |" + Nl + "| --- | --- |" + Nl + "|  |  |", 2, 6); break;
            default:
                if (Diagrams.TryGetValue(action, out var diagram)) Diagram(box, diagram);
                break;
        }
    }

    // ── Diagrams ────────────────────────────────────────────────

    /// <summary>A small working example of each kind of Mermaid diagram, keyed by its action.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Diagrams = new Dictionary<string, string[]>
    {
        ["diagram-flowchart"] = new[] { "flowchart LR", "  A[Start] --> B{Decide}", "  B -->|yes| C[Do it]", "  B -->|no| D[Leave it]" },
        ["diagram-sequence"] = new[] { "sequenceDiagram", "  You->>App: Ask", "  App-->>You: Answer" },
        ["diagram-state"] = new[] { "stateDiagram-v2", "  [*] --> ToDo", "  ToDo --> Doing", "  Doing --> Done", "  Done --> [*]" },
        ["diagram-gantt"] = new[] { "gantt", "  dateFormat YYYY-MM-DD", "  section Work", "  First step :a1, 2026-10-01, 5d", "  Second step :after a1, 3d" },
        ["diagram-mindmap"] = new[] { "mindmap", "  root((Idea))", "    One", "    Two", "    Three" }
    };

    /// <summary>A ```mermaid block with the example in it, the caret on its first line after the kind.</summary>
    private static void Diagram(TextBox box, string[] lines)
    {
        var block = "```mermaid" + Nl + string.Join(Nl, lines) + Nl + "```";
        var caret = ("```mermaid" + Nl + lines[0] + Nl).Length;
        Block(box, block, caret, lines.Length > 1 ? lines[1].Length : 0);
    }

    // ── Inline ──────────────────────────────────────────────────

    private static void Wrap(TextBox box, string mark, string placeholder)
    {
        var text = box.Text;
        var start = box.SelectionStart;
        var length = box.SelectionLength;
        var m = mark.Length;

        // Hug the words: a double-click also takes the space after a word, and
        // "**word **" is not bold in markdown.
        while (length > 0 && char.IsWhiteSpace(text[start + length - 1])) length--;
        while (length > 0 && char.IsWhiteSpace(text[start])) { start++; length--; }

        // The marks sit just outside the selection: take them off.
        if (start >= m && start + length + m <= text.Length &&
            string.CompareOrdinal(text, start - m, mark, 0, m) == 0 &&
            string.CompareOrdinal(text, start + length, mark, 0, m) == 0)
        {
            Replace(box, start - m, length + 2 * m, text.Substring(start, length));
            box.Select(start - m, length);
            return;
        }

        // The selection carries the marks itself: take them off.
        if (length >= 2 * m &&
            string.CompareOrdinal(text, start, mark, 0, m) == 0 &&
            string.CompareOrdinal(text, start + length - m, mark, 0, m) == 0)
        {
            var inner = text.Substring(start + m, length - 2 * m);
            Replace(box, start, length, inner);
            box.Select(start, inner.Length);
            return;
        }

        if (length == 0)
        {
            Replace(box, start, 0, mark + placeholder + mark);
            box.Select(start + m, placeholder.Length);
            return;
        }

        Replace(box, start, length, mark + text.Substring(start, length) + mark);
        box.Select(start + m, length);
    }

    private static void Link(TextBox box)
    {
        var start = box.SelectionStart;
        var selected = box.SelectedText.Trim();

        if (selected.Length == 0)
        {
            Replace(box, start, box.SelectionLength, "[link text](url)");
            box.Select(start + 1, "link text".Length);
            return;
        }

        // A selected address becomes the target, and the caret goes to the text.
        if (Uri.TryCreate(selected, UriKind.Absolute, out _))
        {
            Replace(box, start, box.SelectionLength, $"[link text]({selected})");
            box.Select(start + 1, "link text".Length);
            return;
        }

        Replace(box, start, box.SelectionLength, $"[{selected}](url)");
        box.Select(start + selected.Length + 3, "url".Length);
    }

    // ── Lines ───────────────────────────────────────────────────

    private static string StripList(string line) => ListMarker.Replace(line, "$1", 1);

    /// <summary>A marker after the line's indentation, so nested items stay nested.</summary>
    private static string Marked(string line, string marker)
    {
        var indent = Indent.Match(line).Value;
        return indent + marker + line[indent.Length..];
    }

    /// <summary>
    /// Applies a line format to every line the selection touches. When every
    /// line with content already has it, it comes off instead. Blank lines
    /// inside a selection stay blank rather than gaining an empty marker.
    /// </summary>
    private static void Lines(TextBox box, Regex has, Func<string, string> strip, Func<string, int, string> add)
    {
        var text = box.Text;
        var caret = box.SelectionStart;
        var caretOnly = box.SelectionLength == 0;
        var (lineStart, lineEnd) = LineSpan(text, box.SelectionStart, box.SelectionLength);

        // Each line keeps its own ending: a '\r' before the '\n' stays put.
        var lines = text.Substring(lineStart, lineEnd - lineStart).Split('\n')
            .Select(l => l.EndsWith('\r') ? (Body: l[..^1], Cr: "\r") : (Body: l, Cr: ""))
            .ToList();

        var filled = lines.Where(l => l.Body.Trim().Length > 0).ToList();
        var takeOff = filled.Count > 0 && filled.All(l => has.IsMatch(l.Body));

        var n = 0;
        var result = lines.Select(l =>
        {
            if (l.Body.Trim().Length == 0 && lines.Count > 1) return l.Body + l.Cr;
            if (takeOff) return strip(l.Body) + l.Cr;
            return add(strip(l.Body), ++n) + l.Cr;
        });

        var block = string.Join("\n", result);
        Replace(box, lineStart, lineEnd - lineStart, block);

        if (caretOnly)
        {
            var moved = caret + (block.Length - (lineEnd - lineStart));
            box.Select(Math.Clamp(moved, lineStart, lineStart + block.Length), 0);
        }
        else
        {
            box.Select(lineStart, block.Length);
        }
    }

    /// <summary>
    /// The whole lines a selection touches, without their final line break. A
    /// selection ending exactly at the start of a line does not take that line.
    /// </summary>
    private static (int Start, int End) LineSpan(string text, int start, int length)
    {
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;

        var last = start + length;
        if (length > 0 && last > lineStart && text[last - 1] == '\n') last--;

        var lineEnd = text.IndexOf('\n', last);
        if (lineEnd < 0) lineEnd = text.Length;
        if (lineEnd > lineStart && text[lineEnd - 1] == '\r' && lineEnd < text.Length) lineEnd--;

        return (lineStart, lineEnd);
    }

    // ── Blocks ──────────────────────────────────────────────────

    /// <summary>Fences the selected lines as code, or opens an empty fence; a fenced selection loses its fence.</summary>
    private static void Fence(TextBox box)
    {
        var text = box.Text;

        if (box.SelectionLength == 0)
        {
            Block(box, "```" + Nl + Nl + "```", 3 + Nl.Length);
            return;
        }

        var (lineStart, lineEnd) = LineSpan(text, box.SelectionStart, box.SelectionLength);
        var inner = text.Substring(lineStart, lineEnd - lineStart);
        var lines = inner.Replace("\r\n", "\n").Split('\n');

        if (lines.Length >= 2 && lines[0].Trim().StartsWith("```") && lines[^1].Trim() == "```")
        {
            var body = string.Join(Nl, lines[1..^1]);
            Replace(box, lineStart, lineEnd - lineStart, body);
            box.Select(lineStart, body.Length);
            return;
        }

        Replace(box, lineStart, lineEnd - lineStart, "```" + Nl + inner + Nl + "```");
        box.Select(lineStart + 3 + Nl.Length, inner.Length);
    }

    /// <summary>
    /// Puts a block on lines of its own, with a blank line before it where the
    /// text above has content, then selects <paramref name="selectLength"/>
    /// characters from <paramref name="caretInBlock"/>.
    /// </summary>
    private static void Block(TextBox box, string block, int caretInBlock, int selectLength = 0)
    {
        var text = box.Text;
        var at = box.SelectionStart + box.SelectionLength;

        string before;
        if (at == 0) before = "";
        else if (text[at - 1] != '\n') before = Nl + Nl;
        else
        {
            var prevStart = at < 2 ? 0 : text.LastIndexOf('\n', at - 2) + 1;
            before = text.Substring(prevStart, at - prevStart).Trim().Length > 0 ? Nl : "";
        }

        var after = at < text.Length && text[at] != '\r' && text[at] != '\n' ? Nl + Nl : Nl;

        Replace(box, at, 0, before + block + after);
        box.Select(at + before.Length + caretInBlock, selectLength);
    }

    // ── Enter ───────────────────────────────────────────────────

    /// <summary>
    /// Enter on a list line starts the next item; Enter on an item with nothing
    /// in it ends the list instead. False when the line is not a list.
    /// </summary>
    public static bool ContinueList(TextBox box)
    {
        if (box.SelectionLength > 0) return false;

        var text = box.Text;
        var caret = box.SelectionStart;
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;

        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0) lineEnd = text.Length;
        var line = text.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r');

        var m = Continuable.Match(line);
        if (!m.Success || caret - lineStart < m.Length) return false;

        if (line.Trim() == m.Value.Trim())
        {
            Replace(box, lineStart, m.Length, m.Groups[1].Value);
            box.Select(lineStart + m.Groups[1].Length, 0);
            return true;
        }

        var indent = m.Groups[1].Value;
        var next = m.Groups[2].Success ? $"{m.Groups[2].Value} [ ] "
                 : m.Groups[3].Success ? $"{m.Groups[3].Value} "
                 : $"{int.Parse(m.Groups[4].Value) + 1}{m.Groups[5].Value} ";

        Replace(box, caret, 0, Nl + indent + next);
        box.Select(caret + Nl.Length + indent.Length + next.Length, 0);
        return true;
    }

    private static void Replace(TextBox box, int start, int length, string with)
    {
        box.Select(start, length);
        box.SelectedText = with;
    }
}
