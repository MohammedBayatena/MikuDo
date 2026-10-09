using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MikuDo.ViewModels;

namespace MikuDo.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as SettingsViewModel)?.Cancel();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SettingsViewModel old) old.SectionRequested -= ShowSection;
            if (e.NewValue is SettingsViewModel model) model.SectionRequested += ShowSection;
        };
    }

    /// <summary>Room left above a part of the page scrolled to, so its top edge is not hard against the header.</summary>
    private const double TopRoom = 12;

    /// <summary>
    /// Scrolls to a part of the page once it is laid out, and lights it up for
    /// a moment so the eye finds it. A shortcut waiting for keys is scrolled to
    /// instead, its own box showing that it waits.
    /// </summary>
    private void ShowSection(string section, ShortcutRow? recording)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (recording != null && SectionShortcuts.RowElement(recording) is { } row)
            {
                ScrollTo(row, Scroller.ViewportHeight / 3);
                return;
            }
            if (FindName(section) is not FrameworkElement target) return;
            ScrollTo(target, TopRoom);
            if (target is Border or Panel) Flash(target);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ScrollTo(FrameworkElement element, double room)
    {
        if (!element.IsVisible) return;
        var top = element.TransformToAncestor(Scroller).Transform(new Point(0, 0)).Y + Scroller.VerticalOffset;
        Scroller.ScrollToVerticalOffset(Math.Max(0, top - room));
    }

    /// <summary>The accent's tint over the part, fading out.</summary>
    private static void Flash(FrameworkElement target)
    {
        if (Application.Current.TryFindResource("AccentSoftBrush") is not SolidColorBrush soft) return;
        var tint = new SolidColorBrush(soft.Color);
        var property = target is Border ? Border.BackgroundProperty : Panel.BackgroundProperty;
        var before = target.ReadLocalValue(property);
        target.SetValue(property, tint);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1400))
        {
            BeginTime = TimeSpan.FromMilliseconds(500),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            if (!ReferenceEquals(target.GetValue(property), tint)) return;
            if (before == DependencyProperty.UnsetValue) target.ClearValue(property);
            else target.SetValue(property, before);
        };
        tint.BeginAnimation(Brush.OpacityProperty, fade);
    }
}
