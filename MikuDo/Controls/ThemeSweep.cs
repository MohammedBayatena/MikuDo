using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MikuDo.Controls;

/// <summary>
/// The change from one theme to the next, drawn as a wave. Each window is
/// photographed in the theme it is leaving, the new theme goes on underneath the
/// photograph, and the photograph is wiped away in a circle that grows from
/// the pointer, its edge soft and lit in the new accent colour.
/// </summary>
/// <remarks>
/// <para>
/// The photograph hides the costly part: swapping the theme re-reads every
/// colour in the app and rebuilds the board, and the first frame after it is
/// slow to draw. All of that happens behind a still picture, and the wave
/// starts only once the new frame is up, so the wave itself has nothing to do
/// but redraw one picture through a moving mask.
/// </para>
/// <para>
/// A note's preview is a browser window of its own, drawn over anything WPF
/// draws, so it changes when its page is laid out again rather than with the wave.
/// Windows set to show no animations get the new theme at once.
/// </para>
/// </remarks>
public static class ThemeSweep
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(760);

    private static readonly List<Wave> Running = new();

    /// <summary>Runs <paramref name="swap"/>, which puts the new theme on, behind a wave across <paramref name="windows"/>.</summary>
    public static void Run(IEnumerable<Window> windows, Action swap)
    {
        foreach (var wave in Running.ToList()) wave.Finish();

        var waves = SystemParameters.ClientAreaAnimation
            ? windows.Where(w => w is { IsVisible: true } && w.WindowState != WindowState.Minimized)
                     .Select(Photograph).OfType<Wave>().ToList()
            : new List<Wave>();

        swap();
        if (waves.Count == 0) return;

        var accent = Application.Current.TryFindResource("AccentBrush") is SolidColorBrush brush ? brush.Color : Colors.White;
        foreach (var wave in waves) wave.Cover();

        // The wave waits for the first frame in the new theme, drawn under the photograph.
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var wave in waves) wave.Start(accent);
        }, DispatcherPriority.ContextIdle);
    }

    /// <summary>A still of the window as it looks now, ready to lie over it; null when it cannot be taken.</summary>
    private static Wave? Photograph(Window window)
    {
        if (window.Content is not FrameworkElement root || root.ActualWidth <= 0 || root.ActualHeight <= 0) return null;
        if (AdornerLayer.GetAdornerLayer(root) is not { } layer) return null;

        try
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
            if (width <= 0 || height <= 0) return null;

            var whole = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            whole.Render(window);

            // Only the content is covered, so only the content is kept: a window with a frame of its own draws that too.
            var area = root.TransformToAncestor(window).TransformBounds(new Rect(root.RenderSize));
            var cut = new Int32Rect((int)Math.Round(area.X * dpi.DpiScaleX), (int)Math.Round(area.Y * dpi.DpiScaleY),
                                    (int)Math.Round(area.Width * dpi.DpiScaleX), (int)Math.Round(area.Height * dpi.DpiScaleY));
            cut.Width = Math.Min(cut.Width, width - cut.X);
            cut.Height = Math.Min(cut.Height, height - cut.Y);
            if (cut.X < 0 || cut.Y < 0 || cut.Width <= 0 || cut.Height <= 0) return null;

            var still = new CroppedBitmap(whole, cut);
            still.Freeze();
            return new Wave(root, layer, still, Origin(root));
        }
        catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or COMException)
        {
            Services.LogService.Error("The theme's wave could not photograph a window", ex);
            return null;
        }
    }

    /// <summary>
    /// Where the wave starts in <paramref name="root"/>: the pointer, or the
    /// nearest point to it inside the window, so a wave set off with the
    /// pointer on another screen still crosses this one. The middle when the
    /// pointer cannot be found.
    /// </summary>
    private static Point Origin(FrameworkElement root)
    {
        if (GetCursorPos(out var screen))
        {
            try
            {
                var at = root.PointFromScreen(new Point(screen.X, screen.Y));
                return new Point(Math.Clamp(at.X, 0, root.ActualWidth), Math.Clamp(at.Y, 0, root.ActualHeight));
            }
            catch (InvalidOperationException) { }
        }
        return new Point(root.ActualWidth / 2, root.ActualHeight / 2);
    }

    /// <summary>One window's wave: the photograph over it, and the masks that wipe it away.</summary>
    private sealed class Wave : Adorner
    {
        /// <summary>Inside this share of the radius the new theme shows fully; out to the edge the photograph fades in.</summary>
        private const double ClearShare = 0.8;

        /// <summary>The wave's radius before it starts: a dot, so the photograph covers everything else.</summary>
        private const double Seed = 0.5;

        private readonly AdornerLayer _layer;
        private readonly BitmapSource _still;
        private readonly Point _origin;
        private readonly RadialGradientBrush _mask;
        private RadialGradientBrush? _glow;
        private bool _finished;

        public Wave(FrameworkElement root, AdornerLayer layer, BitmapSource still, Point origin) : base(root)
        {
            _layer = layer;
            _still = still;
            _origin = origin;
            IsHitTestVisible = false;

            _mask = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = origin,
                GradientOrigin = origin,
                RadiusX = Seed,
                RadiusY = Seed,
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Colors.Transparent, ClearShare),
                    new GradientStop(Colors.Black, 1)
                }
            };
        }

        /// <summary>The photograph goes over the window, covering it whole while the theme changes underneath.</summary>
        public void Cover()
        {
            _layer.Add(this);
            Running.Add(this);
        }

        public void Start(Color accent)
        {
            if (_finished) return;

            _glow = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = _origin,
                GradientOrigin = _origin,
                RadiusX = Seed,
                RadiusY = Seed,
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Colors.Transparent, ClearShare - 0.1),
                    new GradientStop(Color.FromArgb(70, accent.R, accent.G, accent.B), ClearShare + 0.07),
                    new GradientStop(Colors.Transparent, 0.99)
                }
            };
            InvalidateVisual();

            // Far enough that the photograph's last soft edge has left the farthest corner.
            var size = AdornedElement.RenderSize;
            var farthest = new[] { new Point(0, 0), new Point(size.Width, 0), new Point(0, size.Height), new Point(size.Width, size.Height) }
                .Max(corner => (corner - _origin).Length);
            var reach = farthest / ClearShare + 2;

            var grow = new DoubleAnimation(Seed, reach, Duration)
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            grow.Completed += (_, _) => Finish();
            foreach (var brush in new[] { _mask, _glow })
            {
                brush.BeginAnimation(RadialGradientBrush.RadiusXProperty, grow);
                brush.BeginAnimation(RadialGradientBrush.RadiusYProperty, grow);
            }
        }

        public void Finish()
        {
            if (_finished) return;
            _finished = true;
            Running.Remove(this);
            _layer.Remove(this);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(AdornedElement.RenderSize);
            dc.PushOpacityMask(_mask);
            dc.DrawImage(_still, bounds);
            dc.Pop();
            if (_glow != null) dc.DrawRectangle(_glow, null, bounds);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
