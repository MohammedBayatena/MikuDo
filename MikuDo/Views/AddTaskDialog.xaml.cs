using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MikuDo.ViewModels;

namespace MikuDo.Views;

public partial class AddTaskDialog : UserControl
{
    public AddTaskDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void LabelInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        (DataContext as AddTaskViewModel)?.AddCustomLabelCommand.Execute(null);
        e.Handled = true;
    }

    private void SubtaskCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskOption option })
            (DataContext as AddTaskViewModel)?.LinkSubtaskCommand.Execute(option);
    }
}
