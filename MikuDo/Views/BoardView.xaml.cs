using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MikuDo.Controls;
using MikuDo.ViewModels;

namespace MikuDo.Views;

public partial class BoardView : UserControl, IFindHost
{
    // ── Find ────────────────────────────────────────────────────

    public string FindNoun => "board";

    /// <summary>The board's own search: the caret goes into its box, with <paramref name="text"/> in it when given.</summary>
    public bool OpenFind(string? text = null)
    {
        if (!BoardSearchBox.IsVisible) return false;
        if (text != null) BoardSearchBox.Text = text;
        Dispatcher.BeginInvoke(() =>
        {
            BoardSearchBox.Focus();
            Keyboard.Focus(BoardSearchBox);
            BoardSearchBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
        return true;
    }

    /// <summary>A board's search narrows the cards rather than going from match to match.</summary>
    public bool FindStep(int step) => false;

    private Point _dragStart;
    private TaskCardViewModel? _dragCandidate;

    /// <summary>The card currently showing the purple insertion line, if any.</summary>
    private TaskCardViewModel? _marked;

    /// <summary>
    /// Set by a card's DragOver and read by the column's, which runs afterwards
    /// because DragOver bubbles. False there means the pointer is over bare
    /// column, so the line belongs nowhere.
    /// </summary>
    private bool _markedThisPass;

    public BoardView()
    {
        InitializeComponent();

        // The grip is only 14px wide, so the pointer leaves it before the drag
        // threshold is met. Watch movement on the whole view instead.
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseUp += (_, _) => _dragCandidate = null;
    }

    // ── Renaming on the card ────────────────────────────────────

    /// <summary>
    /// Rename task's keys (F2 to start with): renames the card under the
    /// pointer, or the one card picked out. False when there is no such card,
    /// or the keys were typed into a box, so they go on to it.
    /// </summary>
    public bool RenameFromKeyboard()
    {
        if (!IsVisible || Board is not { } board) return false;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return false;

        var card = CardAt(Mouse.DirectlyOver as DependencyObject) ?? board.OnlySelected;
        if (card == null) return false;
        board.StartRenameCommand.Execute(card);
        return true;
    }

    /// <summary>The card an element sits in, if any.</summary>
    private TaskCardViewModel? CardAt(DependencyObject? element)
    {
        for (var node = element; node != null && !ReferenceEquals(node, this); node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement { DataContext: TaskCardViewModel card }) return card;
        return null;
    }

    /// <summary>The box opens with the whole title selected, ready to be typed over.</summary>
    private void TitleEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Enter keeps the new title and Esc drops it; neither reaches the window, where Esc would leave the page.</summary>
    private void TitleEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskCardViewModel card } || Board is not { } board) return;
        if (e.Key == Key.Enter) board.CommitRename(card);
        else if (e.Key == Key.Escape) board.CancelRename(card);
        else return;
        e.Handled = true;
    }

    private void TitleEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskCardViewModel card }) Board?.CommitRename(card);
    }

    private BoardViewModel? Board => DataContext as BoardViewModel;

    // ── Opening a task ──────────────────────────────────────────

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskCardViewModel card } || card.IsRenaming) return;
        if (IsInsideGrip(e.OriginalSource as DependencyObject)) return;

        if (IsSelecting)
        {
            Board?.ToggleSelection(card);
            e.Handled = true;
            return;
        }

        Board?.OpenTaskCommand.Execute(card);
    }

    /// <summary>
    /// The list view's row has no click target of its own — only the chevron at
    /// its end — so the row answers a double click, and Ctrl-click here too.
    /// </summary>
    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskCardViewModel card } || card.IsRenaming) return;
        if (IsInsideGrip(e.OriginalSource as DependencyObject)) return;

        if (IsSelecting)
        {
            Board?.ToggleSelection(card);
            e.Handled = true;
            return;
        }

        if (e.ClickCount != 2) return;

        Board?.OpenTaskCommand.Execute(card);
        e.Handled = true;
    }

    /// <summary>Ctrl held: the click picks the card out rather than opening it.</summary>
    private static bool IsSelecting => (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

    private void ListDone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskCardViewModel card })
            Board?.ToggleDoneCommand.Execute(card);
    }

    private void InlineLabel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FilterOption option })
            option.IsChecked = !option.IsChecked;
    }

    /// <summary>
    /// The inline add opens ready to type its name. The box only takes focus
    /// once it is laid out, so the focus waits for that.
    /// </summary>
    private void InlineTitle_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    // ── Priority and labels ─────────────────────────────────────

    /// <summary>
    /// The chip owns its press: without this the row underneath would treat a
    /// double click on the chip as opening the task.
    /// </summary>
    private void PriorityChip_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void PriorityChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskCardViewModel card }) return;
        e.Handled = true;

        // Ctrl keeps its meaning everywhere on a card: pick it out.
        if (IsSelecting)
        {
            Board?.ToggleSelection(card);
            return;
        }

        Board?.OpenCardEditor(card);
    }

    /// <summary>A table row's "+": the priority and labels editor for that task, where a new label can be typed.</summary>
    private void AddLabel_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: TaskCardViewModel card }) Board?.OpenCardEditor(card);
    }

    private void EditCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskCardViewModel card })
            Board?.OpenCardEditor(card);
    }

    // ── Right-click menus ───────────────────────────────────────

    /// <summary>
    /// A card's menu: what its ⋯ menu offers, and for the whole selection when
    /// the card is part of one, as those commands are.
    /// </summary>
    private void Card_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Board is not { } board || sender is not FrameworkElement { DataContext: TaskCardViewModel card } element) return;
        e.Handled = true;
        board.CloseAllMenus();
        Menus.Open(element, CardMenu(board, card));
    }

    private static object?[] CardMenu(BoardViewModel board, TaskCardViewModel card)
        => new object?[]
        {
            Menus.Item("Open task", board.OpenTaskCommand, card),
            Menus.Item("Rename", board.StartRenameCommand, card, Services.Shortcuts.GestureText("board.rename")),
            Menus.Item("Priority and labels…", () => board.OpenCardEditor(card)),
            Menus.Item("Duplicate", board.DuplicateTaskCommand, card),
            Menus.Gap,
            Menus.Item(card.MoveToALabel, board.MoveCardToStatusCommand, new object[] { card, card.OtherColumnA }),
            Menus.Item(card.MoveToBLabel, board.MoveCardToStatusCommand, new object[] { card, card.OtherColumnB }),
            Menus.Sub("Move to workspace", board.MoveTargets.Select(w => (object?)Menus.Item(w.Name, board.MoveToWorkspaceCommand, new object[] { card, w }))),
            card.HasOpenSubtasks ? Menus.Item("Mark all subtasks done", board.CompleteSubtasksCommand, card) : null,
            Menus.Gap,
            Menus.Item("Suggest a label with AI", board.SuggestLabelsCommand, card),
            Menus.Item("Export to Markdown", board.ExportCardsCommand, card),
            Menus.Gap,
            Menus.Danger(Menus.Item("Delete", board.TrashTaskCommand, card))
        };

    /// <summary>A list's menu, from its header or the bare space under its cards.</summary>
    private void Column_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Board is not { } board || sender is not FrameworkElement { DataContext: BoardColumnViewModel column } element) return;
        e.Handled = true;
        board.CloseAllMenus();
        Menus.Open(element, ColumnMenu(board, column));
    }

    private static object?[] ColumnMenu(BoardViewModel board, BoardColumnViewModel column)
        => new object?[]
        {
            board.IsKanban
                ? Menus.Item("Add a task", board.StartInlineAddCommand, column)
                : Menus.Item("Add a task…", board.AddTaskToColumnCommand, column),
            board.IsAllTasks ? Menus.Item("Search this list", board.ToggleColumnSearchCommand, column) : null,
            board.IsAllTasks
                ? Menus.Sub("Sort this list", column.SortOptions.Select(o => (object?)Menus.Check(o.Label, o.IsActive, board.PickSortCommand, o)))
                : null,
            board.IsAllTasks && column.HasOwnSort ? Menus.Item("Use the board's order", board.ResetColumnSortCommand, column) : null,
            Menus.Gap,
            Menus.Item("Suggest labels with AI", board.SuggestColumnLabelsCommand, column),
            Menus.Item("Export list to Markdown", board.ExportColumnCommand, column),
            Menus.Gap,
            board.IsList ? Menus.Item(column.IsExpanded ? "Collapse" : "Expand", board.ToggleColumnCommand, column) : null
        };

    // ── A list's own search ─────────────────────────────────────

    /// <summary>Opening a list's search puts the caret in it, ready to type.</summary>
    private void ListSearch_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;

        // Focus has to wait for the box to be laid out, or it lands nowhere.
        Dispatcher.BeginInvoke(() => Keyboard.Focus(box), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Escape closes the list's search, which also clears it.</summary>
    private void ListSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (sender is FrameworkElement { DataContext: BoardColumnViewModel column })
            Board?.ToggleColumnSearchCommand.Execute(column);
        e.Handled = true;
    }

    private static bool IsInsideGrip(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement { Tag: "grip" or "chip" }) return true;
            if (source is ButtonBase) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    // ── Dragging a card between columns ─────────────────────────

    private void Grip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as TaskCardViewModel;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate == null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragCandidate = null;
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var card = _dragCandidate;
        _dragCandidate = null;

        card.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop(this, card, DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            card.IsDragging = false;
            ClearDropTargets();
        }
    }

    private enum DropZone { Before, After, Into }

    /// <summary>Width of the link strip beside a card's grip, in the XAML as well.</summary>
    public const double LinkZoneWidth = 112;

    /// <summary>Gap between a card's grip and the link strip, in the XAML hosts' Padding as well.</summary>
    private const double LinkZoneGap = 2;

    /// <summary>How much the grip's column widens while the strip shows: the grip, the gap and the strip.</summary>
    public const double LinkZoneSpace = 14 + LinkZoneGap + LinkZoneWidth;

    /// <summary>
    /// Marks what dropping on the card under the pointer would do. A card
    /// that could take the dragged one as a subtask shows a strip just right
    /// of its grip, its content moving over to make room; only a drop on that
    /// strip links. Anywhere else on the card places the dragged one before
    /// or after it by the half the pointer is in, as an insertion line shows.
    /// </summary>
    /// <remarks>
    /// A drag starts at the grip, so a pointer moved straight up or down to
    /// reorder stays over the grips and never on a strip.
    /// </remarks>
    private void Card_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskCardViewModel card } element) return;
        if (e.Data.GetData(typeof(TaskCardViewModel)) is not TaskCardViewModel dragged) return;

        _markedThisPass = true;

        // A card cannot mark its own position.
        if (ReferenceEquals(card, dragged)) { MarkDropTarget(null, DropZone.Before, false); return; }

        var canLink = Board?.CanLinkAsSubtask(dragged, card) == true;
        var stripLeft = GripOf(element) is { } grip
            ? grip.TranslatePoint(new Point(grip.ActualWidth + LinkZoneGap, 0), element).X
            : LinkZoneGap;
        MarkDropTarget(card, ZoneAt(e.GetPosition(element), element.RenderSize, stripLeft, canLink), canLink);
    }

    /// <summary>
    /// What a drop at <paramref name="pointer"/> on a card of <paramref name="size"/>
    /// does, with its link strip starting <paramref name="stripLeft"/> in from the left.
    /// </summary>
    private static DropZone ZoneAt(Point pointer, Size size, double stripLeft, bool canLink)
        => canLink && pointer.X >= stripLeft - 2 && pointer.X <= stripLeft + LinkZoneWidth + 4 ? DropZone.Into
           : pointer.Y < size.Height / 2 ? DropZone.Before
           : DropZone.After;

    /// <summary>The drag grip inside a card, which the link strip sits beside.</summary>
    private static FrameworkElement? GripOf(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Tag: "grip" } grip) return grip;
            if (GripOf(child) is { } deeper) return deeper;
        }
        return null;
    }

    private void MarkDropTarget(TaskCardViewModel? card, DropZone zone, bool canLink)
    {
        if (_marked != null && !ReferenceEquals(_marked, card))
        {
            _marked.DropBefore = false;
            _marked.DropAfter = false;
            _marked.DropInto = false;
            _marked.LinkZoneShown = false;
        }

        _marked = card;
        if (card == null) return;

        card.DropBefore = zone == DropZone.Before;
        card.DropAfter = zone == DropZone.After;
        card.DropInto = zone == DropZone.Into;
        card.LinkZoneShown = canLink;
    }

    private void Column_DragOver(object sender, DragEventArgs e)
    {
        var column = ColumnOf(sender);
        var dragging = e.Data.GetDataPresent(typeof(TaskCardViewModel));

        e.Effects = !dragging ? DragDropEffects.None
                  : _markedThisPass && _marked?.DropInto == true ? DragDropEffects.Link
                  : DragDropEffects.Move;
        e.Handled = true;

        if (!_markedThisPass) MarkDropTarget(null, DropZone.Before, false);
        _markedThisPass = false;

        if (column == null || !dragging) return;
        foreach (var c in Board?.Columns ?? new System.Collections.ObjectModel.ObservableCollection<BoardColumnViewModel>())
            c.IsDropTarget = ReferenceEquals(c, column);
    }

    private void Column_DragLeave(object sender, DragEventArgs e)
    {
        var column = ColumnOf(sender);
        if (column != null) column.IsDropTarget = false;
    }

    private void Column_Drop(object sender, DragEventArgs e)
    {
        // Read before the markers are cleared: the last DragOver already
        // settled whether this drop links or places.
        var parent = _marked is { DropInto: true } target ? target : null;
        ClearDropTargets();

        var column = ColumnOf(sender);
        if (column == null || Board == null) return;
        if (e.Data.GetData(typeof(TaskCardViewModel)) is not TaskCardViewModel card) return;

        if (parent != null)
        {
            Board.LinkAsSubtask(card, parent);
            e.Handled = true;
            return;
        }

        var host = (DependencyObject)sender;
        Board.MoveCard(card, column, DropIndex(host, e.GetPosition((IInputElement)host), card));
        e.Handled = true;
    }

    private void ClearDropTargets()
    {
        MarkDropTarget(null, DropZone.Before, false);
        _markedThisPass = false;

        if (Board == null) return;
        foreach (var column in Board.Columns) column.IsDropTarget = false;
    }

    private static BoardColumnViewModel? ColumnOf(object sender)
        => (sender as FrameworkElement)?.Tag as BoardColumnViewModel
           ?? (sender as FrameworkElement)?.DataContext as BoardColumnViewModel;

    /// <summary>
    /// Where the pointer sits among the cards already in the column: above a
    /// card's midpoint inserts before it, below inserts after.
    /// </summary>
    /// <remarks>
    /// A virtualized column has containers only for the rows in view, so the
    /// search looks for the first row whose midpoint is below the pointer and
    /// takes its index. Rows scrolled off the top have no container and are
    /// skipped, which is right: they all sit above the pointer, and the index
    /// comes from the row itself rather than from counting what came before it.
    /// </remarks>
    private static int DropIndex(DependencyObject host, Point position, TaskCardViewModel dragged)
    {
        var items = CardsHost(host);
        if (items == null) return 0;

        var index = items.Items.Count;
        for (var i = 0; i < items.Items.Count; i++)
        {
            if (items.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;

            var top = container.TransformToAncestor((Visual)host).Transform(new Point(0, 0)).Y;
            if (position.Y >= top + container.ActualHeight / 2) continue;

            index = i;
            break;
        }

        // The card is pulled out of the column before it is put back, so every
        // position after the one it left shifts up by one.
        var from = items.Items.IndexOf(dragged);
        if (from >= 0 && from < index) index--;

        return Math.Max(0, index);
    }

    /// <summary>
    /// The list of cards for the column that was dropped on.
    /// </summary>
    /// <remarks>
    /// In list view the drop lands on a border wrapping the cards; in kanban it
    /// lands on the scroller inside the card list, so the list is an ancestor
    /// there. Looking down first keeps the two apart: a kanban scroller has no
    /// list below it, while a list-view border would otherwise walk up to the
    /// list of columns.
    /// </remarks>
    private static ItemsControl? CardsHost(DependencyObject host)
        => FindDescendant<ItemsControl>(host) ?? FindAncestor<ItemsControl>(host);

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var deeper = FindDescendant<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (var p = VisualTreeHelper.GetParent(node); p != null; p = VisualTreeHelper.GetParent(p))
            if (p is T match) return match;
        return null;
    }
}
