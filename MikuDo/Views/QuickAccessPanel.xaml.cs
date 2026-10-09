using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MikuDo.ViewModels;

namespace MikuDo.Views;

/// <summary>The list that opens under the search bar. Its rows never take the caret, which stays in the bar.</summary>
public partial class QuickAccessPanel : UserControl
{
    public QuickAccessPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is QuickAccessViewModel old)
            {
                old.SelectedShown -= ShowSelected;
                old.RowsReplaced -= ToTop;
            }
            if (e.NewValue is QuickAccessViewModel model)
            {
                model.SelectedShown += ShowSelected;
                model.RowsReplaced += ToTop;
            }
        };
    }

    /// <summary>A new list starts at its top.</summary>
    private void ToTop()
    {
        List.ApplyTemplate();
        (List.Template.FindName("Scroller", List) as ScrollViewer)?.ScrollToTop();
    }

    /// <summary>Keeps the row picked out with the arrow keys on screen; the list builds it first if it was out of view.</summary>
    private void ShowSelected(QuickChoice choice) => List.ScrollIntoView(choice);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: QuickChoice choice })
            choice.Activate(Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: QuickItem item }) item.Assign?.Invoke();
    }
}
