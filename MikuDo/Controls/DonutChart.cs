using System.Windows;
using System.Windows.Media;

namespace MikuDo.Controls;

/// <summary>One slice of a <see cref="DonutChart"/>.</summary>
public sealed record DonutSegment(double Value, Brush Brush);

/// <summary>
/// A ring split into arcs by share, clockwise from the top, with a hairline
/// gap between neighbours. An empty chart shows only its track.
/// </summary>
public class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<DonutSegment>), typeof(DonutChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(DonutChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(DonutChart),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<DonutSegment>? Segments
    {
        get => (IReadOnlyList<DonutSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double RingThickness { get => (double)GetValue(RingThicknessProperty); set => SetValue(RingThicknessProperty, value); }

    /// <summary>The gap between two arcs, in degrees.</summary>
    private const double GapDegrees = 2.5;

    static DonutChart()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(DonutChart), new UIPropertyMetadata(false));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        var radius = (size - RingThickness) / 2;
        if (radius <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        if (Track != null) dc.DrawEllipse(null, new Pen(Track, RingThickness), center, radius, radius);

        var parts = (Segments ?? Array.Empty<DonutSegment>()).Where(s => s.Value > 0).ToList();
        var total = parts.Sum(s => s.Value);
        if (total <= 0) return;

        // A single slice is the whole ring: an arc cannot close on itself.
        if (parts.Count == 1)
        {
            dc.DrawEllipse(null, new Pen(parts[0].Brush, RingThickness), center, radius, radius);
            return;
        }

        var angle = -90.0;
        foreach (var part in parts)
        {
            var sweep = 360 * part.Value / total;
            if (sweep > GapDegrees)
                dc.DrawGeometry(null, new Pen(part.Brush, RingThickness),
                                Arc(center, radius, angle + GapDegrees / 2, angle + sweep - GapDegrees / 2));
            angle += sweep;
        }
    }

    private static Geometry Arc(Point center, double radius, double from, double to)
    {
        Point At(double degrees)
        {
            var rad = degrees * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(from), isFilled: false, isClosed: false);
            ctx.ArcTo(At(to), new Size(radius, radius), 0, isLargeArc: to - from > 180,
                      SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }
}
