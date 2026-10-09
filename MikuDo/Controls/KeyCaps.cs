using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MikuDo.Controls;

/// <summary>
/// A shortcut drawn as keycaps, side by side: each key a rounded box with a
/// thicker bottom edge, its name centred. Drawn rather than built from
/// elements, so a list with keys on every row stays as cheap as plain text.
/// </summary>
public class KeyCaps : FrameworkElement
{
    public static readonly DependencyProperty KeysProperty = DependencyProperty.Register(
        nameof(Keys), typeof(IReadOnlyList<string>), typeof(KeyCaps),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CapBackgroundProperty = Brush(nameof(CapBackground));
    public static readonly DependencyProperty CapBorderProperty = Brush(nameof(CapBorder));
    public static readonly DependencyProperty ForegroundProperty = Brush(nameof(Foreground));

    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(KeyCaps),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Brush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(KeyCaps), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<string>? Keys { get => (IReadOnlyList<string>?)GetValue(KeysProperty); set => SetValue(KeysProperty, value); }
    public Brush? CapBackground { get => (Brush?)GetValue(CapBackgroundProperty); set => SetValue(CapBackgroundProperty, value); }
    public Brush? CapBorder { get => (Brush?)GetValue(CapBorderProperty); set => SetValue(CapBorderProperty, value); }
    public Brush? Foreground { get => (Brush?)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    // The keycap of the designs: 20 high, at least 20 wide, 6 either side of the name, 3 between keys.
    private const double CapHeight = 20;
    private const double MinCapWidth = 20;
    private const double SidePadding = 6;
    private const double Gap = 3;
    private const double Radius = 5;
    private const double FontSize = 11;

    /// <summary>
    /// Shaped key names, kept: the same few keys in the same colours are drawn
    /// on every row, and shaping text is the dearest part of drawing one.
    /// </summary>
    private static readonly Dictionary<(string Key, FontFamily Font, Brush Ink, double Dpi), FormattedText> Shaped = new();

    private FormattedText Text(string key, double dpi)
    {
        var ink = Foreground ?? Brushes.Black;
        if (Shaped.TryGetValue((key, FontFamily, ink, dpi), out var text)) return text;
        if (Shaped.Count > 512) Shaped.Clear();
        text = new FormattedText(key, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                 new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                                 FontSize, ink, dpi);
        Shaped[(key, FontFamily, ink, dpi)] = text;
        return text;
    }

    private double CapWidth(FormattedText text) => Math.Max(MinCapWidth, Math.Ceiling(text.WidthIncludingTrailingWhitespace) + 2 * SidePadding);

    protected override Size MeasureOverride(Size available)
    {
        var keys = Keys;
        if (keys == null || keys.Count == 0) return new Size();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double width = 0;
        foreach (var key in keys) width += CapWidth(Text(key, dpi));
        return new Size(width + Gap * (keys.Count - 1), CapHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var keys = Keys;
        if (keys == null || keys.Count == 0) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var top = Math.Round((RenderSize.Height - CapHeight) / 2);
        double x = 0;
        foreach (var key in keys)
        {
            var text = Text(key, dpi);
            var width = CapWidth(text);
            // The edge is the box behind; the face sits on it, one pixel in and two up from the bottom.
            dc.DrawRoundedRectangle(CapBorder, null, new Rect(x, top, width, CapHeight), Radius, Radius);
            dc.DrawRoundedRectangle(CapBackground, null, new Rect(x + 1, top + 1, width - 2, CapHeight - 3), Radius - 1, Radius - 1);
            dc.DrawText(text, new Point(x + Math.Round((width - text.Width) / 2), top + Math.Round((CapHeight - 1 - text.Height) / 2)));
            x += width + Gap;
        }
    }
}
