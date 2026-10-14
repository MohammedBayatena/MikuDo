using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MikuDo.Services;

/// <summary>Modal confirm / notice styled like the rest of the app.</summary>
public static class DialogService
{
    /// <summary>Asks before something that cannot easily be taken back: the yes button is the red one.</summary>
    public static bool Confirm(string message, string title = "Confirm") => Show(message, title, isConfirm: true);

    /// <summary>As <see cref="Confirm(string, string)"/>, with the buttons saying what they do.</summary>
    public static bool Confirm(string message, string title, string yes, string no)
        => Show(message, title, isConfirm: true, yes, no);

    public static void Notify(string message, string title = "MikuDo") => Show(message, title, isConfirm: false);

    /// <summary>
    /// A plain question with two named answers. Neither is destructive, so the
    /// yes button is the accent one. Enter answers yes and Escape no.
    /// </summary>
    public static bool Ask(string message, string title, string yes, string no)
        => Show(message, title, isConfirm: true, yes, no, destructive: false);

    /// <summary>
    /// Asks what to do with changes not yet saved: Save (Enter), Don't save,
    /// or Cancel (Escape), which stays put.
    /// </summary>
    public static SaveChoice AskToSave(string message, string title = "Unsaved changes")
        => Present(message, title, isConfirm: true, "Save", "Cancel", destructive: false, alternate: "Don't save").Answer switch
        {
            Answer.Yes => SaveChoice.Save,
            Answer.Alternate => SaveChoice.Discard,
            _ => SaveChoice.Cancel
        };

    /// <summary>
    /// A question with two named answers besides Cancel: <paramref name="yes"/>
    /// (Enter), and <paramref name="alternate"/> apart on the left. Escape
    /// cancels. A tick box under the question, worded <paramref name="remember"/>,
    /// lets the answer stand from now on; <c>Remember</c> says whether it was ticked.
    /// </summary>
    public static (ThreeWay Choice, bool Remember) Choose(string message, string title, string yes, string alternate, string remember)
    {
        var (answer, remembered) = Present(message, title, isConfirm: true, yes, "Cancel", destructive: false, alternate, remember);
        var choice = answer switch
        {
            Answer.Yes => ThreeWay.Yes,
            Answer.Alternate => ThreeWay.Alternate,
            _ => ThreeWay.Cancel
        };
        return (choice, remembered && choice != ThreeWay.Cancel);
    }

    private enum Answer { No, Yes, Alternate }

    private static bool Show(string message, string title, bool isConfirm,
                             string yes = "Confirm", string no = "Cancel", bool destructive = true)
        => Present(message, title, isConfirm, yes, no, destructive, alternate: null).Answer == Answer.Yes;

    private static (Answer Answer, bool Remember) Present(string message, string title, bool isConfirm,
                                                          string yes, string no, bool destructive, string? alternate,
                                                          string? remember = null)
    {
        var result = Answer.No;
        var scale = Controls.Display.Current.AppScale;

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.Height,
            // The card inside grows with the app's size; the 18 around it is the room its shadow takes.
            Width = 36 + 384 * scale,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false
        };

        // Without an owner the dialog is a sibling window: it can fall behind the
        // app while still blocking it, which looks like a freeze. The window in
        // front owns it, so a question asked from the image lightbox opens over it.
        dialog.Owner = Application.Current.Windows.OfType<Window>()
            .Where(w => w != dialog && w.IsLoaded && w.IsVisible)
            .OrderByDescending(w => w.IsActive)
            .FirstOrDefault();

        var stack = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = Font(),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Res("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontFamily = Font(),
            FontSize = 12.5,
            LineHeight = 20,
            Foreground = Res("TextSubtleBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, remember == null ? 18 : 12)
        });

        CheckBox? rememberBox = null;
        if (remember != null)
        {
            rememberBox = new CheckBox
            {
                Style = (Style)Application.Current.FindResource("TickBox"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var tick = rememberBox;
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 18)
            };
            line.Children.Add(tick);
            line.Children.Add(new TextBlock
            {
                Text = remember,
                FontFamily = Font(),
                FontSize = 12,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("TextSecondaryBrush")
            });
            // The words tick the box too; the box itself takes its own clicks.
            line.MouseLeftButtonUp += (_, _) => tick.IsChecked = tick.IsChecked != true;
            stack.Children.Add(line);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        // The third answer sits apart on the left, so it is never hit by habit.
        var row = new DockPanel { LastChildFill = false };
        if (alternate != null)
        {
            var other = new Button
            {
                Content = alternate,
                Style = (Style)Application.Current.FindResource("GhostButton")
            };
            other.Click += (_, _) => { result = Answer.Alternate; dialog.Close(); };
            DockPanel.SetDock(other, Dock.Left);
            row.Children.Add(other);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);

        if (isConfirm)
        {
            var cancel = new Button
            {
                Content = no,
                Style = (Style)Application.Current.FindResource("GhostButton"),
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancel.Click += (_, _) => { result = Answer.No; dialog.Close(); };
            buttons.Children.Add(cancel);
        }

        var confirm = new Button
        {
            Content = isConfirm ? yes : "OK",
            Style = (Style)Application.Current.FindResource(isConfirm && destructive ? "DangerButton" : "PrimaryButton")
        };
        confirm.Click += (_, _) => { result = Answer.Yes; dialog.Close(); };
        buttons.Children.Add(confirm);

        stack.Children.Add(row);

        dialog.Content = new Border
        {
            LayoutTransform = Controls.Display.Current.AppTransform,
            Margin = new Thickness(18),
            CornerRadius = new CornerRadius(14),
            Background = Res("SurfaceBrush"),
            BorderBrush = Res("BorderSoftBrush"),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 40,
                ShadowDepth = 14,
                Direction = 270,
                Opacity = 0.22,
                Color = (Color)Application.Current.FindResource("ShadowColor")
            },
            Child = stack
        };

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { result = Answer.No; dialog.Close(); }
            else if (e.Key == System.Windows.Input.Key.Enter) { result = Answer.Yes; dialog.Close(); }
        };

        dialog.ShowDialog();
        dialog.Owner?.Activate();
        return (result, rememberBox?.IsChecked == true);
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    private static FontFamily Font() => (FontFamily)Application.Current.FindResource("AppFont");
}

/// <summary>What to do with changes not yet saved.</summary>
public enum SaveChoice { Save, Discard, Cancel }

/// <summary>The answer to a question with two named answers besides Cancel.</summary>
public enum ThreeWay { Yes, Alternate, Cancel }
