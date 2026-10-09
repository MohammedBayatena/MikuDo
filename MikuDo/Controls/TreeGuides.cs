using System.Windows;
using System.Windows.Media;

namespace MikuDo.Controls;

/// <summary>
/// The vertical guide lines down the left of a tree row, one for each level
/// the row sits under. Rows draw their own share, so a run of rows reads as one
/// unbroken line per level.
/// </summary>
public class TreeGuides : FrameworkElement
{
    public static readonly DependencyProperty DepthProperty = DependencyProperty.Register(
        nameof(Depth), typeof(int), typeof(TreeGuides),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(TreeGuides),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Pixels between one level's line and the next.</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(TreeGuides),
        new FrameworkPropertyMetadata(23.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Where the first level's line stands, from the left edge.</summary>
    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset), typeof(double), typeof(TreeGuides),
        new FrameworkPropertyMetadata(15.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Depth
    {
        get => (int)GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public TreeGuides()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnRender(DrawingContext dc)
    {
        if (Depth <= 0 || Stroke == null || ActualHeight <= 0) return;

        var pen = new Pen(Stroke, 1);
        pen.Freeze();
        var height = ActualHeight;
        for (var level = 0; level < Depth; level++)
        {
            // Half a pixel in puts the 1px line on one column of pixels instead of two.
            var x = Math.Round(Offset + Step * level) + 0.5;
            dc.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }
    }
}
