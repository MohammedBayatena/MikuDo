using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace MikuDo.Controls;

/// <summary>
/// The soft shadow of a rounded card, sized to this element and drawn beneath
/// whatever sits on top of it. Place it behind the card, in the same cell.
/// </summary>
/// <remarks>
/// A <see cref="DropShadowEffect"/> is worked out again every time anything it
/// overlaps is redrawn, so behind a dialog every caret blink, keystroke and
/// scroll step would re-blur the whole card. This blurs once, into a picture a
/// quarter of the size (a blur has no detail to lose), and from then on only
/// stretches that picture. A new one is made only when the size or look changes.
/// </remarks>
public class SoftShadow : FrameworkElement
{
    public static readonly DependencyProperty CornerRadiusProperty = Register(nameof(CornerRadius), 10.0);
    public static readonly DependencyProperty BlurRadiusProperty = Register(nameof(BlurRadius), 40.0);
    public static readonly DependencyProperty ShadowDepthProperty = Register(nameof(ShadowDepth), 12.0);
    public static readonly DependencyProperty ShadowOpacityProperty = Register(nameof(ShadowOpacity), 0.2);

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(SoftShadow),
        new FrameworkPropertyMetadata(Colors.Black, FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double BlurRadius { get => (double)GetValue(BlurRadiusProperty); set => SetValue(BlurRadiusProperty, value); }

    /// <summary>How far the shadow falls below the card.</summary>
    public double ShadowDepth { get => (double)GetValue(ShadowDepthProperty); set => SetValue(ShadowDepthProperty, value); }

    public double ShadowOpacity { get => (double)GetValue(ShadowOpacityProperty); set => SetValue(ShadowOpacityProperty, value); }
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    public static readonly DependencyProperty SizeStepProperty = Register(nameof(SizeStep), 0.0);

    /// <summary>
    /// For a card whose size keeps changing, such as a list that grows and
    /// shrinks as it is searched: the picture is made for the size rounded up
    /// to this step and stretched the few pixels to fit, so it is blurred
    /// again only when the size crosses a step. Nothing, by default: an exact
    /// picture for every size.
    /// </summary>
    public double SizeStep { get => (double)GetValue(SizeStepProperty); set => SetValue(SizeStepProperty, value); }

    /// <summary>The picture is made at this fraction of the size it is drawn at.</summary>
    private const double Scale = 0.25;

    private ImageSource? _picture;
    private Size _pictureSize;

    /// <summary>With a <see cref="SizeStep"/>, the last few pictures made: a list that goes back and forth between two heights blurs each once.</summary>
    private readonly List<(Size Size, ImageSource Picture)> _kept = new();

    static SoftShadow()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(SoftShadow), new UIPropertyMetadata(false));

        // The picture is a blur stretched four times over: the plainest
        // filtering looks the same and costs least.
        RenderOptions.BitmapScalingModeProperty.OverrideMetadata(
            typeof(SoftShadow), new FrameworkPropertyMetadata(BitmapScalingMode.LowQuality));
    }

    private static DependencyProperty Register(string name, double fallback)
        => DependencyProperty.Register(name, typeof(double), typeof(SoftShadow),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var shadow = (SoftShadow)d;
        shadow._picture = null;
        shadow._kept.Clear();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width < 1 || size.Height < 1) return;

        var step = SizeStep;
        var made = step > 0 ? new Size(Math.Ceiling(size.Width / step) * step, Math.Ceiling(size.Height / step) * step) : size;
        if (_picture == null || _pictureSize != made)
        {
            var kept = step > 0 ? _kept.FindIndex(k => k.Size == made) : -1;
            _picture = kept >= 0 ? _kept[kept].Picture : Paint(made);
            _pictureSize = made;
            if (step > 0 && kept < 0)
            {
                _kept.Add((made, _picture));
                if (_kept.Count > 6) _kept.RemoveAt(0);
            }
        }

        var spread = BlurRadius;
        dc.DrawImage(_picture, new Rect(-spread, ShadowDepth - spread,
                                        size.Width + 2 * spread, size.Height + 2 * spread));
    }

    /// <summary>The card's outline, blurred, with room around it for the blur to fade out.</summary>
    private ImageSource Paint(Size size)
    {
        var pad = BlurRadius * Scale;
        var width = Math.Max(1, (int)Math.Ceiling(size.Width * Scale + 2 * pad));
        var height = Math.Max(1, (int)Math.Ceiling(size.Height * Scale + 2 * pad));

        var fill = new SolidColorBrush(Color) { Opacity = ShadowOpacity };
        fill.Freeze();

        var visual = new DrawingVisual
        {
            Effect = new BlurEffect { Radius = BlurRadius * Scale, KernelType = KernelType.Gaussian }
        };
        using (var context = visual.RenderOpen())
        {
            var radius = CornerRadius * Scale;
            context.DrawRoundedRectangle(fill, null, new Rect(pad, pad, size.Width * Scale, size.Height * Scale),
                                         radius, radius);
        }

        var picture = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        picture.Render(visual);
        picture.Freeze();
        return picture;
    }
}
