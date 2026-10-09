using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MikuDo.ViewModels;

namespace MikuDo.Views;

/// <summary>
/// The find bar along the top of a note or a task. Enter goes to the next
/// match and Shift+Enter to the one before; Alt with C, W or R switches match
/// case, whole words and regex; Esc closes it.
/// </summary>
public partial class FindBar : UserControl
{
    public FindBar() => InitializeComponent();

    private FindViewModel? Find => DataContext as FindViewModel;

    /// <summary>Puts the caret in the box with what is in it selected, so typing replaces it.</summary>
    public void FocusBox()
    {
        // Once the bar has been laid out: it may only now be turning visible.
        Dispatcher.BeginInvoke(() =>
        {
            FindBox.Focus();
            Keyboard.Focus(FindBox);
            FindBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>True while the caret is in the bar.</summary>
    public bool HasFocus => IsKeyboardFocusWithin;

    private void FindBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Find is not { } find) return;

        // What is typed is searched a moment later; Enter searches it now, so it steps from where that lands.
        void Commit() => FindBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        var mods = Keyboard.Modifiers;
        if (e.Key == Key.Enter && mods is ModifierKeys.None or ModifierKeys.Shift)
        {
            Commit();
            if (mods == ModifierKeys.Shift) find.PreviousCommand.Execute(null);
            else find.NextCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && mods == ModifierKeys.None)
        {
            find.Close();
            e.Handled = true;
        }
        else if (e.Key == Key.System && mods == ModifierKeys.Alt)
        {
            switch (e.SystemKey)
            {
                case Key.C: find.MatchCase = !find.MatchCase; break;
                case Key.W: find.WholeWord = !find.WholeWord; break;
                case Key.R: find.UseRegex = !find.UseRegex; break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
