using System.Text.RegularExpressions;

namespace MikuDo.Services;

/// <summary>
/// What the find bar looks for: the words typed, and how they are matched.
/// Match case keeps capitals apart; whole word takes a match only where no
/// letter (with its accents and vowel marks), digit or underscore runs on
/// either side; regex reads the words as a
/// .NET regular expression, line by line (^ and $ at each line).
/// </summary>
public sealed record FindPattern(string Text, bool MatchCase, bool WholeWord, bool UseRegex)
{
    /// <summary>More matches than this are not counted on: the first ones are shown and the count says "+".</summary>
    public const int Limit = 5000;

    public bool IsEmpty => Text.Length == 0;

    /// <summary>The expression to search with; null with <paramref name="error"/> saying why when the regex does not parse.</summary>
    public Regex? Build(out string? error)
    {
        error = null;
        if (IsEmpty) return null;

        var pattern = UseRegex ? Text : Regex.Escape(Text);
        if (WholeWord) pattern = $@"(?<![\p{{L}}\p{{M}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{M}}\p{{N}}_])";
        var options = RegexOptions.CultureInvariant | RegexOptions.Multiline | (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try
        {
            return new Regex(pattern, options, TimeSpan.FromMilliseconds(300));
        }
        catch (ArgumentException)
        {
            error = "Not a valid regex";
            return null;
        }
    }

    /// <summary>
    /// Every match in <paramref name="text"/>, as start and length in it, up to
    /// <see cref="Limit"/>; an empty match is passed over. Lines end in "\r\n"
    /// in an editor, so the search runs on "\n" alone, as a regex's $ expects,
    /// and each match is placed back in the text as it is.
    /// </summary>
    public List<(int Start, int Length)> Matches(string text, out string? error, out bool more)
    {
        var found = new List<(int, int)>();
        more = false;
        if (Build(out error) is not { } regex) return found;

        var plain = text.Replace("\r\n", "\n");
        // Where each character of the plain text starts in the original: a line break there is two characters.
        int[]? at = null;
        if (plain.Length != text.Length)
        {
            at = new int[plain.Length + 1];
            var j = 0;
            for (var i = 0; i < plain.Length; i++)
            {
                at[i] = j;
                j += text[j] == '\r' && j + 1 < text.Length && text[j + 1] == '\n' ? 2 : 1;
            }
            at[plain.Length] = j;
        }

        try
        {
            for (var m = regex.Match(plain); m.Success; m = m.NextMatch())
            {
                if (m.Length == 0) continue;
                if (found.Count == Limit)
                {
                    more = true;
                    break;
                }
                var start = at?[m.Index] ?? m.Index;
                var end = at?[m.Index + m.Length] ?? m.Index + m.Length;
                found.Add((start, end - start));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            error = "This regex takes too long here";
            found.Clear();
        }
        return found;
    }

    /// <summary>The pattern as the preview's script takes it.</summary>
    public object ForScript() => new { text = Text, matchCase = MatchCase, wholeWord = WholeWord, regex = UseRegex, limit = Limit };
}
