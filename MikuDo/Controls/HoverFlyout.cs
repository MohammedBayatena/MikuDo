using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace MikuDo.Controls;

/// <summary>
/// A popup that opens just under an element while the pointer rests on it, and
/// stays while the pointer is on either of them. Crossing the gap between them
/// never closes it: it shuts only once the pointer has been off both for a
/// moment.
/// </summary>
public sealed class HoverFlyout
{
    private static readonly TimeSpan OpenAfter = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan CloseAfter = TimeSpan.FromMilliseconds(320);

    /// <summary>Space between the element and the popup's edge.</summary>
    private const double Gap = 4;

    private readonly FrameworkElement _anchor;
    private readonly Popup _popup;
    private readonly double _shadowRoom;
    private readonly DispatcherTimer _opening;
    private readonly DispatcherTimer _closing;

    /// <summary>The pointer came onto the element: whatever else is showing can make way.</summary>
    public event Action? Entered;

    public event Action? Opened;

    /// <summary>
    /// <paramref name="shadowRoom"/> is the room the popup's content keeps
    /// around itself for its shadow, which the placement steps back over so
    /// the popup's edge sits under the element's.
    /// </summary>
    public HoverFlyout(FrameworkElement anchor, Popup popup, double shadowRoom)
    {
        _anchor = anchor;
        _popup = popup;
        _shadowRoom = shadowRoom;
        _opening = new DispatcherTimer(DispatcherPriority.Input) { Interval = OpenAfter };
        _closing = new DispatcherTimer(DispatcherPriority.Input) { Interval = CloseAfter };
        _opening.Tick += (_, _) =>
        {
            _opening.Stop();
            if (!_anchor.IsVisible || !_anchor.IsMouseOver) return;
            Place();
            _popup.IsOpen = true;
            Opened?.Invoke();
        };
        _closing.Tick += (_, _) =>
        {
            _closing.Stop();
            if (!_anchor.IsMouseOver && !(_popup.Child?.IsMouseOver ?? false)) _popup.IsOpen = false;
        };

        anchor.MouseEnter += (_, _) =>
        {
            _closing.Stop();
            Entered?.Invoke();
            if (!_popup.IsOpen) _opening.Start();
        };
        anchor.MouseLeave += (_, _) => Leave();
        anchor.IsVisibleChanged += (_, _) => { if (!_anchor.IsVisible) Close(); };
        if (popup.Child is { } child)
        {
            child.MouseEnter += (_, _) => _closing.Stop();
            child.MouseLeave += (_, _) => Leave();
        }
    }

    public bool IsOpen => _popup.IsOpen;

    /// <summary>Under the element, its left edge on the element's.</summary>
    public void Place()
    {
        var scale = Display.Current.AppScale;
        _popup.PlacementTarget = _anchor;
        _popup.Placement = PlacementMode.Bottom;
        _popup.HorizontalOffset = -_shadowRoom * scale;
        _popup.VerticalOffset = (Gap - _shadowRoom) * scale;
    }

    private void Leave()
    {
        _opening.Stop();
        if (_popup.IsOpen) _closing.Start();
    }

    public void Close()
    {
        _opening.Stop();
        _closing.Stop();
        _popup.IsOpen = false;
    }
}
