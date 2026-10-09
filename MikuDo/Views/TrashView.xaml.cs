using System.Windows;
using System.Windows.Controls;
using MikuDo.Controls;
using MikuDo.ViewModels;

namespace MikuDo.Views;

public partial class TrashView : UserControl
{
    public TrashView() => InitializeComponent();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
        => (DataContext as TrashViewModel)?.ToggleSelectAllCommand.Execute(null);

    private void Row_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not TrashViewModel trash || sender is not FrameworkElement { DataContext: TrashRow row } element) return;
        e.Handled = true;
        Menus.Open(element,
            Menus.Item("Restore", trash.RestoreCommand, row),
            Menus.Gap,
            Menus.Danger(Menus.Item("Delete forever", trash.DeleteForeverCommand, row)));
    }
}
