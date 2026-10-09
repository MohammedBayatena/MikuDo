using System.Text;
using System.Text.RegularExpressions;
using MikuDo.Models;

namespace MikuDo.Services;

/// <summary>Tasks exported under one heading, in the order they were shown.</summary>
public sealed record ExportGroup(string? Heading, IReadOnlyList<TodoItem> Items);

/// <summary>
/// Turns tasks into a markdown checklist: one item per task, carrying its title,
/// priority, labels and description.
/// </summary>
/// <remarks>
/// Images and voice memos are references into the app's own storage, so they
/// mean nothing outside it and are dropped. A description long enough to drown
/// the list is summarized instead of copied.
/// </remarks>
public static class MarkdownExport
{
    /// <summary>From this many lines on, a description is summarized.</summary>
    public const int SummaryThreshold = 100;

    private static readonly Regex Media =
        new(@"!?\[[^\]]*\]\((?:img|voice)://\d+\)", RegexOptions.Compiled);

    private static readonly Regex BlankRuns = new(@"\n{3,}", RegexOptions.Compiled);

    /// <summary>Summarizes text that is too long, or returns null when it cannot.</summary>
    public delegate Task<string?> Summarizer(string text, CancellationToken ct);

    public static string Clean(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        var text = Media.Replace(description.Replace("\r\n", "\n"), string.Empty);
        return BlankRuns.Replace(text, "\n\n").Trim();
    }

    public static int LineCount(string text) => text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;

    public static bool NeedsSummary(TodoItem item) => LineCount(Clean(item.Description)) >= SummaryThreshold;

    public static async Task<string> BuildAsync(
        string title, IReadOnlyList<ExportGroup> groups, Summarizer summarize,
        IProgress<string> status, CancellationToken ct)
    {
        var total = groups.Sum(g => g.Items.Count);
        var longOnes = groups.SelectMany(g => g.Items).Count(NeedsSummary);
        var summarized = 0;

        var md = new StringBuilder()
            .Append("# ").AppendLine(title)
            .AppendLine()
            .Append('_').Append(total == 1 ? "1 task" : $"{total} tasks")
            .Append(", exported ").Append(DateTime.Now.ToString("d MMM yyyy, HH:mm")).AppendLine("_");

        foreach (var group in groups)
        {
            if (group.Items.Count == 0) continue;

            md.AppendLine();
            if (!string.IsNullOrWhiteSpace(group.Heading)) md.Append("## ").AppendLine(group.Heading).AppendLine();

            foreach (var item in group.Items)
            {
                ct.ThrowIfCancellationRequested();
                AppendHeadline(md, item);

                var body = Clean(item.Description);
                if (body.Length == 0) continue;

                var lines = LineCount(body);
                if (lines >= SummaryThreshold)
                {
                    summarized++;
                    status.Report($"Summarizing {summarized} of {longOnes}: {item.Title}");
                    body = await SummarizeOrExcerptAsync(body, lines, summarize, ct);
                }

                AppendBody(md, body);
            }
        }

        return md.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendHeadline(StringBuilder md, TodoItem item)
    {
        md.Append(item.Status == TodoStatus.Completed ? "- [x] **" : "- [ ] **")
          .Append(Escape(item.Title.Trim().Length == 0 ? "Untitled" : item.Title.Trim()))
          .Append("**");

        if (item.Priority != TodoPriority.None) md.Append(" · ").Append(item.Priority).Append(" priority");
        foreach (var tag in item.Tags) md.Append(" `").Append(tag.Replace("`", "")).Append('`');

        md.AppendLine();
    }

    /// <summary>
    /// Indented under the bullet, after a blank line, so markdown keeps the
    /// description inside its task instead of starting a paragraph of its own.
    /// </summary>
    private static void AppendBody(StringBuilder md, string body)
    {
        md.AppendLine();
        foreach (var line in body.Split('\n'))
            md.AppendLine(line.Length == 0 ? string.Empty : "  " + line.TrimEnd());
        md.AppendLine();
    }

    private static async Task<string> SummarizeOrExcerptAsync(
        string body, int lines, Summarizer summarize, CancellationToken ct)
    {
        string? summary = null;
        try { summary = await summarize(body, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { LogService.Error("Export summary failed", ex); }

        if (!string.IsNullOrWhiteSpace(summary))
            return summary.Trim() + $"\n\n_Summarized from {lines} lines._";

        return Excerpt(body) + $"\n\n_First part of {lines} lines; no AI model is installed to summarize it._";
    }

    /// <summary>The opening of the text, cut at a line rather than mid-sentence.</summary>
    private static string Excerpt(string body)
    {
        const int maxLines = 12;
        const int maxChars = 900;

        var kept = new StringBuilder();
        var count = 0;
        foreach (var line in body.Split('\n'))
        {
            if (count >= maxLines || kept.Length + line.Length > maxChars) break;
            kept.AppendLine(line);
            if (line.Trim().Length > 0) count++;
        }
        return kept.ToString().TrimEnd() + "\n…";
    }

    private static string Escape(string text) => text.Replace("*", "\\*").Replace("_", "\\_");
}
