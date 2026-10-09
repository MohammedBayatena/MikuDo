using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MikuDo.Controls;

/// <summary>
/// A speech bubble and a number: how many comments a task has; with
/// <see cref="Words"/>, the number in words, "3 comments", as a card's other
/// counts read. At zero it takes no room and draws nothing.
/// </summary>
/// <remarks>
/// Every card carries one, so like <see cref="PriorityGlyph"/> it draws
/// straight into its own drawing context rather than building a tree.
/// </remarks>
public class CommentCount : FrameworkElement
{
    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
        nameof(Count), typeof(int), typeof(CommentCount),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure |
                                         FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(CommentCount),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits |
                                                    FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(CommentCount),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits |
                                                                     FrameworkPropertyMetadataOptions.AffectsMeasure |
                                                                     FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Room kept before the bubble, only while there is a count to show.</summary>
    public static readonly DependencyProperty LeadingGapProperty = DependencyProperty.Register(
        nameof(LeadingGap), typeof(double), typeof(CommentCount),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure |
                                           FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>"3 comments" in the card's own label size, rather than the bare number.</summary>
    public static readonly DependencyProperty WordsProperty = DependencyProperty.Register(
        nameof(Words), typeof(bool), typeof(CommentCount),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure |
                                             FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The bubble's own colour, a step lighter than the words beside it; the text's colour when unset.</summary>
    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
        nameof(IconBrush), typeof(Brush), typeof(CommentCount),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool Words { get => (bool)GetValue(WordsProperty); set => SetValue(WordsProperty, value); }
    public Brush? IconBrush { get => (Brush?)GetValue(IconBrushProperty); set => SetValue(IconBrushProperty, value); }

    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public double LeadingGap { get => (double)GetValue(LeadingGapProperty); set => SetValue(LeadingGapProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    private const double Box = 12;
    private const double Gap = 4;
    private const double TextSize = 11;

    // In words it matches the card's subtask label: 11.5 regular, eight pixels after the icon.
    private const double WordsGap = 8;
    private const double WordsSize = 11.5;

    private double IconGap => Words ? WordsGap : Gap;

    /// <summary>A rounded bubble with its tail at the lower left, on the 12px box.</summary>
    private static readonly Geometry Bubble = Freeze(Geometry.Parse(
        "M2.8,1.2 H9.2 A2,2 0 0 1 11.2,3.2 V6.8 A2,2 0 0 1 9.2,8.8 H6.2 L3.4,11.2 V8.8 H2.8 " +
        "A2,2 0 0 1 0.8,6.8 V3.2 A2,2 0 0 1 2.8,1.2 Z"));

    static CommentCount()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(CommentCount), new UIPropertyMetadata(false));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(CommentCount), new FrameworkPropertyMetadata(true));
    }

    private static Geometry Freeze(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }

    private FormattedText Text()
    {
        var number = Count.ToString(CultureInfo.CurrentCulture);
        return new FormattedText(
            Words ? $"{number} {(Count == 1 ? "comment" : "comments")}" : number,
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, Words ? FontWeights.Normal : FontWeights.SemiBold, FontStretches.Normal),
            Words ? WordsSize : TextSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Count <= 0) return new Size(0, 0);

        var text = Text();
        return new Size(LeadingGap + Box + IconGap + text.WidthIncludingTrailingWhitespace, Math.Max(Box, text.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Count <= 0) return;

        var text = Text();
        var height = Math.Max(Box, text.Height);
        var pen = new Pen(IconBrush ?? Foreground, 1.3) { LineJoin = PenLineJoin.Round };

        dc.PushTransform(new TranslateTransform(LeadingGap, (height - Box) / 2));
        dc.DrawGeometry(null, pen, Bubble);
        dc.Pop();
        dc.DrawText(text, new Point(LeadingGap + Box + IconGap, (height - text.Height) / 2));
    }
}
