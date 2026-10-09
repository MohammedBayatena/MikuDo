using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MikuDo.Controls;

/// <summary>
/// The find bar's matches in an editor, drawn under its text: every match in
/// one colour, the current one in another. It sits behind the text box, whose
/// background is clear, and draws only the lines in view, again whenever the
/// editor scrolls or reflows. A match broken over two lines by wrapping gets a
/// mark on each.
/// </summary>
public sealed class FindHighlights : FrameworkElement
{
    public static readonly DependencyProperty MatchBrushProperty = DependencyProperty.Register(
        nameof(MatchBrush), typeof(Brush), typeof(FindHighlights),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ActiveBrushProperty = DependencyProperty.Register(
        nameof(ActiveBrush), typeof(Brush), typeof(FindHighlights),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? MatchBrush { get => (Brush?)GetValue(MatchBrushProperty); set => SetValue(MatchBrushProperty, value); }
    public Brush? ActiveBrush { get => (Brush?)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }

    private TextBox? _box;
    private IReadOnlyList<(int Start, int Length)> _matches = Array.Empty<(int, int)>();
    private int _active = -1;

    public FindHighlights()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        SetResourceReference(MatchBrushProperty, "FindMatchBrush");
        SetResourceReference(ActiveBrushProperty, "FindActiveBrush");
    }

    /// <summary>The editor whose text the marks go under.</summary>
    public void Attach(TextBox box)
    {
        _box = box;
        box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => Redraw()));
        box.SizeChanged += (_, _) => Redraw();
    }

    /// <summary>The matches, in order through the text, and which of them is current (-1 for none).</summary>
    public void Show(IReadOnlyList<(int Start, int Length)> matches, int active)
    {
        _matches = matches;
        _active = active;
        InvalidateVisual();
    }

    public void Clear() => Show(Array.Empty<(int, int)>(), -1);

    private void Redraw()
    {
        if (_matches.Count > 0) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_box is not { IsVisible: true } box || _matches.Count == 0) return;

        var first = box.GetFirstVisibleLineIndex();
        var last = box.GetLastVisibleLineIndex();
        if (first < 0 || last < 0) return;

        var length = box.Text.Length;
        int from, to;
        try
        {
            from = box.GetCharacterIndexFromLineIndex(first);
            to = box.GetCharacterIndexFromLineIndex(last) + box.GetLineLength(last);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        // The lines show between the editor's padding; a line half out of view is cut where its text is.
        var origin = box.TranslatePoint(new Point(0, 0), this);
        var top = origin.Y + box.BorderThickness.Top + box.Padding.Top;
        var bottom = origin.Y + box.ActualHeight - box.BorderThickness.Bottom - box.Padding.Bottom;
        if (bottom <= top) return;
        dc.PushClip(new RectangleGeometry(new Rect(origin.X, top, box.ActualWidth, bottom - top)));

        // The first match that could reach into view, found by halving.
        int lo = 0, hi = _matches.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_matches[mid].Start + _matches[mid].Length < from) lo = mid + 1;
            else hi = mid;
        }

        for (var i = lo; i < _matches.Count && _matches[i].Start <= to; i++)
        {
            var (start, count) = _matches[i];
            if (start + count > length) break;
            var brush = i == _active ? ActiveBrush : MatchBrush;
            foreach (var mark in Marks(box, start, start + count))
            {
                var placed = mark;
                placed.Offset(origin.X, origin.Y);
                dc.DrawRoundedRectangle(brush, null, placed, 2.5, 2.5);
            }
        }
        dc.Pop();
    }

    /// <summary>A mark per line the match covers: one for a match on one line, more where wrapping breaks it.</summary>
    private static IEnumerable<Rect> Marks(TextBox box, int start, int end)
    {
        var a = box.GetRectFromCharacterIndex(start);
        var b = box.GetRectFromCharacterIndex(end - 1, true);
        if (a.IsEmpty || b.IsEmpty) yield break;

        if (Math.Abs(a.Top - b.Top) < 0.5)
        {
            yield return Mark(a.Left, b.Right, a.Top, a.Height);
            yield break;
        }

        var left = a.Left;
        var right = a.Left;
        var line = a.Top;
        var height = a.Height;
        for (var i = start; i < end; i++)
        {
            var lead = box.GetRectFromCharacterIndex(i);
            if (lead.IsEmpty) continue;
            if (Math.Abs(lead.Top - line) >= 0.5)
            {
                if (right > left) yield return Mark(left, right, line, height);
                left = lead.Left;
                line = lead.Top;
                height = lead.Height;
            }
            var trail = box.GetRectFromCharacterIndex(i, true);
            if (!trail.IsEmpty && Math.Abs(trail.Top - line) < 0.5) right = Math.Max(right, trail.Right);
        }
        if (right > left) yield return Mark(left, right, line, height);
    }

    /// <summary>The mark around a run of text: a pixel wider each side, so a single letter still shows.</summary>
    private static Rect Mark(double left, double right, double top, double height)
        => new(left - 1, top, Math.Max(3, right - left + 2), height);
}
