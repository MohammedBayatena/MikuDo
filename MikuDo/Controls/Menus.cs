using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MikuDo.Controls;

/// <summary>
/// Right-click menus, built as they open from the same commands the buttons use.
/// </summary>
/// <remarks>
/// A menu declared inside a row's template cannot bind back to the page: a
/// context menu is not in the page's visual tree, so ancestor bindings find
/// nothing. Built in code, each row's menu holds the page's commands directly.
/// </remarks>
public static class Menus
{
    /// <summary>Stands for a line between groups. None is drawn first, last or twice in a row.</summary>
    public static readonly object Gap = new();

    /// <summary>A row that runs <paramref name="command"/>. A routed command shows its own shortcut unless <paramref name="gesture"/> names one.</summary>
    public static MenuItem Item(string header, ICommand command, object? parameter = null, string? gesture = null)
    {
        var item = new MenuItem { Header = header, Command = command, CommandParameter = parameter };
        if (gesture != null) item.InputGestureText = gesture;
        return item;
    }

    public static MenuItem Item(string header, Action click, string? gesture = null)
    {
        var item = new MenuItem { Header = header };
        if (gesture != null) item.InputGestureText = gesture;
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>A row that destroys something, in red.</summary>
    public static MenuItem Danger(MenuItem item)
    {
        item.SetResourceReference(Control.ForegroundProperty, "DangerBrush");
        return item;
    }

    /// <summary>A row with a tick beside it when <paramref name="isChecked"/>, room for one when not.</summary>
    public static MenuItem Check(string header, bool isChecked, ICommand command, object? parameter = null)
    {
        var tick = new Icon
        {
            Size = 11,
            StrokeThickness = 2.6,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = isChecked ? Visibility.Visible : Visibility.Hidden
        };
        tick.SetResourceReference(Icon.DataProperty, "IconCheck");
        tick.SetResourceReference(Icon.StrokeProperty, "AccentBrush");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(tick);
        row.Children.Add(new TextBlock { Text = header, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return new MenuItem { Header = row, Command = command, CommandParameter = parameter };
    }

    /// <summary>A row that opens <paramref name="items"/> beside it; null when there are none.</summary>
    public static MenuItem? Sub(string header, IEnumerable<object?> items)
    {
        var item = new MenuItem { Header = header };
        Fill(item.Items, items);
        return item.Items.Count == 0 ? null : item;
    }

    /// <summary>Opens a menu of <paramref name="items"/> at the pointer. Null items are left out.</summary>
    public static void Open(FrameworkElement target, params object?[] items)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
        Fill(menu.Items, items);
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    /// <summary>Opens a menu of <paramref name="items"/> under <paramref name="target"/>, for a button with a caret.</summary>
    public static void OpenBelow(FrameworkElement target, params object?[] items)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom, VerticalOffset = 2 };
        Fill(menu.Items, items);
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    /// <summary>Puts <paramref name="items"/> into a menu that opens by itself, in place of what it held.</summary>
    public static void Refill(ContextMenu menu, params object?[] items)
    {
        menu.Items.Clear();
        Fill(menu.Items, items);
    }

    private static void Fill(ItemCollection into, IEnumerable<object?> items)
    {
        foreach (var item in items)
        {
            if (item == null) continue;
            if (ReferenceEquals(item, Gap))
            {
                if (into.Count > 0 && into[into.Count - 1] is not Separator) into.Add(new Separator());
                continue;
            }
            into.Add(item);
        }

        if (into.Count > 0 && into[into.Count - 1] is Separator) into.RemoveAt(into.Count - 1);
    }
}
