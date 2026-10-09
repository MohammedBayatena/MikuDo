using System.Windows;
using System.Windows.Media;

namespace MikuDo.Controls;

/// <summary>
/// Clips an element to a rounded rectangle.
///
/// A Border's CornerRadius only rounds what the Border itself paints;
/// ClipToBounds then clips children to a plain rectangle, so any child that
/// paints its own background — the sidebar, each page's root grid — fills the
/// corners back in square. This keeps a real rounded clip on the element and
/// resizes it with the element.
/// </summary>
public static class RoundedClip
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(RoundedClip),
        new PropertyMetadata(0.0, OnRadiusChanged));

    public static void SetRadius(DependencyObject element, double value)
        => element.SetValue(RadiusProperty, value);

    public static double GetRadius(DependencyObject element)
        => (double)element.GetValue(RadiusProperty);

    private static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        element.SizeChanged -= OnSizeChanged;
        element.SizeChanged += OnSizeChanged;
        UpdateClip(element);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateClip((FrameworkElement)sender);

    private static void UpdateClip(FrameworkElement element)
    {
        var radius = GetRadius(element);
        if (radius <= 0 || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            element.Clip = null;
            return;
        }

        var clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
        clip.Freeze();
        element.Clip = clip;
    }
}
