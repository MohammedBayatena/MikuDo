using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MikuDo.Controls;

/// <summary>
/// Turns the wheel's stepped jumps into one continuous glide.
/// </summary>
/// <remarks>
/// The wheel normally moves a <see cref="ScrollViewer"/> three lines at a time
/// with no travel between them, which reads as stutter.
///
/// A notch does not start an animation of its own — it only moves a target,
/// and a single render-time loop eases every active viewer towards its target
/// each frame. That distinction is the whole point: per-notch animations
/// restart one another while the wheel is still turning, so a fast scroll
/// becomes a run of clipped, re-eased hops. One integrator instead produces a
/// single motion that simply speeds up as notches arrive.
/// </remarks>
public static class SmoothScroll
{
    /// <summary>Pixels the target moves per wheel notch.</summary>
    private const double Step = 110;

    /// <summary>
    /// How hard the offset is pulled towards the target, per second. Higher is
    /// snappier and closer to the pointer; lower drifts.
    /// </summary>
    private const double Rate = 24;

    /// <summary>Closer than this and the glide is over.</summary>
    private const double Settled = 0.4;

    /// <summary>Where the wheel has asked this viewer to end up.</summary>
    private static readonly DependencyProperty TargetOffsetProperty =
        DependencyProperty.RegisterAttached("TargetOffset", typeof(double), typeof(SmoothScroll),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SmoothScroll),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    /// <summary>The viewers still gliding. Empty means the loop is unhooked.</summary>
    private static readonly List<ScrollViewer> Gliding = new();

    private static TimeSpan _lastFrame;

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer) return;

        viewer.PreviewMouseWheel -= OnWheel;
        if (e.NewValue is true) viewer.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;

        // Nothing to scroll: leave the wheel alone so an outer viewer gets it.
        if (viewer.ScrollableHeight <= 0) return;

        // A modifier means zoom, or horizontal scrolling handled elsewhere.
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        // Mid-glide the target is the truth; otherwise anything else that moved
        // the viewer — the scrollbar, the keyboard, new content — is.
        var from = Gliding.Contains(viewer)
            ? (double)viewer.GetValue(TargetOffsetProperty)
            : viewer.VerticalOffset;

        var target = Math.Clamp(from - e.Delta / 120.0 * Step, 0, viewer.ScrollableHeight);

        // Already at that end: hand the wheel to the parent instead of eating it.
        if (Math.Abs(target - viewer.VerticalOffset) < Settled) return;

        viewer.SetValue(TargetOffsetProperty, target);
        e.Handled = true;

        if (Gliding.Contains(viewer)) return;

        Gliding.Add(viewer);
        if (Gliding.Count == 1)
        {
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;

        // The first frame has no interval to measure, and a frame after a stall
        // would otherwise jump the whole way at once.
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : (args.RenderingTime - _lastFrame).TotalSeconds;
        _lastFrame = args.RenderingTime;
        dt = Math.Clamp(dt, 1 / 240.0, 1 / 20.0);

        // Framerate-independent exponential approach.
        var k = 1 - Math.Exp(-Rate * dt);

        for (var i = Gliding.Count - 1; i >= 0; i--)
        {
            var viewer = Gliding[i];

            // The content shrank out from under the glide.
            if (viewer.ScrollableHeight <= 0)
            {
                Gliding.RemoveAt(i);
                continue;
            }

            var target = Math.Clamp((double)viewer.GetValue(TargetOffsetProperty), 0, viewer.ScrollableHeight);
            var current = viewer.VerticalOffset;

            if (Math.Abs(target - current) < Settled)
            {
                viewer.ScrollToVerticalOffset(target);
                Gliding.RemoveAt(i);
                continue;
            }

            viewer.ScrollToVerticalOffset(current + (target - current) * k);
        }

        if (Gliding.Count == 0) CompositionTarget.Rendering -= OnRendering;
    }
}
