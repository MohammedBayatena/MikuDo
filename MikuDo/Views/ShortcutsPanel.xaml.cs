using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MikuDo.Models;
using MikuDo.ViewModels;

namespace MikuDo.Views;

/// <summary>
/// Settings → Keyboard shortcuts. While a row waits for keys its box holds
/// the caret and takes every key itself, so none of them runs a command.
/// </summary>
public partial class ShortcutsPanel : UserControl
{
    public ShortcutsPanel() => InitializeComponent();

    private ShortcutsViewModel? Vm => DataContext as ShortcutsViewModel;

    /// <summary>The row of <paramref name="row"/>; null while the section is folded or the row is not shown.</summary>
    public FrameworkElement? RowElement(ShortcutRow row)
        => Rows() is { } rows ? rows.ItemContainerGenerator.ContainerFromItem(row) as FrameworkElement : null;

    /// <summary>The list of rows, which exists only while the section is open.</summary>
    private ItemsControl? Rows()
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is ItemsControl { Name: "Rows" } rows) return rows;
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                pending.Push(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    // ── Search ──────────────────────────────────────────────────

    /// <summary>
    /// Keys with Ctrl or Alt, or a function key, look for the command that
    /// has them. The keys a text box edits with stay with the box.
    /// </summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var chord = KeyChord.From(e);
        if (KeyChord.IsModifierKey(chord.Key) || Vm == null) return;
        var function = chord.Key is >= Key.F1 and <= Key.F24;
        if (!chord.HasCtrl && !chord.HasAlt && !function) return;
        if (IsEditingKey(chord)) return;

        Vm.SearchByKeys(chord);
        if (sender is TextBox box) box.CaretIndex = box.Text.Length;
        e.Handled = true;
    }

    private static bool IsEditingKey(KeyChord chord)
        => chord.HasCtrl && !chord.HasAlt
           && chord.Key is Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y
                        or Key.Left or Key.Right or Key.Home or Key.End or Key.Back or Key.Delete or Key.Insert;

    // ── Waiting for keys ────────────────────────────────────────

    /// <summary>The box takes the caret as soon as it shows, and comes into view.</summary>
    private void Capture_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement box) return;
        Dispatcher.BeginInvoke(() =>
        {
            Keyboard.Focus(box);
            ((box.DataContext is ShortcutRow row ? RowElement(row) : null) ?? box).BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Capture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: ShortcutRow row }) row.Press(KeyChord.From(e));
    }

    /// <summary>
    /// Letting go of a modifier before any key shows what is still held.
    /// Taking the key up here also keeps a lone Alt from opening the window menu.
    /// </summary>
    private void Capture_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: ShortcutRow row })
            row.Release(new KeyChord(Key.None, Keyboard.Modifiers));
    }

    /// <summary>Clicking anywhere else gives up waiting; Save and Cancel never take the caret, so they still work.</summary>
    private void Capture_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShortcutRow { IsRecording: true } row } && IsActiveWindow())
            row.Cancel();
    }

    private bool IsActiveWindow() => Window.GetWindow(this)?.IsActive == true;
}
