using System.Windows;
using System.Windows.Media;

namespace MikuDo.Controls;

/// <summary>
/// Draws a 24×24 vector icon scaled to <see cref="Size"/>. Geometry lives in
/// Themes/Icons.xaml; stroke width scales with the icon the way an SVG does.
/// </summary>
/// <remarks>
/// The geometry is drawn straight into the element's own drawing context rather
/// than through a Viewbox around a Path. A <see cref="System.Windows.Shapes.Path"/>
/// sizes itself from <c>Geometry.GetRenderBounds(pen)</c>, which widens the
/// stroked outline — arcs flattened, round joins mitred — and does it again on
/// every measure pass, for every icon on screen. Measuring to a known box skips
/// that entirely, and collapses four visuals per icon into one.
/// </remarks>
public class Icon : FrameworkElement
{
    /// <summary>The grid every icon in Themes/Icons.xaml is authored on.</summary>
    private const double DesignSize = 24.0;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPenChanged));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(1.7, FrameworkPropertyMetadataOptions.AffectsRender, OnPenChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure, OnSizeChanged));

    public Geometry? Data { get => (Geometry?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    static Icon()
    {
        FocusableProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(Icon), new UIPropertyMetadata(false));
    }

    private Pen? _pen;
    private Transform? _scale;

    private static void OnPenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((Icon)d)._pen = null;

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((Icon)d)._scale = null;

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
        => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        var data = Data;
        if (data == null || Size <= 0) return;

        var fill = Fill;
        var pen = PenOrNull();
        if (fill == null && pen == null) return;

        // Left to stretch, the icon is handed the whole box it sits in, such
        // as a tile around it; it is drawn in the middle of that box.
        var dx = Math.Max(0, (RenderSize.Width - Size) / 2);
        var dy = Math.Max(0, (RenderSize.Height - Size) / 2);
        if (dx > 0 || dy > 0) dc.PushTransform(new TranslateTransform(dx, dy));

        dc.PushTransform(_scale ??= Frozen(new ScaleTransform(Size / DesignSize, Size / DesignSize)));
        dc.DrawGeometry(fill, pen, data);
        dc.Pop();

        if (dx > 0 || dy > 0) dc.Pop();
    }

    private Pen? PenOrNull()
    {
        if (_pen != null) return _pen;

        var stroke = Stroke;
        if (stroke == null || StrokeThickness <= 0) return null;

        return _pen = Frozen(new Pen(stroke, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        });
    }

    /// <summary>Freezing lets one icon's pen and transform be reused across frames.</summary>
    private static T Frozen<T>(T value) where T : Freezable
    {
        if (value.CanFreeze) value.Freeze();
        return value;
    }
}
