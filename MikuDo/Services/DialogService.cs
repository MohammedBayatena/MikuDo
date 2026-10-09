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
        => Present(message, title, isConfirm: true, "Save", "Cancel", destructive: false, alternate: "Don't save") switch
        {
            Answer.Yes => SaveChoice.Save,
            Answer.Alternate => SaveChoice.Discard,
            _ => SaveChoice.Cancel
        };

    private enum Answer { No, Yes, Alternate }

    private static bool Show(string message, string title, bool isConfirm,
                             string yes = "Confirm", string no = "Cancel", bool destructive = true)
        => Present(message, title, isConfirm, yes, no, destructive, alternate: null) == Answer.Yes;

    private static Answer Present(string message, string title, bool isConfirm,
                                  string yes, string no, bool destructive, string? alternate)
    {
        var result = Answer.No;

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.Height,
            Width = 420,
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
            Margin = new Thickness(0, 0, 0, 18)
        });

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
        return result;
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    private static FontFamily Font() => (FontFamily)Application.Current.FindResource("AppFont");
}

/// <summary>What to do with changes not yet saved.</summary>
public enum SaveChoice { Save, Discard, Cancel }
