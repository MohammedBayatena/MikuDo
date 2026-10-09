using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MikuDo.Controls;
using MikuDo.ViewModels;

namespace MikuDo.Views;

public partial class VaultView : UserControl
{
    public VaultView()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private VaultViewModel? Vm => DataContext as VaultViewModel;

    private void Unlock_Click(object sender, RoutedEventArgs e) => Submit();

    private void Row_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Vm is not { } vault || sender is not FrameworkElement { DataContext: VaultRow row } element) return;
        e.Handled = true;
        Menus.Open(element,
            Menus.Item("Open", vault.OpenItemCommand, row),
            Menus.Gap,
            Menus.Danger(Menus.Item("Delete", vault.DeleteItemCommand, row)));
    }

    private void Password_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Submit();
        e.Handled = true;
    }

    private void Submit()
    {
        if (Vm == null) return;

        Vm.Submit(PasswordBox.Password, ConfirmBox.Password);
        if (Vm.IsUnlocked)
        {
            PasswordBox.Clear();
            ConfirmBox.Clear();
        }
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: VaultRow row }) return;
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
        Vm?.OpenItemCommand.Execute(row);
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
