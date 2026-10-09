using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MikuDo.Models;
using MikuDo.ViewModels;

namespace MikuDo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MaximizeFix.Apply(this);
        RoundedWindow.Apply(this, CornerRadius);

        // WPF drops a Maximized WindowState set before Show() when it also has to
        // centre the window, so it is applied once the handle exists — still
        // before the first paint, so there is no flash.
        SourceInitialized += (_, _) => WindowState = WindowState.Maximized;

        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.AppWindow = this;
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.Gallery)) SyncGallery(vm.Gallery);
                };
                WatchQuickAccess(vm.QuickAccess);
            }
            SyncMaximizedChrome();
        };
        Closing += OnClosing;

        // Markdown files dropped anywhere on the window open as notes, and
        // folders open under Notes. A drop something inside takes for itself,
        // such as a card, never gets here.
        AllowDrop = true;
        DragOver += Window_DragOver;
        Drop += Window_Drop;
        StateChanged += (_, _) => SyncMaximizedChrome();
        KeyDown += OnKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        Deactivated += (_, _) => Quick?.Close();
        // The popup stays where it opened, so moving the window closes it.
        LocationChanged += (_, _) => Quick?.Close();
        SizeChanged += (_, _) => SizeQuickAccess();
    }

    private bool _taskPageClosed;
    private bool _noteChecked;
    private bool _readersClosed;

    // ── Notes ───────────────────────────────────────────────────

    private void NotesAdd_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) Controls.Menus.OpenBelow(NotesAddButton, NotesAddMenu(vm));
    }

    private static object?[] NotesAddMenu(MainViewModel vm) => new object?[]
    {
        Controls.Menus.Item("Open file…", vm.OpenNoteFileCommand, null, Services.Shortcuts.GestureText("note.open")),
        Controls.Menus.Item("Open folder…", vm.OpenNoteFolderCommand),
        Controls.Menus.Gap,
        Controls.Menus.Item("New note…", vm.NewNoteCommand)
    };

    private void NoteRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement element) return;
        e.Handled = true;
        if (NoteRowMenu(vm, element.DataContext) is { } items) Controls.Menus.Open(element, items);
    }

    /// <summary>The menu for a row under NOTES, which depends on what the row is.</summary>
    private static object?[]? NoteRowMenu(MainViewModel vm, object? row) => row switch
    {
        RecentGroupRow => new object?[]
        {
            Controls.Menus.Item("Clear recent", vm.ClearRecentNotesCommand)
        },
        NoteRow { Folder: null } recent => new object?[]
        {
            Controls.Menus.Item("Open", vm.OpenNoteRowCommand, recent),
            Controls.Menus.Item("Open in new window", vm.OpenNoteInWindowCommand, recent),
            Controls.Menus.Item("Show in Explorer", vm.ShowNoteInFolderCommand, recent),
            Controls.Menus.Gap,
            Controls.Menus.Item("Remove from Recent", vm.ForgetNoteCommand, recent)
        },
        NoteRow note => new object?[]
        {
            Controls.Menus.Item("Open", vm.OpenNoteRowCommand, note),
            Controls.Menus.Item("Open in new window", vm.OpenNoteInWindowCommand, note),
            Controls.Menus.Item("Show in Explorer", vm.ShowNoteInFolderCommand, note),
            Controls.Menus.Gap,
            note.IsHiddenItem
                ? Controls.Menus.Item("Show in tree", vm.ShowInNoteTreeCommand, note)
                : Controls.Menus.Item("Hide from tree", vm.HideInNoteTreeCommand, note)
        },
        FolderRow { IsRoot: true } root => new object?[]
        {
            Controls.Menus.Item("New note here…", vm.NewNoteInCommand, root.Path),
            Controls.Menus.Item("Show in Explorer", vm.ShowNoteFolderInExplorerCommand, root.Path),
            Controls.Menus.Item("Collapse all", vm.CollapseNoteFolderCommand, root),
            Controls.Menus.Gap,
            Controls.Menus.Danger(Controls.Menus.Item("Remove from Notes", vm.RemoveNoteFolderCommand, root))
        },
        FolderRow folder => new object?[]
        {
            Controls.Menus.Item("New note here…", vm.NewNoteInCommand, folder.Path),
            Controls.Menus.Item("Show in Explorer", vm.ShowNoteFolderInExplorerCommand, folder.Path),
            Controls.Menus.Item("Collapse all", vm.CollapseNoteFolderCommand, folder),
            Controls.Menus.Gap,
            folder.IsHiddenItem
                ? Controls.Menus.Item("Show in tree", vm.ShowInNoteTreeCommand, folder)
                : Controls.Menus.Item("Hide from tree", vm.HideInNoteTreeCommand, folder)
        },
        HiddenGroupRow hidden => new object?[]
        {
            Controls.Menus.Item("Show all in tree", vm.ShowAllInNoteTreeCommand, hidden)
        },
        _ => null
    };

    /// <summary>A note opens; a group or folder opens or shuts; Show more shows the next batch.</summary>
    private void NoteTreeRow_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement { DataContext: SidebarNoteRow row }) return;
        if (row is NoteRow note) vm.OpenNoteRowCommand.Execute(note);
        else if (row is MoreNotesRow more) vm.ShowMoreNotesCommand.Execute(more);
        else vm.ToggleNoteRowCommand.Execute(row);
    }

    /// <summary>The button on a note's row opens it in a reader window; the row itself is not clicked.</summary>
    private void NoteRowPopOut_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainViewModel vm && sender is FrameworkElement { DataContext: NoteRow row })
            vm.OpenNoteInWindowCommand.Execute(row);
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainViewModel vm) vm.ClearRecentNotesCommand.Execute(null);
    }

    private void WorkspaceHeader_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement element) return;
        e.Handled = true;
        Controls.Menus.Open(element,
            Controls.Menus.Check("Show as a list", vm.IsWorkspacesOpen && !vm.IsWorkspacesCompact, vm.ShowAllWorkspacesCommand),
            Controls.Menus.Check("Show as squares", vm.IsWorkspacesOpen && vm.IsWorkspacesCompact,
                                 new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                                 {
                                     vm.IsWorkspacesOpen = true;
                                     vm.IsWorkspacesCompact = true;
                                 })),
            Controls.Menus.Gap,
            Controls.Menus.Item(vm.IsWorkspacesOpen ? "Fold away" : "Show", vm.ToggleWorkspacesSectionCommand));
    }

    // ── Sidebar width ───────────────────────────────────────────

    /// <summary>Room the page keeps however wide the sidebar is dragged.</summary>
    private const double PageRoom = 560;

    private void SidebarEdge_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var widest = Math.Max(MainViewModel.MinSidebarWidth, Math.Min(MainViewModel.MaxSidebarWidth, AppCard.ActualWidth - PageRoom));
        vm.SidebarWidth = Math.Clamp(vm.SidebarWidth + e.HorizontalChange, MainViewModel.MinSidebarWidth, widest);
    }

    private void SidebarEdge_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => (DataContext as MainViewModel)?.SaveSidebarWidth();

    private void SidebarEdge_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.SidebarWidth = MainViewModel.DefaultSidebarWidth;
        vm.SaveSidebarWidth();
        e.Handled = true;
    }

    private void EmptyFolderNewNote_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is FrameworkContentElement { DataContext: EmptyFolderRow row })
            vm.NewNoteInCommand.Execute(row.Folder);
    }

    /// <summary>Dropped Markdown files, and dropped folders, which open under Notes.</summary>
    private static (string[] Notes, string[] Folders) Dropped(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return (Array.Empty<string>(), Array.Empty<string>());
        return (paths.Where(Services.NoteFiles.IsNote).ToArray(), paths.Where(System.IO.Directory.Exists).ToArray());
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Handled) return;
        var (notes, folders) = Dropped(e);
        if (notes.Length == 0 && folders.Length == 0) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>Each dropped note joins the sidebar and the first opens; each dropped folder opens under Notes.</summary>
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Handled || DataContext is not MainViewModel vm) return;
        var (notes, folders) = Dropped(e);
        if (notes.Length == 0 && folders.Length == 0) return;
        e.Handled = true;
        foreach (var folder in folders) vm.AddNoteFolder(folder);
        if (notes.Length == 0) return;
        foreach (var path in notes.Skip(1).Reverse()) vm.RememberNotePath(path);
        vm.OpenNote(notes[0]);
    }

    /// <summary>
    /// Holds the close until an open task page has saved. The page may first
    /// have to wait for a recording to finish, so the close is cancelled, the
    /// page exits, and the window closes again once it has.
    /// </summary>
    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // A note with changes not saved to its file asks first; Cancel keeps the app open.
        if (!_noteChecked && DataContext is MainViewModel { OpenNotePage: { } note })
        {
            if (!note.ConfirmLeave())
            {
                e.Cancel = true;
                return;
            }
            _noteChecked = true;
        }

        // So does each reader window; then they close with the app.
        if (!_readersClosed)
        {
            if (!NoteWindow.CloseAllForExit())
            {
                e.Cancel = true;
                return;
            }
            _readersClosed = true;
        }

        if (_taskPageClosed || (DataContext as MainViewModel)?.OpenTaskPage is not { } page) return;

        e.Cancel = true;
        try
        {
            await page.ExitAsync(DetailExit.Close);
        }
        catch (Exception ex)
        {
            Services.LogService.Error("Saving the open task while closing failed", ex);
        }

        _taskPageClosed = true;

        // When the page had nothing to wait for, this is still inside the
        // Closing event, where Close throws; the second close waits until
        // the first has finished.
        _ = Dispatcher.InvokeAsync(Close);
    }

    // ── Image viewer ────────────────────────────────────────────

    private GalleryWindow? _galleryWindow;

    /// <summary>Opens the image viewer over the app while there is a gallery to show, and closes it after.</summary>
    private void SyncGallery(GalleryViewModel? gallery)
    {
        if (ReferenceEquals(_galleryWindow?.DataContext, gallery) && gallery != null) return;

        var open = _galleryWindow;
        _galleryWindow = null;
        open?.Close();
        if (gallery == null) return;

        var window = new GalleryWindow(this, gallery, CornerRadius) { DataContext = gallery };
        window.Closed += (_, _) =>
        {
            // Closed some other way than through the gallery, such as Alt+F4.
            if (ReferenceEquals(_galleryWindow, window)) _galleryWindow = null;
            if (DataContext is MainViewModel { Gallery: { } shown } vm && ReferenceEquals(shown, gallery))
                vm.CloseGalleryCommand.Execute(null);
            Activate();
        };
        _galleryWindow = window;
        window.Show();
    }

    // ── Keyboard shortcuts ──────────────────────────────────────

    /// <summary>
    /// A key nothing on the page used runs the command the user set it to.
    /// This listens after the page has had its turn, so a text box keeps the
    /// keys it types and edits with, and the editors their own Ctrl B, I and E.
    /// </summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not MainViewModel vm) return;
        var chord = Models.KeyChord.From(e);
        if (Models.KeyChord.IsModifierKey(chord.Key)) return;
        if (vm.RunShortcut(chord)) e.Handled = true;
    }

    // ── Quick Access ────────────────────────────────────────────

    private QuickAccessViewModel? Quick => (DataContext as MainViewModel)?.QuickAccess;

    /// <summary>Widths of the bar: closed, and open over its list. A narrow window keeps room either side.</summary>
    private const double SearchWidth = 500;
    private const double QuickWidth = 640;
    private const double SearchSideRoom = 300;

    private void WatchQuickAccess(QuickAccessViewModel quick)
    {
        quick.FocusRequested += () =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        };
        // The bar lets go of the caret for good: coming back to the window must not land in it and open the list again.
        quick.Closed += () =>
        {
            if (SearchBox.IsKeyboardFocusWithin || ReferenceEquals(FocusManager.GetFocusedElement(this), SearchBox)) LeaveSearchBox();
        };
        quick.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuickAccessViewModel.IsOpen))
            {
                SizeQuickAccess();
                // The bar widens first, so the popup places itself under the bar as it will be.
                if (quick.IsOpen) UpdateLayout();
                QuickPopup.IsOpen = quick.IsOpen;
            }
            // A prefix typed into the bar is taken off it as the mode changes; the box follows.
            if (e.PropertyName == nameof(QuickAccessViewModel.Query) && SearchBox.Text != quick.Query)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (SearchBox.Text == quick.Query) return;
                    SearchBox.Text = quick.Query;
                    SearchBox.CaretIndex = SearchBox.Text.Length;
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        };
        SizeQuickAccess();
    }

    private void SizeQuickAccess()
    {
        var room = Math.Max(320, ActualWidth - SearchSideRoom);
        var open = Quick?.IsOpen == true;
        SearchBar.Width = Math.Min(open ? QuickWidth : SearchWidth, room);
        SearchBar.Height = open ? 32 : 30;
        SearchBar.Margin = new Thickness(0, open ? 6 : 7, 0, 0);
        QuickPanel.Width = Math.Min(QuickWidth, room);
        QuickPanel.MaxHeight = Math.Max(200, ActualHeight - 44 - 24);
    }

    /// <summary>The list's own keys. Everything else goes to the bar's text, or on to the window.</summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Quick is not { } quick) return;
        if (!quick.IsOpen)
        {
            if (e.Key is Key.Down or Key.Enter) quick.Open(QuickMode.Everything);
            else if (e.Key == Key.Escape) LeaveSearchBox();
            else return;
            e.Handled = true;
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Down: quick.Move(1); break;
            case Key.Up: quick.Move(-1); break;
            case Key.PageDown: quick.Move(8); break;
            case Key.PageUp: quick.Move(-8); break;
            case Key.Enter: quick.ActivateSelected(control); break;
            case Key.Tab: quick.NextMode(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); break;
            case Key.Escape: quick.Close(); break;
            case Key.Back when quick.Back(): break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// The caret leaves the bar for the window itself. Keys still need somewhere
    /// to go: with nothing focused at all, the window's shortcuts would not run.
    /// </summary>
    private void LeaveSearchBox()
    {
        FocusManager.SetFocusedElement(this, this);
        Keyboard.Focus(this);
    }

    private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Quick is { IsOpen: false } quick) quick.Open(QuickMode.Everything);
    }

    /// <summary>The caret went somewhere else in the window: the list goes with it.</summary>
    private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is DependencyObject next && (QuickPanel.IsAncestorOf(next) || SearchBar.IsAncestorOf(next))) return;
        Quick?.Close();
    }

    /// <summary>A click anywhere but the bar and its list closes the list, and still does what it was for.</summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Quick is not { IsOpen: true } quick || e.OriginalSource is not DependencyObject source) return;
        if (IsWithin(source, QuickPanel) || IsWithin(source, SearchBar)) return;
        quick.Close();
    }

    private static bool IsWithin(DependencyObject node, DependencyObject container)
    {
        for (var at = node; at != null; at = at is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                 ? System.Windows.Media.VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, container)) return true;
        }
        return false;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e) => Quick?.Clear();

    // ── Window chrome ───────────────────────────────────────────

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Corner radius of the window and of the card it holds.</summary>
    private const double CornerRadius = 10;

    /// <summary>Height of the title bar: brand, search and the window buttons.</summary>
    private const double TitleBarHeight = 44;

    /// <summary>
    /// The card keeps its rounded corners in both states; only the canvas band
    /// around it tightens when the window is maximised.
    /// </summary>
    private void SyncMaximizedChrome()
    {
        var maximized = WindowState == WindowState.Maximized;
        var band = maximized ? 6 : 8;

        // The shadow caster is a separate element, so it tracks the card.
        AppCard.Margin = AppCardShadow.Margin = new Thickness(band, TitleBarHeight, band, band);
        Controls.RoundedClip.SetRadius(AppCardClip, CornerRadius);

        MaxIcon.Data = (System.Windows.Media.Geometry)FindResource(
            maximized ? "IconRestoreWindow" : "IconMaximize");
    }

    // ── Workspace rows ──────────────────────────────────────────

    private void WorkspaceInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            vm.ConfirmAddWorkspaceCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelAddWorkspaceCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void DeleteWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceNavItem item })
            (DataContext as MainViewModel)?.DeleteWorkspaceCommand.Execute(item);
    }

    // ── Renaming a workspace ────────────────────────────────────

    private void RenameWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceNavItem item })
            (DataContext as MainViewModel)?.StartRenameWorkspaceCommand.Execute(item);
        e.Handled = true;
    }

    /// <summary>
    /// Double-clicking a row renames it. A double click that lands on one of
    /// the row's own small buttons belongs to that button instead.
    /// </summary>
    private void WorkspaceRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node != null && !ReferenceEquals(node, sender);
             node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase or TextBox) return;
        }

        if (sender is FrameworkElement { DataContext: WorkspaceNavItem item })
            (DataContext as MainViewModel)?.StartRenameWorkspaceCommand.Execute(item);
        e.Handled = true;
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;

        // Focus has to wait for the box to be laid out, or it lands nowhere.
        Dispatcher.BeginInvoke(() =>
        {
            Keyboard.Focus(box);
            box.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkspaceNavItem item } || DataContext is not MainViewModel vm)
            return;

        if (e.Key == Key.Enter)
        {
            vm.CommitRenameWorkspaceCommand.Execute(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelRenameWorkspaceCommand.Execute(item);
            e.Handled = true;
        }
    }

    /// <summary>Clicking away keeps what was typed; only Escape throws it away.</summary>
    private void RenameBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceNavItem item })
            (DataContext as MainViewModel)?.CommitRenameWorkspaceCommand.Execute(item);
    }

    private void Workspace_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TaskCardViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Workspace_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not FrameworkElement { DataContext: WorkspaceNavItem target }) return;
        if (e.Data.GetData(typeof(TaskCardViewModel)) is not TaskCardViewModel card) return;

        vm.MoveTasksToWorkspace(new[] { card.Item }, target.Id);
        e.Handled = true;
    }
}
