using System.Windows;

namespace MikuDo.Controls;

/// <summary>The name a square button shows under itself, in the sidebar's rows of squares.</summary>
public static class Caption
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Caption), new PropertyMetadata(string.Empty));

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);
    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
}
