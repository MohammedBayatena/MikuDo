using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MikuDo.Services;

namespace MikuDo.Controls;

/// <summary>
/// Plays an animated GIF from a file, filling its box the way a photo fills a
/// frame: scaled to cover it and centred. A still image shows as it is.
/// </summary>
/// <remarks>
/// Built to cost next to nothing while it sits in a corner of the window:
/// <list type="bullet">
/// <item>Every frame is decoded once, off the UI thread, straight to the size
/// it is shown at. A large GIF neither keeps its full-size frames in memory
/// nor has the renderer scale them again on every frame.</item>
/// <item>Playing a frame copies it into the one bitmap this element draws, so
/// only this element's few pixels are redrawn.</item>
/// <item>The timer runs only while the animation can be seen: it stops while
/// the element is hidden, the window is minimised or in the background, or
/// Windows is set to show no animations.</item>
/// <item>The frames are held to a fixed memory budget; a GIF too long for it
/// keeps every second frame, or third, with the timing of the whole kept.</item>
/// </list>
/// </remarks>
public class GifPlayer : FrameworkElement
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(GifPlayer),
        new PropertyMetadata(null, (d, _) => ((GifPlayer)d).Reload()));

    /// <summary>The GIF, or any still image, to show. Null shows nothing.</summary>
    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    /// <summary>The most the decoded frames may take.</summary>
    private const long FrameBudget = 24L * 1024 * 1024;

    private sealed record Frames(int Width, int Height, byte[][] Pixels, int[] Delays);

    private Frames? _frames;
    private WriteableBitmap? _bitmap;
    private int _index;
    private int _version;
    private (int Width, int Height) _decodedFor;
    private Window? _window;
    private readonly DispatcherTimer _timer = new();

    public GifPlayer()
    {
        _timer.Tick += OnTick;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateRunning();
        SizeChanged += (_, _) =>
        {
            if (TargetPixels() != _decodedFor) Reload();
        };
    }

    /// <summary>Takes whatever box it is given; the frames are made to fit it.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap != null) dc.DrawImage(_bitmap, new Rect(RenderSize));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window != null)
        {
            _window.Activated += OnWindowChanged;
            _window.Deactivated += OnWindowChanged;
            _window.StateChanged += OnWindowChanged;
        }
        UpdateRunning();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        if (_window == null) return;
        _window.Activated -= OnWindowChanged;
        _window.Deactivated -= OnWindowChanged;
        _window.StateChanged -= OnWindowChanged;
        _window = null;
    }

    private void OnWindowChanged(object? sender, EventArgs e) => UpdateRunning();

    /// <summary>The box in device pixels, which is the size the frames are made at.</summary>
    private (int Width, int Height) TargetPixels()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return (0, 0);
        var dpi = VisualTreeHelper.GetDpi(this);
        return ((int)Math.Round(ActualWidth * dpi.DpiScaleX), (int)Math.Round(ActualHeight * dpi.DpiScaleY));
    }

    /// <summary>
    /// Decodes on a worker thread and hands the frames back through this
    /// element's dispatcher. An await would resume on whatever context the
    /// caller had, and before the message loop starts there is none, so the
    /// frames would land on the worker thread.
    /// </summary>
    private void Reload()
    {
        var version = ++_version;
        _timer.Stop();

        var path = FilePath;
        var target = TargetPixels();
        _decodedFor = target;
        if (string.IsNullOrEmpty(path) || target.Width == 0)
        {
            Show(null);
            return;
        }

        Task.Run(() =>
        {
            Frames? frames = null;
            try
            {
                frames = Decode(path, target.Width, target.Height);
            }
            catch (Exception ex)
            {
                LogService.Error($"Could not play {Path.GetFileName(path)}", ex);
            }

            // A newer file or size may have been asked for while this one decoded.
            Dispatcher.BeginInvoke(() =>
            {
                if (version == _version) Show(frames);
            });
        });
    }

    private void Show(Frames? frames)
    {
        _frames = frames;
        _index = 0;
        if (frames == null)
        {
            _bitmap = null;
        }
        else
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _bitmap = new WriteableBitmap(frames.Width, frames.Height,
                                          96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32, null);
            Paint(0);
        }
        InvalidateVisual();
        UpdateRunning();
    }

    private void Paint(int index)
    {
        var frames = _frames!;
        _bitmap!.WritePixels(new Int32Rect(0, 0, frames.Width, frames.Height), frames.Pixels[index], frames.Width * 4, 0);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_frames == null || _bitmap == null)
        {
            _timer.Stop();
            return;
        }

        _index = (_index + 1) % _frames.Pixels.Length;
        Paint(_index);
        _timer.Interval = TimeSpan.FromMilliseconds(_frames.Delays[_index]);
    }

    private void UpdateRunning()
    {
        var run = _frames is { Pixels.Length: > 1 }
                  && IsVisible
                  && SystemParameters.ClientAreaAnimation
                  && _window is { IsActive: true, WindowState: not WindowState.Minimized };

        if (run == _timer.IsEnabled) return;
        if (run)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(_frames!.Delays[_index]);
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    // ── Decoding ────────────────────────────────────────────────

    /// <summary>
    /// Builds every frame as it looks on screen and cuts each to cover a
    /// <paramref name="width"/> × <paramref name="height"/> box, centred.
    /// </summary>
    /// <remarks>
    /// A GIF frame is only the part of the picture that changed, placed at an
    /// offset, and says what becomes of it before the next one: kept, cleared,
    /// or put back as it was. So the frames are laid onto one canvas in order,
    /// and the canvas is what gets kept. A box larger than the picture keeps
    /// the picture's own pixels, and drawing scales them up.
    /// </remarks>
    private static Frames Decode(string path, int width, int height)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream,
            BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);

        var first = decoder.Frames[0];
        var gif = decoder is GifBitmapDecoder;
        var canvasWidth = gif ? Query(ContainerMetadata(decoder), "/logscrdesc/Width", first.PixelWidth) : first.PixelWidth;
        var canvasHeight = gif ? Query(ContainerMetadata(decoder), "/logscrdesc/Height", first.PixelHeight) : first.PixelHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0) (canvasWidth, canvasHeight) = (first.PixelWidth, first.PixelHeight);

        // The part of the picture that covers the box, and the scale it is kept at.
        var cover = Math.Max((double)width / canvasWidth, (double)height / canvasHeight);
        var cropWidth = Math.Clamp((int)Math.Round(width / cover), 1, canvasWidth);
        var cropHeight = Math.Clamp((int)Math.Round(height / cover), 1, canvasHeight);
        var crop = new Int32Rect((canvasWidth - cropWidth) / 2, (canvasHeight - cropHeight) / 2, cropWidth, cropHeight);
        var scale = Math.Min(1.0, cover);
        var outWidth = Math.Max(1, (int)Math.Round(cropWidth * scale));
        var outHeight = Math.Max(1, (int)Math.Round(cropHeight * scale));

        var count = decoder.Frames.Count;
        var frameBytes = (long)outWidth * outHeight * 4;
        var step = (int)Math.Ceiling(count / (double)Math.Max(1, FrameBudget / frameBytes));

        var canvas = new byte[canvasWidth * canvasHeight * 4];
        byte[]? saved = null;
        var pixels = new List<byte[]>();
        var delays = new List<int>();
        var previousDisposal = 0;
        var previousRect = Int32Rect.Empty;

        for (var i = 0; i < count; i++)
        {
            var frame = decoder.Frames[i];
            var meta = FrameMetadata(frame);
            var left = gif ? Query(meta, "/imgdesc/Left", 0) : 0;
            var top = gif ? Query(meta, "/imgdesc/Top", 0) : 0;
            var disposal = gif ? Query(meta, "/grctlext/Disposal", 0) : 0;

            // Browsers play a delay under 20 ms as 100 ms, and GIFs are made to match.
            var delay = gif ? Query(meta, "/grctlext/Delay", 10) * 10 : 100;
            if (delay < 20) delay = 100;

            // What the previous frame asked to happen once it had been shown.
            if (previousDisposal == 2) Clear(canvas, canvasWidth, canvasHeight, previousRect);
            else if (previousDisposal == 3 && saved != null) Buffer.BlockCopy(saved, 0, canvas, 0, canvas.Length);
            if (disposal == 3)
            {
                saved ??= new byte[canvas.Length];
                Buffer.BlockCopy(canvas, 0, saved, 0, canvas.Length);
            }

            var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            var frameWidth = converted.PixelWidth;
            var frameHeight = converted.PixelHeight;
            var framePixels = new byte[frameWidth * frameHeight * 4];
            converted.CopyPixels(framePixels, frameWidth * 4, 0);
            Blend(framePixels, frameWidth, frameHeight, canvas, canvasWidth, canvasHeight, left, top);

            previousDisposal = disposal;
            previousRect = new Int32Rect(left, top, frameWidth, frameHeight);

            if (i % step != 0)
            {
                delays[^1] += delay;
                continue;
            }

            pixels.Add(Shoot(canvas, canvasWidth, canvasHeight, crop, scale, outWidth, outHeight));
            delays.Add(delay);
        }

        return new Frames(outWidth, outHeight, pixels.ToArray(), delays.ToArray());
    }

    private static byte[] Shoot(byte[] canvas, int canvasWidth, int canvasHeight, Int32Rect crop,
                                double scale, int outWidth, int outHeight)
    {
        BitmapSource picture = BitmapSource.Create(canvasWidth, canvasHeight, 96, 96, PixelFormats.Pbgra32, null,
                                                   canvas, canvasWidth * 4);
        picture = new CroppedBitmap(picture, crop);
        if (scale < 1)
        {
            picture = new TransformedBitmap(picture,
                new ScaleTransform(outWidth / (double)crop.Width, outHeight / (double)crop.Height));
        }

        var shot = new byte[outWidth * outHeight * 4];
        var copyWidth = Math.Min(outWidth, picture.PixelWidth);
        var copyHeight = Math.Min(outHeight, picture.PixelHeight);
        picture.CopyPixels(new Int32Rect(0, 0, copyWidth, copyHeight), shot, outWidth * 4, 0);
        return shot;
    }

    /// <summary>Lays premultiplied <paramref name="source"/> over the canvas at its offset, clipped to the canvas.</summary>
    private static void Blend(byte[] source, int sourceWidth, int sourceHeight,
                              byte[] canvas, int canvasWidth, int canvasHeight, int left, int top)
    {
        for (var y = 0; y < sourceHeight; y++)
        {
            var cy = top + y;
            if (cy < 0 || cy >= canvasHeight) continue;
            for (var x = 0; x < sourceWidth; x++)
            {
                var cx = left + x;
                if (cx < 0 || cx >= canvasWidth) continue;

                var s = (y * sourceWidth + x) * 4;
                var alpha = source[s + 3];
                if (alpha == 0) continue;

                var d = (cy * canvasWidth + cx) * 4;
                if (alpha == 255)
                {
                    canvas[d] = source[s];
                    canvas[d + 1] = source[s + 1];
                    canvas[d + 2] = source[s + 2];
                    canvas[d + 3] = 255;
                    continue;
                }

                var keep = 255 - alpha;
                for (var c = 0; c < 4; c++)
                    canvas[d + c] = (byte)(source[s + c] + (canvas[d + c] * keep + 127) / 255);
            }
        }
    }

    private static void Clear(byte[] canvas, int canvasWidth, int canvasHeight, Int32Rect rect)
    {
        for (var y = Math.Max(0, rect.Y); y < Math.Min(canvasHeight, rect.Y + rect.Height); y++)
        {
            var from = Math.Max(0, rect.X);
            var to = Math.Min(canvasWidth, rect.X + rect.Width);
            if (to > from) Array.Clear(canvas, (y * canvasWidth + from) * 4, (to - from) * 4);
        }
    }

    private static BitmapMetadata? ContainerMetadata(BitmapDecoder decoder)
    {
        try { return decoder.Metadata; }
        catch (NotSupportedException) { return null; }
    }

    private static BitmapMetadata? FrameMetadata(BitmapFrame frame)
    {
        try { return frame.Metadata as BitmapMetadata; }
        catch (NotSupportedException) { return null; }
    }

    private static int Query(BitmapMetadata? metadata, string query, int fallback)
    {
        try
        {
            return metadata?.GetQuery(query) is { } value ? Convert.ToInt32(value) : fallback;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidCastException
                                       or FormatException or OverflowException)
        {
            return fallback;
        }
    }
}
