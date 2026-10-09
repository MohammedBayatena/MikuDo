using System.Windows;
using System.Windows.Controls;

namespace MikuDo.Controls;

/// <summary>
/// Lays its children in a row, centred on one line, and gives the first one
/// whatever room the others leave. A long title then trims while the chip
/// after it stays whole and right beside it, however much space there is.
/// </summary>
public class LeadTrimPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        if (Children.Count == 0) return new Size();

        double others = 0, height = 0;
        for (var i = 1; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            others += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var lead = Children[0];
        lead.Measure(new Size(Math.Max(0, available.Width - others), available.Height));
        height = Math.Max(height, lead.DesiredSize.Height);
        return new Size(lead.DesiredSize.Width + others, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double others = 0;
        for (var i = 1; i < Children.Count; i++) others += Children[i].DesiredSize.Width;

        double x = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var width = i == 0 ? Math.Min(child.DesiredSize.Width, Math.Max(0, final.Width - others)) : child.DesiredSize.Width;
            var height = child.DesiredSize.Height;
            child.Arrange(new Rect(x, (final.Height - height) / 2, width, height));
            x += width;
        }
        return final;
    }
}
