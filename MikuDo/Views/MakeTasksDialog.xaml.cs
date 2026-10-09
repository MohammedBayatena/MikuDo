using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Controls;
using MikuDo.ViewModels;

namespace MikuDo.Views;

public partial class MakeTasksDialog : UserControl
{
    public MakeTasksDialog() => InitializeComponent();

    private MakeTasksViewModel? Vm => DataContext as MakeTasksViewModel;

    private void Workspace_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        Menus.OpenBelow(WorkspaceField, vm.Workspaces
            .Select(w => (object?)Menus.Check(w.Name, ReferenceEquals(w, vm.Workspace), new RelayCommand(() => vm.Workspace = w)))
            .ToArray());
    }

    private void List_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        Menus.OpenBelow(ListField, vm.Lists
            .Select(l => (object?)Menus.Check(l, l == vm.List, new RelayCommand(() => vm.List = l)))
            .ToArray());
    }

    private void Label_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        Menus.OpenBelow(LabelField, vm.Labels
            .Select(l => (object?)Menus.Check(l, l == vm.Label, new RelayCommand(() => vm.Label = l)))
            .ToArray());
    }
}
