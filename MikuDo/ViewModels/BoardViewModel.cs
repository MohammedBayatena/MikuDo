using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>A checkbox in the Filter popover.</summary>
public partial class FilterOption : ObservableObject
{
    [ObservableProperty] private bool _isChecked;

    public string Label { get; }
    private readonly Action _changed;

    public FilterOption(string label, Action changed)
    {
        Label = label;
        _changed = changed;
    }

    partial void OnIsCheckedChanged(bool value) => _changed();
}

public class FilterGroup
{
    public string Name { get; }
    public ObservableCollection<FilterOption> Options { get; }

    public FilterGroup(string name, IEnumerable<FilterOption> options)
    {
        Name = name;
        Options = new ObservableCollection<FilterOption>(options);
    }

    public IEnumerable<string> Checked => Options.Where(o => o.IsChecked).Select(o => o.Label);
}

/// <summary>An entry in a sort menu: the board's own, or one list's.</summary>
public partial class SortOption : ObservableObject
{
    [ObservableProperty] private bool _isActive;

    public string Label { get; }
    public BoardViewModel Board { get; }

    /// <summary>The list this entry sorts, or null for the board as a whole.</summary>
    public BoardColumnViewModel? Column { get; }

    public SortOption(BoardViewModel board, string label, BoardColumnViewModel? column = null)
    {
        Board = board;
        Label = label;
        Column = column;
    }
}

/// <summary>
/// A label in the card editor. Ticked when every task being edited carries it,
/// so ticking it adds the label to all of them and unticking takes it off all.
/// </summary>
public partial class LabelToggle : ObservableObject
{
    [ObservableProperty] private bool _isChecked;

    public string Label { get; }
    public Brush Background { get; }
    public Brush Foreground { get; }

    public LabelToggle(string label, bool isChecked)
    {
        Label = label;
        _isChecked = isChecked;
        var colors = Palette.Label(label);
        Background = colors.Background;
        Foreground = colors.Foreground;
    }
}

/// <summary>One board column: To do, Doing or Done.</summary>
public partial class BoardColumnViewModel : ObservableObject
{
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isAdding;
    [ObservableProperty] private bool _isDropTarget;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private string _inlineTitle = string.Empty;
    [ObservableProperty] private string _inlineDescription = string.Empty;
    [ObservableProperty] private TodoPriority _inlinePriority = TodoPriority.None;

    /// <summary>Rows for the kanban board; kept current only while that view is up.</summary>
    [ObservableProperty] private ObservableCollection<TaskCardViewModel> _cards = new();

    /// <summary>
    /// Rows for the list view.
    /// </summary>
    /// <remarks>
    /// The two views need separate collections. A panel that does not
    /// virtualize builds a container for every item the moment the items
    /// change, whether or not it is on screen, so one collection shared by
    /// both views would build every row twice on each change.
    /// </remarks>
    [ObservableProperty] private ObservableCollection<TaskCardViewModel> _listCards = new();

    /// <summary>How many tasks are in this column after filtering.</summary>
    [ObservableProperty] private int _count;

    /// <summary>This list's own search, applied on top of the board's.</summary>
    [ObservableProperty] private string _query = string.Empty;

    /// <summary>This list's own order, or null to follow the board's.</summary>
    [ObservableProperty] private string? _sortBy;

    [ObservableProperty] private bool _isSearchOpen;
    [ObservableProperty] private bool _isSortOpen;

    public BoardViewModel Board { get; }
    public TodoStatus Status { get; }
    public string Name { get; }
    public Brush Dot { get; }
    public Brush DotHalo { get; }

    /// <summary>The soft colour the list's header sits on in the Flat layout.</summary>
    public Brush Tint { get; }

    /// <summary>The colour of the list's count on that header.</summary>
    public Brush Ink { get; }
    public ObservableCollection<FilterOption> InlineLabels { get; }
    public ObservableCollection<SortOption> SortOptions { get; } = new();

    /// <summary>
    /// The column's top-level tasks in display order, whichever view is
    /// showing. Grouped by parent, a subtask sits under its parent instead.
    /// </summary>
    public List<TodoItem> Items { get; set; } = new();

    /// <summary>Every task in the column after filtering, nested or not: what an export takes.</summary>
    public List<TodoItem> Tasks { get; set; } = new();

    /// <summary>How many of <see cref="Items"/> are on screen; the foot of the list shows the next page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore), nameof(MoreLabel))]
    private int _shown = Paging.PageSize;

    /// <summary>What the list was last built from. When it changes, the list starts again at one page.</summary>
    public string PagingKey { get; set; } = string.Empty;

    public bool HasMore => Items.Count > Shown;
    public string MoreLabel => Paging.MoreLabel(Items.Count - Shown);

    [RelayCommand]
    private void ShowMore()
    {
        if (!HasMore) return;
        Shown += Paging.PageSize;
        Board.ShowMore(this);
    }

    public void RaisePaging()
    {
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreLabel));
    }

    public string CountLabel => Count == 1 ? "1 Task" : $"{Count} Tasks";

    public bool HasOwnSort => SortBy != null;

    /// <summary>The order this list is actually in.</summary>
    public string SortLabel => SortBy ?? Board.SortBy;

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public BoardColumnViewModel(BoardViewModel board, TodoStatus status)
    {
        Board = board;
        Status = status;
        Name = Palette.ColumnName(status);
        Dot = Palette.ColumnDot(status);
        DotHalo = Palette.ColumnDotHalo(status);
        Tint = Palette.ColumnTint(status);
        Ink = Palette.ColumnInk(status);
        InlineLabels = new ObservableCollection<FilterOption>(
            Palette.QuickLabels.Select(l => new FilterOption(l, () => { })));

        foreach (var label in TaskOrder.All) SortOptions.Add(new SortOption(board, label, this));
        SyncSortOptions();
    }

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    [RelayCommand]
    private void PickInlinePriority(TodoPriority priority) => InlinePriority = priority;

    partial void OnQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasQuery));
        Board.ColumnFiltersChanged();
    }

    partial void OnSortByChanged(string? value)
    {
        OnPropertyChanged(nameof(HasOwnSort));
        RaiseSortLabel();
    }

    /// <summary>Puts back the order this list was last left in, as the board is built.</summary>
    public void RestoreSort(string? order) => SortBy = order;

    /// <summary>The board's order changed, which is this list's too unless it has its own.</summary>
    public void RaiseSortLabel()
    {
        OnPropertyChanged(nameof(SortLabel));
        SyncSortOptions();
    }

    private void SyncSortOptions()
    {
        var active = SortLabel;
        foreach (var option in SortOptions) option.IsActive = option.Label == active;
    }
}

public partial class BoardViewModel : ObservableObject
{
    [ObservableProperty] private string _workspaceName;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _viewMode = "Kanban";       // Kanban | List | Table
    [ObservableProperty] private string _statusTab = "All Tasks";   // All Tasks | To do | Doing | Done
    [ObservableProperty] private string _sortBy = TaskOrder.Manual;
    [ObservableProperty] private bool _isSortOpen;
    [ObservableProperty] private bool _isFilterOpen;
    [ObservableProperty] private bool _isMoveMenuOpen;
    [ObservableProperty] private int _filterCount;
    [ObservableProperty] private ObservableCollection<TaskCardViewModel> _allTasks = new();
    [ObservableProperty] private bool _isEmpty;

    /// <summary>The table's rows in order, of which <see cref="TableShown"/> are on screen.</summary>
    private List<TodoItem> _table = new();
    private string _tablePagingKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TableHasMore), nameof(TableMoreLabel))]
    private int _tableShown = Paging.PageSize;

    public bool TableHasMore => _table.Count > TableShown;
    public string TableMoreLabel => Paging.MoreLabel(_table.Count - TableShown);

    [RelayCommand]
    private void ShowMoreTable()
    {
        if (!TableHasMore) return;
        TableShown += Paging.PageSize;
        Sync(AllTasks, _table.Take(TableShown).ToList());
    }

    /// <summary>Brings a list's rows up to how many it now shows, leaving every other list alone.</summary>
    public void ShowMore(BoardColumnViewModel column)
    {
        var visible = column.Items.Take(column.Shown).ToList();
        if (IsKanban) Sync(column.Cards, visible);
        if (IsList) Sync(column.ListCards, visible);
    }

    /// <summary>
    /// Everything that decides which tasks a list holds and in what order. A
    /// change to any of it starts the list again at one page; a change to the
    /// tasks themselves keeps however far the user had scrolled.
    /// </summary>
    private string PagingKeyFor(string? order, string listQuery)
        => string.Join('\u001f', SearchQuery, StatusTab, _main.GroupByParent, order, listQuery,
                       string.Join(',', FilterGroups.SelectMany(g => g.Checked)));

    private readonly MainViewModel _main;
    private List<TodoItem> _source = new();
    private Dictionary<string, TodoItem> _lookup = new();
    private bool _suspendReload;

    /// <summary>The shell, for the toolbar's undo button and the badge counts.</summary>
    public MainViewModel Main => _main;

    public string? WorkspaceId { get; }
    public bool AllWorkspaces { get; }

    public ObservableCollection<BoardColumnViewModel> Columns { get; } = new();
    public ObservableCollection<FilterGroup> FilterGroups { get; } = new();
    public ObservableCollection<SortOption> SortOptions { get; } = new();

    /// <summary>Workspaces a task here can be moved to, for the card and selection menus.</summary>
    public ObservableCollection<WorkspaceNavItem> MoveTargets { get; } = new();

    public bool IsKanban => ViewMode == "Kanban";
    public bool IsList => ViewMode == "List";
    public bool IsTable => ViewMode == "Table";

    /// <summary>Every list is on screen, so each one offers its own search and sort.</summary>
    public bool IsAllTasks => StatusTab == "All Tasks";

    /// <summary>A list sorted on its own means the board does not have one order.</summary>
    public bool IsCustomSort => Columns.Any(c => c.HasOwnSort);

    public string SortLabel => IsCustomSort ? TaskOrder.Custom : SortBy;

    /// <summary>The board or one of its lists is in some order other than the default.</summary>
    public bool IsSortChanged => SortBy != TaskOrder.Manual || IsCustomSort;

    public BoardViewModel(MainViewModel main, string? workspaceId, string workspaceName, bool allWorkspaces = false)
    {
        _main = main;
        WorkspaceId = workspaceId;
        AllWorkspaces = allWorkspaces;
        _workspaceName = workspaceName;

        foreach (var status in Palette.BoardColumns)
            Columns.Add(new BoardColumnViewModel(this, status));

        RestoreSort();

        foreach (var label in TaskOrder.All)
            SortOptions.Add(new SortOption(this, label));
        RaiseSortLabel();

        BuildFilterGroups();
    }

    // ── Saved order ─────────────────────────────────────────────

    /// <summary>
    /// Where this board's orders are kept: one setting for the board and one
    /// for each list, per workspace. The search board is rebuilt for every
    /// search, so it keeps none.
    /// </summary>
    private string? SortKey => AllWorkspaces ? null : $"Sort:{WorkspaceId ?? "default"}";

    /// <summary>Opens the board in the orders it and its lists were last left in.</summary>
    private void RestoreSort()
    {
        if (SortKey is not { } key) return;

        if (Known(App.Database.GetSetting(key)) is { } board) SortBy = board;
        foreach (var column in Columns)
            column.RestoreSort(Known(App.Database.GetSetting($"{key}:{column.Status}")));
    }

    private static string? Known(string? order) => order != null && TaskOrder.All.Contains(order) ? order : null;

    private void SaveSort()
    {
        if (SortKey is not { } key) return;

        App.Database.SaveSetting(key, SortBy);
        foreach (var column in Columns)
            App.Database.SaveSetting($"{key}:{column.Status}", column.SortBy ?? string.Empty);
    }

    private void BuildFilterGroups()
    {
        FilterGroups.Clear();

        var labels = App.Database.GetAllBoardTodos()
            .SelectMany(t => t.Tags)
            .Concat(Palette.QuickLabels)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .Take(12);

        FilterGroups.Add(new FilterGroup("LABELS", labels.Select(l => new FilterOption(l, OnFilterChanged))));
        FilterGroups.Add(new FilterGroup("STATUS",
            new[] { "To do", "Doing", "Done" }.Select(l => new FilterOption(l, OnFilterChanged))));
        FilterGroups.Add(new FilterGroup("PRIORITY",
            new[] { "High", "Medium", "Low", "None" }.Select(l => new FilterOption(l, OnFilterChanged))));
        FilterGroups.Add(new FilterGroup("CONTENT",
            new[] { "Has images", "Has voice memo", "Has subtasks" }.Select(l => new FilterOption(l, OnFilterChanged))));
    }

    private void OnFilterChanged()
    {
        if (_suspendReload) return;
        FilterCount = FilterGroups.Sum(g => g.Options.Count(o => o.IsChecked));
        Rebuild();
    }

    partial void OnSearchQueryChanged(string value) => Rebuild();

    partial void OnStatusTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsAllTasks));
        Rebuild();
    }

    partial void OnSortByChanged(string value) => RaiseSortLabel();

    partial void OnViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsKanban));
        OnPropertyChanged(nameof(IsList));
        OnPropertyChanged(nameof(IsTable));
        Rebuild();
    }

    /// <summary>A list's own search changed.</summary>
    public void ColumnFiltersChanged()
    {
        if (!_suspendReload) Rebuild();
    }

    private void RaiseSortLabel()
    {
        OnPropertyChanged(nameof(IsCustomSort));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(IsSortChanged));

        var custom = IsCustomSort;
        foreach (var option in SortOptions) option.IsActive = !custom && option.Label == SortBy;
        foreach (var column in Columns) column.RaiseSortLabel();
    }

    // ── Loading ─────────────────────────────────────────────────

    public void Reload()
    {
        // The lookup already holds every task a board can show, so the board's
        // own rows come out of it rather than from a second read of the same
        // collection.
        _lookup = App.Database.GetTodoLookup();

        _source = _lookup.Values
            .Where(t => AllWorkspaces || t.WorkspaceId == WorkspaceId)
            .ToList();

        foreach (var gone in _cards.Keys.Where(id => !_lookup.ContainsKey(id)).ToList())
            _cards.Remove(gone);

        RefreshMoveTargets();
        Rebuild();

        // Filtering and sorting cannot change what the sidebar counts, so the
        // scan that feeds the badges belongs here rather than in Rebuild.
        _main.RefreshCounts();
    }

    private void RefreshMoveTargets()
    {
        MoveTargets.Clear();
        foreach (var item in _main.Workspaces)
            if (AllWorkspaces || item.Id != WorkspaceId) MoveTargets.Add(item);
    }

    /// <summary>
    /// The row for a task, made once and kept.
    /// </summary>
    /// <remarks>
    /// Views share these instances, so selecting a row or opening its menu
    /// shows up wherever that task appears. Keeping them also means a rebuild
    /// re-derives a row's text only when the task behind it actually changed.
    /// </remarks>
    private TaskCardViewModel Card(TodoItem item)
    {
        if (_cards.TryGetValue(item.IdText, out var known))
        {
            known.Adopt(item, _lookup);
            return known;
        }

        return _cards[item.IdText] = new TaskCardViewModel(this, item, _lookup)
        {
            IsSelected = _selectedIds.Contains(item.IdText)
        };
    }

    private readonly Dictionary<string, TaskCardViewModel> _cards = new(StringComparer.Ordinal);

    /// <summary>Every row currently known, in no particular order.</summary>
    private IEnumerable<TaskCardViewModel> AllCards => _cards.Values;

    private static bool Matches(TodoItem t, string query)
        => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           t.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase));

    private void Rebuild()
    {
        var items = _source.AsEnumerable();
        var grouped = _main.GroupByParent;

        // Grouped, the search waits until the trees are built, so a task found
        // by its own text or by a subtask's keeps its whole tree.
        var query = SearchQuery?.Trim() ?? string.Empty;
        if (query.Length > 0 && !grouped) items = items.Where(t => Matches(t, query));

        var labelFilter = FilterGroups.FirstOrDefault(g => g.Name == "LABELS")?.Checked.ToList() ?? new();
        if (labelFilter.Count > 0)
            items = items.Where(t => t.Tags.Any(tag => labelFilter.Contains(tag, StringComparer.OrdinalIgnoreCase)));

        var statusFilter = FilterGroups.FirstOrDefault(g => g.Name == "STATUS")?.Checked.ToList() ?? new();
        if (statusFilter.Count > 0)
            items = items.Where(t => statusFilter.Contains(Palette.ColumnName(t.Status)));

        var priorityFilter = FilterGroups.FirstOrDefault(g => g.Name == "PRIORITY")?.Checked.ToList() ?? new();
        if (priorityFilter.Count > 0)
            items = items.Where(t => priorityFilter.Contains(t.Priority.ToString()));

        var contentFilter = FilterGroups.FirstOrDefault(g => g.Name == "CONTENT")?.Checked.ToList() ?? new();
        if (contentFilter.Contains("Has images"))
            items = items.Where(t => t.Description.Contains("img://", StringComparison.Ordinal));
        if (contentFilter.Contains("Has voice memo"))
            items = items.Where(t => t.Description.Contains("voice://", StringComparison.Ordinal));
        if (contentFilter.Contains("Has subtasks"))
            items = items.Where(t => t.SubtaskIds.Count > 0);

        var list = items.ToList();
        var shown = new HashSet<string>(StringComparer.Ordinal);

        // Grouping works over what the view shows: in a single-status tab a
        // parent in another column is not on screen, so its subtask stays out.
        var scope = IsAllTasks ? list : list.Where(t => Palette.ColumnName(t.Status) == StatusTab).ToList();
        var nested = Group(grouped ? scope : new List<TodoItem>());

        if (grouped && query.Length > 0)
        {
            var kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (var top in scope.Where(t => !nested.Contains(t.IdText)))
            {
                var card = Card(top);
                if (!Matches(top, query) && !UnderMatches(card, query)) continue;
                kept.Add(top.IdText);
                foreach (var below in Below(card)) kept.Add(below.Item.IdText);
            }
            list = list.Where(t => kept.Contains(t.IdText)).ToList();
            scope = scope.Where(t => kept.Contains(t.IdText)).ToList();
        }

        foreach (var column in Columns)
        {
            var mine = list.Where(t => t.Status == column.Status && !nested.Contains(t.IdText));
            var order = SortBy;

            // A list's own search and sort only apply while every list is on
            // screen; a single-status tab is governed by the board's alone.
            // Grouped, a parent stays when something under it matches.
            if (IsAllTasks)
            {
                var own = column.Query.Trim();
                if (own.Length > 0) mine = mine.Where(t => Matches(t, own) || UnderMatches(Card(t), own));
                order = column.SortBy ?? SortBy;
            }

            column.Tasks = TaskOrder.Apply(list.Where(t => t.Status == column.Status), order).ToList();
            column.Items = TaskOrder.Apply(mine, order).ToList();
            column.Count = column.Items.Count;
            column.IsVisible = IsAllTasks || StatusTab == column.Name;

            var key = PagingKeyFor(order, IsAllTasks ? column.Query : string.Empty);
            if (key != column.PagingKey)
            {
                column.PagingKey = key;
                column.Shown = Paging.PageSize;
            }
            column.RaisePaging();
            var visible = column.Items.Take(column.Shown).ToList();

            // Only the view on screen is brought up to date. A hidden view
            // keeps the rows it had, untouched and so costing nothing, and
            // coming back to it only pays for what changed in between.
            if (IsKanban) Sync(column.Cards, visible);
            if (IsList) Sync(column.ListCards, visible);

            if (!IsTable && column.IsVisible)
                foreach (var t in column.Items) shown.Add(t.IdText);
        }

        if (IsTable)
        {
            _table = TaskOrder.Apply(scope.Where(t => !nested.Contains(t.IdText)), SortBy).ToList();
            var key = PagingKeyFor(SortBy, string.Empty);
            if (key != _tablePagingKey)
            {
                _tablePagingKey = key;
                TableShown = Paging.PageSize;
            }
            OnPropertyChanged(nameof(TableHasMore));
            OnPropertyChanged(nameof(TableMoreLabel));
            Sync(AllTasks, _table.Take(TableShown).ToList());
            foreach (var t in _table) shown.Add(t.IdText);
        }

        // A task the filters just hid cannot stay selected.
        if (_selectedIds.RemoveWhere(id => !shown.Contains(id)) > 0) RaiseSelectionChanged();

        IsEmpty = list.Count == 0;
    }

    // ── Grouping by parent ──────────────────────────────────────

    /// <summary>Cards that were given subtasks by the last grouping, so the next can take them back.</summary>
    private HashSet<TaskCardViewModel> _parents = new();

    /// <summary>
    /// Gives each task in <paramref name="scope"/> its subtasks from the same
    /// scope and returns the ids that now sit under a parent. An empty scope
    /// ungroups everything.
    /// </summary>
    /// <remarks>
    /// A task is top-level when nothing in scope lists it as a subtask. Links
    /// can form a loop, which leaves no top-level task to reach the loop from,
    /// so any task the top-level ones cannot reach is lifted to the top.
    /// </remarks>
    private HashSet<string> Group(List<TodoItem> scope)
    {
        var byId = scope.ToDictionary(t => t.IdText, StringComparer.Ordinal);
        var childrenOf = new Dictionary<string, List<TodoItem>>(StringComparer.Ordinal);
        var nested = new HashSet<string>(StringComparer.Ordinal);

        foreach (var task in scope)
        {
            var kids = task.SubtaskIds.Distinct(StringComparer.Ordinal)
                .Where(id => id != task.IdText && byId.ContainsKey(id))
                .Select(id => byId[id])
                .ToList();
            if (kids.Count == 0) continue;

            childrenOf[task.IdText] = kids;
            foreach (var kid in kids) nested.Add(kid.IdText);
        }

        var reached = new HashSet<string>(StringComparer.Ordinal);
        void Reach(string id)
        {
            if (!reached.Add(id)) return;
            if (childrenOf.TryGetValue(id, out var kids))
                foreach (var kid in kids) Reach(kid.IdText);
        }

        foreach (var task in scope) if (!nested.Contains(task.IdText)) Reach(task.IdText);
        foreach (var task in scope)
        {
            if (reached.Contains(task.IdText)) continue;
            nested.Remove(task.IdText);
            Reach(task.IdText);
        }

        var parents = new HashSet<TaskCardViewModel>();
        foreach (var (id, kids) in childrenOf)
        {
            // The links' own order is the manual one for subtasks.
            var ordered = SortBy == TaskOrder.Manual ? kids : TaskOrder.Apply(kids, SortBy).ToList();
            var card = Card(byId[id]);
            card.SetGroupChildren(ordered.Select(Card).ToList());
            parents.Add(card);
        }

        foreach (var card in _parents)
            if (!parents.Contains(card)) card.SetGroupChildren(Array.Empty<TaskCardViewModel>());
        foreach (var card in parents.Concat(_parents)) card.RebuildSubRows();
        _parents = parents;

        return nested;
    }

    /// <summary>Something grouped under <paramref name="card"/>, at any depth, matches the search.</summary>
    private static bool UnderMatches(TaskCardViewModel card, string query)
        => Below(card).Any(c => Matches(c.Item, query));

    /// <summary>Everything grouped under <paramref name="card"/>, at any depth, each once.</summary>
    private static IEnumerable<TaskCardViewModel> Below(TaskCardViewModel card)
    {
        var seen = new HashSet<TaskCardViewModel> { card };
        var stack = new Stack<TaskCardViewModel>(card.GroupChildren);
        while (stack.Count > 0)
        {
            var next = stack.Pop();
            if (!seen.Add(next)) continue;
            yield return next;
            foreach (var child in next.GroupChildren) stack.Push(child);
        }
    }

    /// <summary>
    /// Brings <paramref name="target"/> into line with <paramref name="items"/>
    /// by moving, inserting and removing rows.
    /// </summary>
    /// <remarks>
    /// Handing an <see cref="ObservableCollection{T}"/> a fresh instance makes
    /// the list throw away every container and build the visual tree again,
    /// which is by far the most expensive thing a rebuild can do — and a search
    /// or a filter rebuilds on each keystroke. Editing the collection in place
    /// keeps the containers of the rows that survived.
    /// </remarks>
    private void Sync(ObservableCollection<TaskCardViewModel> target, IReadOnlyList<TodoItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var card = Card(items[i]);
            if (i < target.Count && ReferenceEquals(target[i], card)) continue;

            // Every slot before i already holds its final row, so a row found
            // here can only be further down.
            var from = target.IndexOf(card);
            if (from > i) target.Move(from, i);
            else if (from < 0) target.Insert(i, card);
        }

        while (target.Count > items.Count) target.RemoveAt(target.Count - 1);
    }

    // ── Toolbar ─────────────────────────────────────────────────

    [RelayCommand] private void SetViewMode(string? mode) => ViewMode = mode ?? "Kanban";
    [RelayCommand] private void SetStatusTab(string? tab) => StatusTab = tab ?? "All Tasks";

    [RelayCommand]
    private void ToggleSortMenu()
    {
        var open = !IsSortOpen;
        CloseAllMenus();
        IsSortOpen = open;
    }

    [RelayCommand]
    private void ToggleFilterMenu()
    {
        var open = !IsFilterOpen;
        CloseAllMenus();
        IsFilterOpen = open;
    }

    [RelayCommand]
    private void CloseMenus() => CloseAllMenus();

    /// <summary>
    /// Picking the board's order puts every list back on it, which is how a
    /// board that went Custom becomes one order again.
    /// </summary>
    [RelayCommand]
    private void PickSort(SortOption? option)
    {
        if (option == null) return;

        // One row template serves both menus; a list's own entries carry it.
        if (option.Column != null)
        {
            PickColumnSort(option);
            return;
        }

        _suspendReload = true;
        foreach (var column in Columns) column.SortBy = null;
        _suspendReload = false;

        SortBy = option.Label;
        RaiseSortLabel();
        SaveSort();
        IsSortOpen = false;
        Rebuild();
    }

    /// <summary>Puts the board and every list back on the default order.</summary>
    [RelayCommand]
    private void ResetSort()
    {
        _suspendReload = true;
        foreach (var column in Columns) column.SortBy = null;
        _suspendReload = false;

        SortBy = TaskOrder.Manual;
        RaiseSortLabel();
        SaveSort();
        CloseAllMenus();
        Rebuild();
    }

    /// <summary>
    /// Releases every card on this board from where it was put, so they all
    /// fall back to date and priority.
    /// </summary>
    [RelayCommand]
    private void ClearManualOrder()
    {
        var placed = _source.Where(t => t.IsPlaced).ToList();
        IsSortOpen = false;
        if (placed.Count == 0) return;

        var snapshots = placed.Select(t => t.Snapshot()).ToList();
        foreach (var item in placed)
        {
            item.IsPlaced = false;
            App.Database.UpsertTodo(item);
        }

        _main.PushUndo("clear manual order", WorkspaceId, () => Restore(snapshots));
        Reload();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suspendReload = true;
        foreach (var group in FilterGroups)
            foreach (var option in group.Options)
                option.IsChecked = false;
        _suspendReload = false;
        FilterCount = 0;
        Rebuild();
    }

    [RelayCommand] private void ClearSearch() => SearchQuery = string.Empty;

    // ── A list's own search and sort ────────────────────────────

    [RelayCommand]
    private void ToggleColumnSearch(BoardColumnViewModel? column)
    {
        if (column == null) return;

        // Closing the box drops its search, so nothing stays filtered out of
        // sight with no box left on screen to explain why.
        if (column.IsSearchOpen) column.Query = string.Empty;
        column.IsSearchOpen = !column.IsSearchOpen;
    }

    [RelayCommand]
    private void ClearColumnSearch(BoardColumnViewModel? column)
    {
        if (column != null) column.Query = string.Empty;
    }

    [RelayCommand]
    private void ToggleColumnSort(BoardColumnViewModel? column)
    {
        if (column == null) return;
        var open = !column.IsSortOpen;
        CloseAllMenus();
        column.IsSortOpen = open;
    }

    /// <summary>
    /// Sorts one list. Choosing the order the board already uses hands the list
    /// back to the board rather than pinning it to a copy of the same order.
    /// </summary>
    private void PickColumnSort(SortOption? option)
    {
        var column = option?.Column;
        if (column == null) return;

        column.SortBy = option!.Label == SortBy ? null : option.Label;
        column.IsSortOpen = false;
        RaiseSortLabel();
        SaveSort();
        Rebuild();
    }

    /// <summary>Hands one list back to the board's order.</summary>
    [RelayCommand]
    private void ResetColumnSort(BoardColumnViewModel? column)
    {
        if (column == null) return;
        column.SortBy = null;
        column.IsSortOpen = false;
        RaiseSortLabel();
        SaveSort();
        Rebuild();
    }

    // ── Cards ───────────────────────────────────────────────────

    [RelayCommand]
    private void OpenTask(TaskCardViewModel? card)
    {
        if (card == null) return;
        CloseAllMenus();
        _main.OpenTask(card.Item, isVault: false, "Board");
    }

    [RelayCommand]
    private void ToggleCardMenu(TaskCardViewModel? card)
    {
        if (card == null) return;
        var open = !card.IsMenuOpen;
        CloseAllMenus();
        card.IsMenuOpen = open;
    }

    public void CloseAllMenus()
    {
        foreach (var card in AllCards) card.IsMenuOpen = false;
        foreach (var column in Columns) column.IsSortOpen = false;
        IsSortOpen = false;
        IsFilterOpen = false;
        IsMoveMenuOpen = false;
        IsCardEditorOpen = false;
    }

    /// <summary>Ticking a card in list view flips it between To do and Done.</summary>
    [RelayCommand]
    private void ToggleDone(TaskCardViewModel? card)
    {
        if (card == null) return;
        var item = card.Item;
        var snapshot = item.Snapshot();
        SetStatus(item, item.Status == TodoStatus.Completed ? TodoStatus.Active : TodoStatus.Completed);
        _main.PushUndo("tick", WorkspaceId, () => Restore(new[] { snapshot }));
        Reload();
        if (item.Status == TodoStatus.Completed) _main.OfferToFinishSubtasks(new[] { item });
    }

    [RelayCommand]
    private void DuplicateTask(TaskCardViewModel? card)
    {
        if (card == null) return;
        var source = card.Item;
        var copy = new TodoItem
        {
            Title = source.Title + " copy",
            Description = source.Description,
            Priority = source.Priority,
            Status = source.Status,
            WorkspaceId = source.WorkspaceId,
            Tags = new List<string>(source.Tags),
            SubtaskIds = new List<string>(source.SubtaskIds),
            // Straight after the original, wherever that was put.
            SortOrder = source.SortOrder + 1,
            IsPlaced = source.IsPlaced
        };
        App.Database.UpsertTodo(copy);

        var id = copy.Id;
        _main.PushUndo("duplicate", WorkspaceId, () => App.Database.DeleteTodoPermanently(id));

        CloseAllMenus();
        Reload();
    }

    // ── Priority and labels, from the card ──────────────────────

    [ObservableProperty] private TaskCardViewModel? _editingCard;
    [ObservableProperty] private bool _isCardEditorOpen;
    [ObservableProperty] private string _editorNewLabel = string.Empty;

    /// <summary>Highest first, the way the eye scans for what matters.</summary>
    public TodoPriority[] PriorityChoices { get; } =
        { TodoPriority.High, TodoPriority.Medium, TodoPriority.Low, TodoPriority.None };

    /// <summary>None first, in the order the task page and Add Task list them.</summary>
    public TodoPriority[] AscendingPriorities { get; } =
        { TodoPriority.None, TodoPriority.Low, TodoPriority.Medium, TodoPriority.High };

    public ObservableCollection<LabelToggle> EditorLabels { get; } = new();

    /// <summary>What the editor applies to: the card, or the whole selection it belongs to.</summary>
    public string EditorHeading
    {
        get
        {
            if (EditingCard == null) return string.Empty;
            var count = TargetsFor(EditingCard).Count;
            return count > 1 ? $"{count} selected tasks" : EditingCard.Title;
        }
    }

    /// <summary>The editor is changing several tasks at once.</summary>
    public bool EditorAppliesToMany => EditingCard != null && TargetsFor(EditingCard).Count > 1;

    /// <summary>One editor serves every card; it opens beside whatever was clicked.</summary>
    public void OpenCardEditor(TaskCardViewModel card)
    {
        CloseAllMenus();
        EditingCard = card;
        EditorNewLabel = string.Empty;
        RebuildEditorLabels();
        OnPropertyChanged(nameof(EditorHeading));
        OnPropertyChanged(nameof(EditorAppliesToMany));
        IsCardEditorOpen = true;
    }

    /// <summary>
    /// The selection bar's "Priority &amp; labels": the same editor, anchored on
    /// one selected card, so every change reaches the whole selection.
    /// </summary>
    [RelayCommand]
    private void EditSelection()
    {
        var anchor = _cards.Values.FirstOrDefault(c => _selectedIds.Contains(c.Item.IdText));
        if (anchor != null) OpenCardEditor(anchor);
    }

    private void RebuildEditorLabels()
    {
        EditorLabels.Clear();
        if (EditingCard == null) return;

        var targets = TargetsFor(EditingCard);
        var onAll = targets
            .Select(t => t.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase))
            .Aggregate((a, b) => { a.IntersectWith(b); return a; });

        // Labels already in use come first, so the ones that matter are not
        // buried under suggestions nobody has picked yet.
        var names = targets.SelectMany(t => t.Tags)
            .Concat(_source.SelectMany(t => t.Tags))
            .Concat(Palette.SuggestedLabels)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24);

        foreach (var name in names) EditorLabels.Add(new LabelToggle(name, onAll.Contains(name)));
    }

    [RelayCommand]
    private void SetEditorPriority(TodoPriority priority)
    {
        if (EditingCard == null) return;

        var targets = TargetsFor(EditingCard);
        if (targets.All(t => t.Priority == priority)) return;

        var snapshots = targets.Select(t => t.Snapshot()).ToList();
        foreach (var item in targets)
        {
            item.Priority = priority;
            App.Database.UpsertTodo(item);
        }

        _main.PushUndo(targets.Count > 1 ? "priority change" : "priority", WorkspaceId, () => Restore(snapshots));
        Reload();
    }

    [RelayCommand]
    private void ToggleEditorLabel(LabelToggle? toggle)
    {
        if (toggle == null || EditingCard == null) return;
        ApplyLabel(toggle.Label, add: !toggle.IsChecked);
    }

    [RelayCommand]
    private void AddEditorLabel()
    {
        var label = EditorNewLabel.Trim();
        if (label.Length == 0 || EditingCard == null) return;

        EditorNewLabel = string.Empty;
        ApplyLabel(label, add: true);
        BuildFilterGroups();
    }

    private void ApplyLabel(string label, bool add)
    {
        var targets = TargetsFor(EditingCard!);
        var snapshots = targets.Select(t => t.Snapshot()).ToList();
        var changed = false;

        foreach (var item in targets)
        {
            var has = item.Tags.Any(t => string.Equals(t, label, StringComparison.OrdinalIgnoreCase));
            if (add == has) continue;

            if (add) item.Tags.Add(label);
            else item.Tags.RemoveAll(t => string.Equals(t, label, StringComparison.OrdinalIgnoreCase));
            App.Database.UpsertTodo(item);
            changed = true;
        }

        if (!changed) return;

        _main.PushUndo(add ? "add label" : "remove label", WorkspaceId, () => Restore(snapshots));
        Reload();
        RebuildEditorLabels();
    }

    // ── Moving to another workspace ─────────────────────────────

    [RelayCommand]
    private void ToggleMoveMenu()
    {
        var open = !IsMoveMenuOpen;
        CloseAllMenus();
        IsMoveMenuOpen = open;
    }

    /// <summary>
    /// The parameter pairs a card with the destination; a null card means the
    /// selection bar asked, so the selection moves.
    /// </summary>
    [RelayCommand]
    private void MoveToWorkspace(object? parameter)
    {
        if (parameter is not object[] { Length: 2 } args) return;
        if (args[1] is not WorkspaceNavItem destination) return;

        var card = args[0] as TaskCardViewModel ?? FirstSelected();
        if (card == null) return;

        var targets = TargetsFor(card).Where(t => t.WorkspaceId != destination.Id).ToList();
        CloseAllMenus();
        if (targets.Count == 0) return;

        ClearSelection();
        _main.MoveTasksToWorkspace(targets, destination.Id);
    }

    /// <summary>The selection bar's Move to menu.</summary>
    [RelayCommand]
    private void MoveSelectionToWorkspace(WorkspaceNavItem? destination)
    {
        if (destination != null) MoveToWorkspace(new object?[] { null, destination });
    }

    // ── Export ──────────────────────────────────────────────────

    [RelayCommand]
    private void ExportBoard()
    {
        CloseAllMenus();
        var groups = Columns.Where(c => c.IsVisible && c.Tasks.Count > 0)
                            .Select(c => new ExportGroup(c.Name, c.Tasks.ToList()))
                            .ToList();
        _main.OpenExport(WorkspaceName, groups);
    }

    [RelayCommand]
    private void ExportColumn(BoardColumnViewModel? column)
    {
        if (column == null) return;
        CloseAllMenus();
        _main.OpenExport($"{WorkspaceName} · {column.Name}",
                         new[] { new ExportGroup(column.Name, column.Tasks.ToList()) });
    }

    /// <summary>One card, or the selection it belongs to, gets a kind label suggested.</summary>
    [RelayCommand]
    private void SuggestLabels(TaskCardViewModel? card)
    {
        card ??= FirstSelected();
        if (card == null) return;

        var targets = TargetsFor(card);
        CloseAllMenus();
        _main.OpenLabelSuggest(AllWorkspaces ? null : WorkspaceId,
                               targets.Count == 1 ? targets[0].Title : $"{targets.Count} selected tasks", targets);
    }

    /// <summary>Suggests labels for every task the list shows, nested ones included.</summary>
    [RelayCommand]
    private void SuggestColumnLabels(BoardColumnViewModel? column)
    {
        if (column == null) return;
        CloseAllMenus();
        _main.OpenLabelSuggest(AllWorkspaces ? null : WorkspaceId, column.Name, column.Tasks);
    }

    /// <summary>One card, or the selection it belongs to, grouped by column.</summary>
    [RelayCommand]
    private void ExportCards(TaskCardViewModel? card)
    {
        card ??= FirstSelected();
        if (card == null) return;

        var targets = TargetsFor(card);
        CloseAllMenus();

        var groups = Palette.BoardColumns
            .Select(s => new ExportGroup(Palette.ColumnName(s), targets.Where(t => t.Status == s).ToList()))
            .Where(g => g.Items.Count > 0)
            .ToList();
        _main.OpenExport(targets.Count == 1 ? targets[0].Title : $"{targets.Count} tasks", groups);
    }

    // ── Selection ───────────────────────────────────────────────

    /// <summary>
    /// Ticked cards, by id. Cards are rebuilt on every reload, so the selection
    /// lives here rather than on them.
    /// </summary>
    private readonly HashSet<string> _selectedIds = new();

    public int SelectedCount => _selectedIds.Count;

    public bool HasSelection => _selectedIds.Count > 0;

    public string SelectionLabel => _selectedIds.Count == 1 ? "1 task selected" : $"{_selectedIds.Count} tasks selected";

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionLabel));
    }

    /// <summary>Ctrl-clicking a card adds it to the selection instead of opening it.</summary>
    public void ToggleSelection(TaskCardViewModel card)
    {
        var id = card.Item.IdText;
        if (!_selectedIds.Remove(id)) _selectedIds.Add(id);

        card.IsSelected = _selectedIds.Contains(id);
        RaiseSelectionChanged();
    }

    /// <summary>Bulk versions for the selection bar, which has no card to act on.</summary>
    [RelayCommand]
    private void AdvanceSelection() => AdvanceCard(FirstSelected());

    [RelayCommand]
    private void RetreatSelection() => RetreatCard(FirstSelected());

    [RelayCommand]
    private void TrashSelection() => TrashTask(FirstSelected());

    private TaskCardViewModel? FirstSelected()
        => AllCards.FirstOrDefault(c => c.IsSelected);

    [RelayCommand]
    private void ClearSelection()
    {
        _selectedIds.Clear();
        foreach (var card in AllCards) card.IsSelected = false;

        RaiseSelectionChanged();
    }

    /// <summary>
    /// What an action applies to: the whole selection when the card acted on is
    /// part of it, otherwise just that card.
    /// </summary>
    private List<TodoItem> TargetsFor(TaskCardViewModel card)
    {
        if (!_selectedIds.Contains(card.Item.IdText)) return new List<TodoItem> { card.Item };

        return _source.Where(t => _selectedIds.Contains(t.IdText)).ToList();
    }

    // ── Card actions ────────────────────────────────────────────

    /// <summary>Moves a card one column right; the last column has nowhere to go.</summary>
    [RelayCommand]
    private void AdvanceCard(TaskCardViewModel? card) => ShiftCards(card, 1);

    /// <summary>Moves a card one column left; the first column has nowhere to go.</summary>
    [RelayCommand]
    private void RetreatCard(TaskCardViewModel? card) => ShiftCards(card, -1);

    private void ShiftCards(TaskCardViewModel? card, int direction)
    {
        if (card == null) return;

        var targets = TargetsFor(card);
        var snapshots = targets.Select(t => t.Snapshot()).ToList();
        var moved = false;
        var finished = new List<TodoItem>();

        foreach (var item in targets)
        {
            var next = Neighbour(item.Status, direction);
            if (next == null) continue;

            SetStatus(item, next.Value);
            moved = true;
            if (next == TodoStatus.Completed) finished.Add(item);
        }

        if (moved)
            _main.PushUndo(targets.Count > 1 ? "move" : "move task", WorkspaceId,
                           () => Restore(snapshots));

        CloseAllMenus();
        ClearSelection();
        Reload();
        _main.OfferToFinishSubtasks(finished);
    }

    /// <summary>The column <paramref name="direction"/> steps along, or null at the ends.</summary>
    private static TodoStatus? Neighbour(TodoStatus status, int direction)
    {
        var order = Palette.BoardColumns;
        var i = Array.IndexOf(order, status);
        if (i < 0) return null;

        var next = i + direction;
        return next >= 0 && next < order.Length ? order[next] : null;
    }

    private static void Restore(IEnumerable<TodoItem> snapshots)
    {
        foreach (var snapshot in snapshots) App.Database.UpsertTodo(snapshot);
    }

    /// <summary>
    /// Marks done everything beneath the card, however deep, or beneath each
    /// selected card when it is part of the selection, as one step Undo takes back.
    /// </summary>
    [RelayCommand]
    private void CompleteSubtasks(TaskCardViewModel? card)
    {
        if (card == null) return;
        CloseAllMenus();
        _main.FinishSubtasks(MainViewModel.OpenSubtasksBeneath(TargetsFor(card)));
    }

    [RelayCommand]
    private void TrashTask(TaskCardViewModel? card)
    {
        if (card == null) return;

        var targets = TargetsFor(card);
        var snapshots = targets.Select(t => t.Snapshot()).ToList();

        foreach (var item in targets)
        {
            item.StatusBeforeTrash = item.Status;
            item.Status = TodoStatus.Trashed;
            item.TrashedAt = DateTime.UtcNow;
            App.Database.UpsertTodo(item);
        }

        _main.PushUndo(targets.Count > 1 ? "delete" : "delete task", WorkspaceId,
                       () => Restore(snapshots));

        CloseAllMenus();
        ClearSelection();
        Reload();
    }

    [RelayCommand]
    private void MoveCardToStatus(object? parameter)
    {
        if (parameter is not object[] args || args.Length < 2) return;
        if (args[0] is not TaskCardViewModel card || args[1] is not string statusName) return;

        var item = card.Item;
        var snapshot = item.Snapshot();
        var status = Palette.StatusFromName(statusName);
        var finishing = status == TodoStatus.Completed && item.Status != TodoStatus.Completed;
        SetStatus(item, status);
        _main.PushUndo("move task", WorkspaceId, () => Restore(new[] { snapshot }));
        CloseAllMenus();
        Reload();
        if (finishing) _main.OfferToFinishSubtasks(new[] { item });
    }

    /// <summary>
    /// A card sent to another column by a button lands among that column's
    /// unplaced cards; the slot it held in the column it left means nothing there.
    /// </summary>
    private static void SetStatus(TodoItem item, TodoStatus status)
    {
        if (item.Status != status) item.IsPlaced = false;
        item.Status = status;
        item.CompletedAt = status == TodoStatus.Completed ? DateTime.UtcNow : null;
        App.Database.UpsertTodo(item);
    }

    /// <summary>Names of the two columns a card is not currently in.</summary>
    public IEnumerable<string> OtherColumnNames(TaskCardViewModel card)
        => Columns.Where(c => c.Status != card.Item.Status).Select(c => c.Name);

    // ── Drag and drop ───────────────────────────────────────────

    /// <summary>
    /// Drops <paramref name="card"/> into <paramref name="target"/> at
    /// <paramref name="index"/>, and pins the whole column in the order it now
    /// shows: an arrangement the user just made is theirs to keep.
    /// </summary>
    public void MoveCard(TaskCardViewModel card, BoardColumnViewModel target, int index)
    {
        var moving = card.Item;
        var ordered = target.Items.Where(t => !ReferenceEquals(t, moving)).ToList();
        index = Math.Clamp(index, 0, ordered.Count);
        ordered.Insert(index, moving);

        var snapshots = ordered.Select(t => t.Snapshot()).ToList();
        var finishing = target.Status == TodoStatus.Completed && moving.Status != TodoStatus.Completed;

        if (moving.Status != target.Status)
        {
            moving.Status = target.Status;
            moving.CompletedAt = target.Status == TodoStatus.Completed ? DateTime.UtcNow : null;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = (i + 1) * 1000;
            ordered[i].IsPlaced = true;
            App.Database.UpsertTodo(ordered[i]);
        }

        // Dragging is an arrangement, so the list stops following any other order.
        if (target.SortBy != null && target.SortBy != TaskOrder.Manual)
        {
            target.SortBy = null;
            SaveSort();
        }

        _main.PushUndo("move task", WorkspaceId, () => Restore(snapshots));
        foreach (var column in Columns) column.IsDropTarget = false;
        Reload();
        if (finishing) _main.OfferToFinishSubtasks(new[] { moving });
    }

    // ── Linking by drag ─────────────────────────────────────────

    /// <summary>
    /// Whether <paramref name="child"/> can be dropped onto <paramref name="parent"/>
    /// to become its subtask: not onto itself, not again, and not onto a task
    /// that is already somewhere beneath it, which would make a loop.
    /// </summary>
    public bool CanLinkAsSubtask(TaskCardViewModel child, TaskCardViewModel parent)
    {
        if (ReferenceEquals(child, parent)) return false;
        if (parent.Item.SubtaskIds.Contains(child.Item.IdText)) return false;
        return !Reaches(child.Item, parent.Item.IdText);
    }

    /// <summary>Whether <paramref name="target"/> sits anywhere among the subtasks under <paramref name="from"/>.</summary>
    private bool Reaches(TodoItem from, string target)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { from.IdText };
        var queue = new Queue<string>(from.SubtaskIds);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (id == target) return true;
            if (!seen.Add(id) || !_lookup.TryGetValue(id, out var task)) continue;
            foreach (var next in task.SubtaskIds) queue.Enqueue(next);
        }
        return false;
    }

    /// <summary>Makes <paramref name="child"/> a subtask of <paramref name="parent"/>. Its own column and place stay as they were.</summary>
    public void LinkAsSubtask(TaskCardViewModel child, TaskCardViewModel parent)
    {
        if (!CanLinkAsSubtask(child, parent)) return;

        var snapshot = parent.Item.Snapshot();
        parent.Item.SubtaskIds.Add(child.Item.IdText);
        App.Database.UpsertTodo(parent.Item);

        _main.PushUndo("link subtask", WorkspaceId, () => Restore(new[] { snapshot }));
        foreach (var column in Columns) column.IsDropTarget = false;
        Reload();
    }

    // ── Renaming on the card ────────────────────────────────────

    /// <summary>Turns a card's title into a box to type the new one in. One card at a time.</summary>
    [RelayCommand]
    private void StartRename(TaskCardViewModel? card)
    {
        if (card == null) return;
        CloseAllMenus();
        foreach (var other in AllCards) if (!ReferenceEquals(other, card)) other.IsRenaming = false;
        card.RenameText = card.Title;
        card.IsRenaming = true;
    }

    /// <summary>Keeps the typed title, as one step Undo takes back. A blank or unchanged title changes nothing.</summary>
    public void CommitRename(TaskCardViewModel card)
    {
        if (!card.IsRenaming) return;
        card.IsRenaming = false;

        var title = card.RenameText.Trim();
        var item = App.Database.GetTodoById(card.Item.Id);
        if (item == null || title.Length == 0 || title == item.Title) return;

        var snapshot = item.Snapshot();
        item.Title = title;
        App.Database.UpsertTodo(item);
        _main.PushUndo("rename task", WorkspaceId, () => Restore(new[] { snapshot }));
        Reload();
    }

    public void CancelRename(TaskCardViewModel card) => card.IsRenaming = false;

    /// <summary>The card picked out, when exactly one is.</summary>
    public TaskCardViewModel? OnlySelected => _selectedIds.Count == 1 ? AllCards.FirstOrDefault(c => c.IsSelected) : null;

    // ── Inline add ──────────────────────────────────────────────

    [RelayCommand]
    private void StartInlineAdd(BoardColumnViewModel? column)
    {
        if (column == null) return;
        foreach (var c in Columns) c.IsAdding = false;
        column.InlineTitle = string.Empty;
        column.InlineDescription = string.Empty;
        column.InlinePriority = TodoPriority.None;
        foreach (var label in column.InlineLabels) label.IsChecked = false;
        column.IsAdding = true;
    }

    [RelayCommand]
    private void CancelInlineAdd(BoardColumnViewModel? column)
    {
        if (column != null) column.IsAdding = false;
    }

    [RelayCommand]
    private void SaveInlineAdd(BoardColumnViewModel? column)
    {
        if (column == null) return;
        var title = column.InlineTitle.Trim();
        if (title.Length == 0) return;

        App.Database.UpsertTodo(new TodoItem
        {
            Title = title,
            Description = column.InlineDescription.Trim(),
            Priority = column.InlinePriority,
            Status = column.Status,
            WorkspaceId = AllWorkspaces ? null : WorkspaceId,
            Tags = column.InlineLabels.Where(l => l.IsChecked).Select(l => l.Label).ToList(),
            SortOrder = App.Database.GetNextSortOrder(WorkspaceId, column.Status),
            CompletedAt = column.Status == TodoStatus.Completed ? DateTime.UtcNow : null
        });

        column.IsAdding = false;
        BuildFilterGroups();
        Reload();
        _main.ReloadWorkspaces();
    }

    [RelayCommand]
    private void ToggleColumn(BoardColumnViewModel? column)
    {
        if (column != null) column.IsExpanded = !column.IsExpanded;
    }

    [RelayCommand]
    private void AddTaskToColumn(BoardColumnViewModel? column)
        => _main.OpenAddTask(AllWorkspaces ? null : WorkspaceId, column?.Status ?? TodoStatus.Active);
}
