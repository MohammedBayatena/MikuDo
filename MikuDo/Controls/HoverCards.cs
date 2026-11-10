using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace MikuDo.Controls;

/// <summary>
/// One preview card at a time, just under the square the pointer rests on.
/// </summary>
/// <remarks>
/// The card opens after a short rest and goes to the next square the pointer
/// stays on. A card that only tells things gives way the moment the pointer
/// comes onto it, so the squares it covers are never out of reach; one with
/// something to use on it (<see cref="Preview.HasActions"/>) stays while the
/// pointer is on it. It closes once the pointer has been off the square and
/// the card for a moment.
/// </remarks>
public sealed class HoverCards
{
    private static readonly TimeSpan OpenAfter = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan SwitchAfter = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CloseAfter = TimeSpan.FromMilliseconds(260);

    /// <summary>Room the card keeps around itself for its shadow, which the placement steps back over.</summary>
    private const double ShadowRoom = 8;

    /// <summary>Space between the square and the card's edge.</summary>
    private const double Gap = 4;

    private readonly Popup _popup;
    private readonly ContentControl _card;
    private readonly DispatcherTimer _opening;
    private readonly DispatcherTimer _closing;
    private FrameworkElement? _shown;
    private FrameworkElement? _waiting;

    public event Action? Opened;

    public HoverCards(Popup popup)
    {
        _popup = popup;
        _card = (ContentControl)popup.Child;
        _opening = new DispatcherTimer(DispatcherPriority.Input);
        _closing = new DispatcherTimer(DispatcherPriority.Input) { Interval = CloseAfter };
        _opening.Tick += (_, _) =>
        {
            _opening.Stop();
            if (_waiting is { IsMouseOver: true, IsVisible: true } square && !_card.IsMouseOver) Show(square);
        };
        _closing.Tick += (_, _) =>
        {
            _closing.Stop();
            if (!_card.IsMouseOver && !(_shown?.IsMouseOver ?? false)) Close();
        };
        _card.MouseEnter += (_, _) =>
        {
            _opening.Stop();
            _closing.Stop();
            if (_shown == null || !Preview.GetHasActions(_shown)) Close();
        };
        _card.MouseLeave += (_, _) => _closing.Start();
    }

    public bool IsOpen => _popup.IsOpen;

    /// <summary>The pointer came onto <paramref name="square"/>, which has a card to show.</summary>
    public void Enter(FrameworkElement square)
    {
        if (Preview.GetTemplate(square) == null || square.DataContext == null) return;
        _waiting = square;
        _opening.Stop();
        if (ReferenceEquals(square, _shown) && _popup.IsOpen)
        {
            _closing.Stop();
            return;
        }
        _opening.Interval = _popup.IsOpen ? SwitchAfter : OpenAfter;
        _opening.Start();
    }

    public void Leave(FrameworkElement square)
    {
        if (ReferenceEquals(square, _waiting)) _opening.Stop();
        if (_popup.IsOpen) _closing.Start();
    }

    /// <summary>Closes the card unless the pointer is on it: a click anywhere else means the user has moved on.</summary>
    public void Dismiss()
    {
        if (!_card.IsMouseOver) Close();
    }

    public void Close()
    {
        _opening.Stop();
        _closing.Stop();
        _popup.IsOpen = false;
        _shown = null;
    }

    private void Show(FrameworkElement square)
    {
        _closing.Stop();
        if (_popup.IsOpen && !ReferenceEquals(square, _shown)) _popup.IsOpen = false;

        var scale = Display.Current.AppScale;
        _card.ContentTemplate = Preview.GetTemplate(square);
        _card.Content = square.DataContext;
        _popup.PlacementTarget = square;
        _popup.Placement = PlacementMode.Bottom;
        _popup.HorizontalOffset = -ShadowRoom * scale;
        _popup.VerticalOffset = (Gap - ShadowRoom) * scale;
        _shown = square;
        _popup.IsOpen = true;
        Opened?.Invoke();
    }
}
