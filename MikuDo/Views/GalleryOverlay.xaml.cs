using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MikuDo.ViewModels;

namespace MikuDo.Views;

/// <summary>The image viewer's content: the image, its zoom and pan, and the buttons around it.</summary>
public partial class GalleryOverlay : UserControl
{
    private const double MaxZoom = 8;
    private const double WheelStep = 1.2;

    /// <summary>How far a double-click zooms in on a fitted image.</summary>
    private const double DoubleClickZoom = 2.5;

    private bool _panning;
    private Point _panStart;
    private Point _panOrigin;

    public GalleryOverlay()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnGalleryChanged;
            if (e.NewValue is INotifyPropertyChanged now) now.PropertyChanged += OnGalleryChanged;
            ResetZoom();
        };
    }

    private GalleryViewModel? Gallery => DataContext as GalleryViewModel;

    private bool IsZoomed => Zoom.ScaleX > 1;

    /// <summary>Each image opens fitted, whatever the last one was zoomed to.</summary>
    private void OnGalleryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GalleryViewModel.Current)) ResetZoom();
    }

    /// <summary>A press on bare backdrop, away from the image and the buttons, closes the viewer.</summary>
    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop) || ReferenceEquals(e.OriginalSource, Stage))
            Gallery?.CloseCommand.Execute(null);
    }

    // ── Zoom and pan ────────────────────────────────────────────

    public void ResetZoom()
    {
        _panning = false;
        Zoom.ScaleX = Zoom.ScaleY = 1;
        Pan.X = Pan.Y = 0;
        Picture.Cursor = null;
    }

    /// <summary>Zooms by <paramref name="factor"/> keeping <paramref name="anchor"/>, a point on the image, where it is.</summary>
    private void ZoomBy(double factor, Point anchor)
    {
        var scale = Math.Clamp(Zoom.ScaleX * factor, 1, MaxZoom);
        if (scale <= 1)
        {
            ResetZoom();
            return;
        }

        // A point p on the image shows at p·scale + pan, so moving pan by
        // p·(old − new) keeps it in place.
        Pan.X += anchor.X * (Zoom.ScaleX - scale);
        Pan.Y += anchor.Y * (Zoom.ScaleY - scale);
        Zoom.ScaleX = Zoom.ScaleY = scale;
        Picture.Cursor = Cursors.SizeAll;
    }

    /// <summary>Zooms in or out about the middle of the image, for the keyboard.</summary>
    public void ZoomStep(bool zoomIn)
    {
        if (Gallery?.HasImage != true) return;
        var middle = new Point(Picture.ActualWidth / 2, Picture.ActualHeight / 2);
        ZoomBy(zoomIn ? WheelStep : 1 / WheelStep, middle);
    }

    private void Stage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Gallery?.HasImage != true) return;
        ZoomBy(e.Delta > 0 ? WheelStep : 1 / WheelStep, e.GetPosition(Picture));
        e.Handled = true;
    }

    private void Picture_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (IsZoomed) ResetZoom();
            else ZoomBy(DoubleClickZoom, e.GetPosition(Picture));
            e.Handled = true;
            return;
        }

        if (!IsZoomed) return;
        _panning = true;
        _panStart = e.GetPosition(Stage);
        _panOrigin = new Point(Pan.X, Pan.Y);
        Picture.CaptureMouse();
        e.Handled = true;
    }

    private void Picture_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var now = e.GetPosition(Stage);
        Pan.X = _panOrigin.X + now.X - _panStart.X;
        Pan.Y = _panOrigin.Y + now.Y - _panStart.Y;
    }

    private void Picture_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        Picture.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Picture_LostMouseCapture(object sender, MouseEventArgs e) => _panning = false;
}
