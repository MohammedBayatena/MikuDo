using System.Windows;

namespace MikuDo.Controls;

/// <summary>Whether a square in the sidebar wears the small pin on its top left corner: it is pinned to SHORTCUTS.</summary>
public static class PinBadge
{
    public static readonly DependencyProperty ShownProperty = DependencyProperty.RegisterAttached(
        "Shown", typeof(bool), typeof(PinBadge), new PropertyMetadata(false));

    public static void SetShown(DependencyObject element, bool value) => element.SetValue(ShownProperty, value);
    public static bool GetShown(DependencyObject element) => (bool)element.GetValue(ShownProperty);
}
