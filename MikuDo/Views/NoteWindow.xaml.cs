using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using MikuDo.Controls;
using MikuDo.Services;
using MikuDo.ViewModels;

namespace MikuDo.Views;

/// <summary>
/// A note read in a window of its own: the page alone, with a bar to edit it,
/// move it into MikuDo or make tasks of it. Open with on a Markdown file opens
/// one, and so does the pop-out button by a note in the sidebar.
/// </summary>
public partial class NoteWindow : Window
{
    private static readonly List<NoteWindow> Shown = new();

    /// <summary>The reader windows open now.</summary>
    public static IReadOnlyList<NoteWindow> All => Shown;

    /// <summary>A new reader sits this far down and right of the last one, so neither hides the other.</summary>
    private const double CascadeStep = 28;

    private readonly MainViewModel _main;

    /// <summary>Set once the note has gone to the Notes page, so closing leaves it open there.</summary>
    private bool _handedOver;

    /// <summary>Set when the app has already asked about unsaved changes and is closing every reader.</summary>
    private bool _closingWithApp;

    public NoteViewModel Note { get; private set; }

    private NoteWindow(MainViewModel main, NoteViewModel note)
    {
        InitializeComponent();
        MaximizeFix.Apply(this);
        RoundedWindow.Apply(this, CornerRadius);

        _main = main;
        Note = note;
        Attach(note);
        Topmost = App.Database.GetSetting("ReaderOnTop") == "True";

        Closing += OnClosing;
        Closed += OnClosed;
        StateChanged += (_, _) => SyncEdge();
        Activated += (_, _) => SyncEdge();
        Deactivated += (_, _) => SyncEdge();
        PreviewKeyDown += OnPreviewKeyDown;
        KeyDown += OnKeyDown;
    }

    /// <summary>The corner radius of the window, its edge, and the content clipped inside it.</summary>
    private const double CornerRadius = 10;

    /// <summary>
    /// The edge is drawn strongest while the window has focus, as Windows draws
    /// its own. A maximised window fills the screen and needs no edge or corners.
    /// </summary>
    private void SyncEdge()
    {
        var maximized = WindowState == WindowState.Maximized;
        Edge.BorderThickness = new Thickness(maximized ? 0 : 1);
        Edge.CornerRadius = new CornerRadius(maximized ? 0 : CornerRadius);
        RoundedClip.SetRadius(Body, maximized ? 0 : CornerRadius - 1);
        Edge.SetResourceReference(Border.BorderBrushProperty, IsActive ? "AccentBorderStrongBrush" : "AccentBorderBrush");
        MaxIcon.Data = (System.Windows.Media.Geometry)FindResource(maximized ? "IconRestoreWindow" : "IconMaximize");
    }

    /// <summary>Opens a note in a reader window, or brings forward the one already showing it.</summary>
    public static NoteWindow? Open(MainViewModel main, string path)
    {
        try { path = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }

        if (Find(path) is { } open)
        {
            open.BringForward();
            return open;
        }
        if (!File.Exists(path))
        {
            DialogService.Notify($"{Path.GetFileName(path)} is no longer at\n{Path.GetDirectoryName(path)}", "Note not found");
            return null;
        }

        NoteViewModel note;
        try
        {
            note = new NoteViewModel(main, path);
        }
        catch (Exception ex)
        {
            DialogService.Notify($"{Path.GetFileName(path)} could not be opened:\n{ex.Message}", "Open note");
            return null;
        }
        main.RememberNotePath(path);
        main.RememberOpened(MainViewModel.NoteKind, path);
        return Adopt(main, note);
    }

    /// <summary>Shows a note already open, unsaved changes and all, in a reader window of its own.</summary>
    public static NoteWindow Adopt(MainViewModel main, NoteViewModel note)
    {
        var window = new NoteWindow(main, note);
        window.Place();
        Shown.Add(window);
        window.Show();
        window.BringForward();
        return window;
    }

    /// <summary>The reader showing <paramref name="path"/>, if one is.</summary>
    public static NoteWindow? Find(string path)
        => Shown.FirstOrDefault(w => MainViewModel.SamePath(w.Note.Path, path));

    /// <summary>
    /// Before the app closes: asks about each reader's unsaved changes, then
    /// closes them all. False when one is answered Cancel, and nothing closes.
    /// </summary>
    public static bool CloseAllForExit()
    {
        foreach (var window in Shown.ToList())
        {
            if (window.Note.ConfirmLeave()) continue;
            window.BringForward();
            return false;
        }
        foreach (var window in Shown.ToList())
        {
            window._closingWithApp = true;
            window.Close();
        }
        return true;
    }

    /// <summary>The first reader centred on the screen; each one after it a step down and right of the last.</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 40);
        Height = Math.Min(Height, area.Height - 40);

        if (Shown.LastOrDefault(w => w.WindowState == WindowState.Normal) is { } last)
        {
            Left = last.Left + CascadeStep;
            Top = last.Top + CascadeStep;
        }
        else
        {
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }
        if (Left + Width > area.Right) Left = area.Left + 20;
        if (Top + Height > area.Bottom) Top = area.Top + 20;
    }

    /// <summary>Restores the window if it was minimised, as it was, and brings it to the front.</summary>
    public void BringForward()
    {
        if (WindowState == WindowState.Minimized) ShowWindow(new WindowInteropHelper(this).Handle, 9);
        Activate();
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    private void Attach(NoteViewModel note)
    {
        note.IsReader = true;
        note.NoteLinkFollowed += FollowLink;
        DataContext = note;
    }

    private void Detach(NoteViewModel note) => note.NoteLinkFollowed -= FollowLink;

    /// <summary>A link to another note opens it here, in place of this one.</summary>
    private void FollowLink(string path)
    {
        if (Shown.FirstOrDefault(w => w != this && MainViewModel.SamePath(w.Note.Path, path)) is { } other)
        {
            other.BringForward();
            return;
        }
        if (!File.Exists(path) || !Note.ConfirmLeave()) return;

        NoteViewModel next;
        try
        {
            next = new NoteViewModel(_main, Path.GetFullPath(path));
        }
        catch (Exception ex)
        {
            DialogService.Notify($"{Path.GetFileName(path)} could not be opened:\n{ex.Message}", "Open note");
            return;
        }
        Detach(Note);
        Note.Close();
        Note = next;
        Attach(next);
        _main.RememberNotePath(next.Path);
    }

    // ── The reading bar ─────────────────────────────────────────

    /// <summary>Moves the note onto the Notes page, unsaved changes and all, and closes this window.</summary>
    private void OpenInMikuDo_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = Application.Current.MainWindow;
        if (mainWindow is { IsVisible: false }) mainWindow.Show();

        var note = Note;
        Detach(note);
        if (!_main.ShowNote(note))
        {
            Attach(note);
            return;
        }
        _handedOver = true;
        Close();
        if (mainWindow != null) App.BringForward(mainWindow);
    }

    private void MakeTasks_Click(object sender, RoutedEventArgs e)
    {
        Menus.OpenBelow(MakeTasksButton,
            Menus.Item(Note.HasOpenItems ? $"From the checklist ({Note.OpenItems} open)…" : "From the checklist…",
                       Note.MakeTasksCommand, "items"),
            Menus.Item("The whole note as one task…", Note.MakeTasksCommand, "whole"));
    }

    private void OnTop_Click(object sender, RoutedEventArgs e)
        => App.Database.SaveSetting("ReaderOnTop", Topmost ? "True" : "False");

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Note.ReaderDialog != null)
        {
            Note.CloseDialog();
            e.Handled = true;
        }
    }

    /// <summary>The reader's commands, on the keys the user set for them: Save, closing the window, and find.</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (Services.Shortcuts.Is("app.save", e))
        {
            Note.SaveCommand.Execute(null);
            e.Handled = true;
        }
        else if (Services.Shortcuts.Is("window.closereader", e))
        {
            Close();
            e.Handled = true;
        }
        else if (Services.Shortcuts.Is("find.open", e)) e.Handled = Page.OpenFind();
        else if (Services.Shortcuts.Is("find.next", e)) e.Handled = Page.FindStep(1);
        else if (Services.Shortcuts.Is("find.previous", e)) e.Handled = Page.FindStep(-1);
    }

    // ── Window chrome ───────────────────────────────────────────

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Closing ─────────────────────────────────────────────────

    /// <summary>Unsaved changes ask Save, Don't save or Cancel; Cancel keeps the window open.</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_handedOver || _closingWithApp) return;
        if (!Note.ConfirmLeave()) e.Cancel = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Shown.Remove(this);
        Detach(Note);
        // The page lets go of the note before its preview goes with the window.
        DataContext = null;
        if (!_handedOver) Note.Close();

        // Opened from Explorer with the main window never shown, the readers are
        // the whole app: when the last one closes, so does MikuDo.
        if (Shown.Count == 0 && Application.Current?.MainWindow is { IsVisible: false })
            Application.Current.Shutdown();
    }
}
