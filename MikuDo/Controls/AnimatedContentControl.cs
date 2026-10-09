using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MikuDo.Controls;

/// <summary>
/// A <see cref="ContentControl"/> that fades and lifts its content into place
/// whenever the content changes, so switching pages reads as a move rather
/// than a repaint.
/// </summary>
public class AnimatedContentControl : ContentControl
{
    private static readonly Duration Fade = new(TimeSpan.FromMilliseconds(70));
    private static readonly Duration Slide = new(TimeSpan.FromMilliseconds(120));

    private readonly TranslateTransform _slide = new();

    public AnimatedContentControl()
    {
        RenderTransform = _slide;
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (newContent == null) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Fade));
        _slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, Slide) { EasingFunction = ease });
    }
}
