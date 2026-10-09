using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MikuDo.ViewModels;

namespace MikuDo.Views;

/// <summary>
/// The image viewer: a window of its own laid exactly over the app, the app
/// blurred and dimmed behind the image.
/// </summary>
/// <remarks>
/// It cannot be an element inside the main window. The task page's markdown
/// preview is a WebView2, which is a child HWND, and Windows paints a child
/// HWND over every WPF element in its window whatever their order: the task's
/// text would sit on top of the viewer and over its close button. A separate
/// window owned by the main one is above all of that.
///
/// The blur is of a picture of the app taken from the screen just before the
/// window opens. Only the screen has the preview in it; WPF cannot render a
/// child HWND into a bitmap.
/// </remarks>
public sealed class GalleryWindow : Window
{
    private const double BlurRadius = 22;
    private const double ScrimOpacity = 0.74;
    private static readonly Duration OpenDuration = TimeSpan.FromMilliseconds(170);

    private readonly Window _app;
    private readonly GalleryViewModel _gallery;
    private readonly GalleryOverlay _viewer;
    private readonly RECT _bounds;

    public GalleryWindow(Window app, GalleryViewModel gallery, double cornerRadius)
    {
        _app = app;
        _gallery = gallery;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Owner = app;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Image";
        SetResourceReference(BackgroundProperty, "CanvasBrush");

        GetWindowRect(new WindowInteropHelper(app).Handle, out _bounds);

        // A first guess in DIPs so the window opens in the right place; the
        // exact pixels are set once it has a handle.
        var fromDevice = PresentationSource.FromVisual(app)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new Point(_bounds.Left, _bounds.Top));
        var bottomRight = fromDevice.Transform(new Point(_bounds.Right, _bounds.Bottom));
        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(1, bottomRight.X - topLeft.X);
        Height = Math.Max(1, bottomRight.Y - topLeft.Y);

        var blur = new BlurEffect { Radius = BlurRadius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        var backdrop = new Image { Source = CaptureScreen(_bounds), Stretch = Stretch.Fill, Effect = blur };
        var scrim = new Rectangle { Opacity = ScrimOpacity };
        scrim.SetResourceReference(Shape.FillProperty, "GalleryScrimBrush");
        _viewer = new GalleryOverlay { DataContext = gallery };

        Content = new Grid { ClipToBounds = true, Children = { backdrop, scrim, _viewer } };

        SourceInitialized += (_, _) =>
        {
            SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, _bounds.Left, _bounds.Top,
                         _bounds.Right - _bounds.Left, _bounds.Bottom - _bounds.Top, SwpNoZOrder | SwpNoActivate);
        };
        if (app.WindowState != WindowState.Maximized) RoundedWindow.Apply(this, cornerRadius);

        // The app opens as it was, then blurs and dims under the image.
        if (SystemParameters.ClientAreaAnimation)
        {
            blur.Radius = 0;
            scrim.Opacity = 0;
            _viewer.Opacity = 0;
            Loaded += (_, _) =>
            {
                var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(BlurRadius, OpenDuration) { EasingFunction = ease });
                scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(ScrimOpacity, OpenDuration) { EasingFunction = ease });
                _viewer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, OpenDuration) { EasingFunction = ease });
            };
        }

        PreviewKeyDown += OnPreviewKeyDown;

        // The viewer covers the app exactly only while the app stays put, so
        // it closes when the app moves or changes size.
        _app.LocationChanged += OnAppMoved;
        _app.SizeChanged += OnAppMoved;
        _app.StateChanged += OnAppMoved;
        Closed += (_, _) =>
        {
            _app.LocationChanged -= OnAppMoved;
            _app.SizeChanged -= OnAppMoved;
            _app.StateChanged -= OnAppMoved;
        };
    }

    private void OnAppMoved(object? sender, EventArgs e) => _gallery.CloseCommand.Execute(null);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var control = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.Escape: _gallery.CloseCommand.Execute(null); break;
            case Key.Left: _gallery.PreviousCommand.Execute(null); break;
            case Key.Right: _gallery.NextCommand.Execute(null); break;
            case Key.S when control: _gallery.SaveCommand.Execute(null); break;
            case Key.OemPlus or Key.Add: _viewer.ZoomStep(zoomIn: true); break;
            case Key.OemMinus or Key.Subtract: _viewer.ZoomStep(zoomIn: false); break;
            case Key.D0 or Key.NumPad0: _viewer.ResetZoom(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>What the screen shows inside <paramref name="bounds"/>, or null if it cannot be read.</summary>
    private static BitmapSource? CaptureScreen(RECT bounds)
    {
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return null;

        try
        {
            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new System.Drawing.Size(width, height));

            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height),
                                       System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
            try
            {
                var shot = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null,
                                               data.Scan0, data.Stride * height, data.Stride);
                shot.Freeze();
                return shot;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch (Exception ex)
        {
            Services.LogService.Error("Could not picture the app behind the image viewer", ex);
            return null;
        }
    }

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
}
