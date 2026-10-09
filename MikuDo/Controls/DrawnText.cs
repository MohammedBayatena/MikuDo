using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MikuDo.Controls;

/// <summary>
/// One line of text, drawn, cut with "…" when it does not fit, with the words
/// of a search picked out: heavier, in <see cref="MatchForeground"/>, on
/// <see cref="MatchBackground"/>. It takes its font and colour from the
/// element around it, as a TextBlock does.
/// </summary>
/// <remarks>
/// A list that changes on every key press lays out the same few strings
/// again and again. A TextBlock formats its text on every measure and again
/// to trim it; this shapes its text once for the room it is given, trimmed
/// if need be, keeps it with the boxes behind its matches, and shapes it
/// anew only when the text, the words, the font or the room change.
/// </remarks>
public class DrawnText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(DrawnText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    public static readonly DependencyProperty WordsProperty = DependencyProperty.Register(
        nameof(Words), typeof(IReadOnlyList<string>), typeof(DrawnText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    public static readonly DependencyProperty MatchForegroundProperty = DependencyProperty.Register(
        nameof(MatchForeground), typeof(Brush), typeof(DrawnText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    public static readonly DependencyProperty MatchBackgroundProperty = DependencyProperty.Register(
        nameof(MatchBackground), typeof(Brush), typeof(DrawnText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(DrawnText), new FrameworkPropertyMetadata(Brushes.Black,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(DrawnText), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLookChanged));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(DrawnText), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLookChanged));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(
        typeof(DrawnText), new FrameworkPropertyMetadata(FontWeights.Normal,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLookChanged));

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IReadOnlyList<string>? Words { get => (IReadOnlyList<string>?)GetValue(WordsProperty); set => SetValue(WordsProperty, value); }
    public Brush? MatchForeground { get => (Brush?)GetValue(MatchForegroundProperty); set => SetValue(MatchForegroundProperty, value); }
    public Brush? MatchBackground { get => (Brush?)GetValue(MatchBackgroundProperty); set => SetValue(MatchBackgroundProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DrawnText)d)._shaped = null;

    /// <summary>The text as last shaped, the room it was shaped for, and where its matches sit.</summary>
    private FormattedText? _shaped;
    private double _shapedFor;
    private double _width;
    private Rect[] _boxes = Array.Empty<Rect>();
    private double? _dpi;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _dpi = newDpi.PixelsPerDip;
        _shaped = null;
        InvalidateMeasure();
    }

    /// <summary>
    /// The text shaped for <paramref name="room"/>: whole when it fits, else cut
    /// with "…". Text that fits needs no shaping again for more room.
    /// </summary>
    private FormattedText Fit(double room)
    {
        if (_shaped != null && (_shapedFor.Equals(room) || (room >= _shapedFor && _width < _shapedFor - 0.5))) return _shaped;

        var text = Text ?? string.Empty;
        var shaped = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                       new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal),
                                       FontSize, Foreground, _dpi ??= VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        if (!double.IsInfinity(room)) shaped.MaxTextWidth = Math.Max(1, room);

        var matches = Matches(text, Words);
        foreach (var (start, length) in matches)
        {
            shaped.SetFontWeight(FontWeights.ExtraBold, start, length);
            if (MatchForeground != null) shaped.SetForegroundBrush(MatchForeground, start, length);
        }

        _boxes = MatchBackground == null || matches.Length == 0
            ? Array.Empty<Rect>()
            : matches.Select(m => shaped.BuildHighlightGeometry(new Point(0, 0), m.Start, m.Length)?.Bounds ?? Rect.Empty)
                     .Where(r => !r.IsEmpty).ToArray();
        _shaped = shaped;
        _shapedFor = room;
        _width = shaped.WidthIncludingTrailingWhitespace;
        return shaped;
    }

    protected override Size MeasureOverride(Size available)
    {
        var shaped = Fit(available.Width);
        return new Size(Math.Ceiling(Math.Min(_width, available.Width)), Math.Ceiling(shaped.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Laid out narrower than it was measured, it is cut to the room it got.
        var shaped = _shaped != null && RenderSize.Width >= _width - 0.5 ? _shaped : Fit(RenderSize.Width);
        foreach (var box in _boxes) dc.DrawRoundedRectangle(MatchBackground, null, box, 3, 3);
        dc.DrawText(shaped, new Point(0, 0));
    }

    /// <summary>Each place a word appears, any case, overlapping places joined.</summary>
    public static (int Start, int Length)[] Matches(string text, IReadOnlyList<string>? words)
    {
        if (words == null || words.Count == 0 || text.Length == 0) return Array.Empty<(int, int)>();
        var marked = new bool[text.Length];
        var any = false;
        foreach (var word in words)
        {
            if (word.Length == 0) continue;
            for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0;
                 at = text.IndexOf(word, at + word.Length, StringComparison.OrdinalIgnoreCase))
            {
                for (var i = at; i < at + word.Length; i++) marked[i] = true;
                any = true;
            }
        }
        if (!any) return Array.Empty<(int, int)>();

        var ranges = new List<(int, int)>();
        for (var i = 0; i < text.Length; i++)
        {
            if (!marked[i]) continue;
            var start = i;
            while (i < text.Length && marked[i]) i++;
            ranges.Add((start, i - start));
        }
        return ranges.ToArray();
    }
}
