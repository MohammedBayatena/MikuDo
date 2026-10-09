using System.Windows;
using System.Windows.Media;
using MikuDo.Models;

namespace MikuDo.Controls;

/// <summary>
/// Three rising bars, as many filled as the priority is high: Low one, Medium
/// two, High three, None none.
/// </summary>
/// <remarks>
/// It reads at a glance without a word, and always takes the same room, so a
/// card's layout does not change with its priority. It draws straight into its
/// own drawing context, one visual per glyph, since every card carries one.
/// </remarks>
public class PriorityGlyph : FrameworkElement
{
    public static readonly DependencyProperty PriorityProperty = DependencyProperty.Register(
        nameof(Priority), typeof(TodoPriority), typeof(PriorityGlyph),
        new FrameworkPropertyMetadata(TodoPriority.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(PriorityGlyph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GhostProperty = DependencyProperty.Register(
        nameof(Ghost), typeof(Brush), typeof(PriorityGlyph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public TodoPriority Priority { get => (TodoPriority)GetValue(PriorityProperty); set => SetValue(PriorityProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Ghost { get => (Brush?)GetValue(GhostProperty); set => SetValue(GhostProperty, value); }

    private const double Box = 12;
    private const double BarWidth = 2.6;

    /// <summary>Bar heights, left to right, on the 12px box.</summary>
    private static readonly double[] Heights = { 4.5, 7.75, 11 };

    static PriorityGlyph()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(PriorityGlyph), new UIPropertyMetadata(false));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(PriorityGlyph), new FrameworkPropertyMetadata(true));
    }

    protected override Size MeasureOverride(Size availableSize) => new(Box, Box);

    protected override void OnRender(DrawingContext dc)
    {
        var filled = (int)Priority;
        var gap = (Box - Heights.Length * BarWidth) / (Heights.Length - 1);

        for (var i = 0; i < Heights.Length; i++)
        {
            var brush = i < filled ? Fill : Ghost;
            if (brush == null) continue;

            var x = i * (BarWidth + gap);
            var rect = new Rect(x, Box - Heights[i], BarWidth, Heights[i]);
            dc.DrawRoundedRectangle(brush, null, rect, 1, 1);
        }
    }
}
