using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MikuDo.Controls;

/// <summary>
/// How far one notch of the mouse wheel scrolls a list, in pixels. WPF's own
/// step is 48px, under two rows of a picker list; Windows lists move three
/// rows a notch, so a list of short rows sets this to three of its rows.
/// </summary>
/// <remarks>
/// Set it on the list or on anything holding its ScrollViewer. At either end
/// the notch is left alone, so it goes on to scroll the page around the list.
/// </remarks>
public static class WheelScroll
{
    public static readonly DependencyProperty PixelsProperty = DependencyProperty.RegisterAttached(
        "Pixels", typeof(double), typeof(WheelScroll), new PropertyMetadata(0.0, OnPixelsChanged));

    public static void SetPixels(DependencyObject element, double value) => element.SetValue(PixelsProperty, value);
    public static double GetPixels(DependencyObject element) => (double)element.GetValue(PixelsProperty);

    private static void OnPixelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseWheel -= OnWheel;
        if (e.NewValue is double pixels && pixels > 0) element.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject owner || ScrollerIn(owner) is not { } scroller) return;
        if (scroller.ScrollableHeight <= 0) return;

        var down = e.Delta < 0;
        if (down && scroller.VerticalOffset >= scroller.ScrollableHeight) return;
        if (!down && scroller.VerticalOffset <= 0) return;

        scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta / 120.0 * GetPixels(owner));
        e.Handled = true;
    }

    /// <summary>The element itself if it is a ScrollViewer, else the first one inside it.</summary>
    private static ScrollViewer? ScrollerIn(DependencyObject root)
    {
        if (root is ScrollViewer own) return own;

        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is ScrollViewer scroller) return scroller;
                queue.Enqueue(child);
            }
        }
        return null;
    }
}
