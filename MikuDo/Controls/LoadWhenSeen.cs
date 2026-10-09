using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace MikuDo.Controls;

/// <summary>
/// Presses a "show more" button by itself once it scrolls into view, so a long
/// list fills in as the user reaches its foot. The button stays clickable for
/// anyone who prefers to, and for a list too short to scroll.
/// </summary>
/// <remarks>
/// It watches the nearest ScrollViewer above it. A press that adds rows grows
/// the scroll extent, which reports a scroll change and so checks again: a
/// list keeps loading until the button is pushed out of view or runs out.
/// </remarks>
public static class LoadWhenSeen
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(LoadWhenSeen), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>How close below the visible area the button counts as seen, so rows arrive before they are needed.</summary>
    private const double Lead = 240;

    private sealed class Watch
    {
        public ScrollViewer? Scroller;
        public ScrollChangedEventHandler? OnScroll;
        public bool Pending;
    }

    private static readonly ConditionalWeakTable<ButtonBase, Watch> Watches = new();

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;

        button.Loaded -= OnLoaded;
        button.Unloaded -= OnUnloaded;
        button.IsVisibleChanged -= OnVisibleChanged;
        Detach(button);

        if (e.NewValue is not true) return;
        button.Loaded += OnLoaded;
        button.Unloaded += OnUnloaded;
        button.IsVisibleChanged += OnVisibleChanged;
        if (button.IsLoaded) Attach(button);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Attach((ButtonBase)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs e) => Detach((ButtonBase)sender);
    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Check((ButtonBase)sender);

    private static void Attach(ButtonBase button)
    {
        Detach(button);
        var scroller = FindAncestor<ScrollViewer>(button);
        if (scroller == null) return;

        var watch = Watches.GetOrCreateValue(button);
        watch.Scroller = scroller;
        watch.OnScroll = (_, _) => Check(button);
        scroller.ScrollChanged += watch.OnScroll;
        Check(button);
    }

    private static void Detach(ButtonBase button)
    {
        if (!Watches.TryGetValue(button, out var watch)) return;
        if (watch.Scroller != null && watch.OnScroll != null) watch.Scroller.ScrollChanged -= watch.OnScroll;
        watch.Scroller = null;
        watch.OnScroll = null;
    }

    private static void Check(ButtonBase button)
    {
        if (!Watches.TryGetValue(button, out var watch) || watch.Scroller is not { } scroller) return;
        if (watch.Pending || !button.IsVisible || button.Command == null) return;
        if (!Seen(button, scroller)) return;

        // After the layout pass that showed it, so the press sees settled rows.
        watch.Pending = true;
        button.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            watch.Pending = false;
            if (button.IsVisible && Seen(button, scroller) && button.Command?.CanExecute(button.CommandParameter) == true)
                button.Command.Execute(button.CommandParameter);
        });
    }

    private static bool Seen(FrameworkElement element, ScrollViewer scroller)
    {
        try
        {
            var bounds = element.TransformToAncestor(scroller).TransformBounds(new Rect(element.RenderSize));
            return bounds.Top < scroller.ActualHeight + Lead && bounds.Bottom > -Lead;
        }
        catch (InvalidOperationException)
        {
            // Not under that scroller any more: it was moved or is being torn down.
            return false;
        }
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(node); current != null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
}
