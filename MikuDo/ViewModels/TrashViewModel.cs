using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>A row in the trash list. Items are purged 30 days after deletion.</summary>
public partial class TrashRow : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public TodoItem Item { get; }
    public string Title => Item.Title;
    public string Origin { get; }

    public TodoPriority Priority => Item.Priority;
    public bool HasPriority => Item.Priority != TodoPriority.None;
    public string PriorityName => TaskCardViewModel.PriorityText(Item.Priority);
    public Brush PriorityForeground => Palette.Priority(Item.Priority).Foreground;
    public int CommentCount => Item.Comments.Count;

    private DateTime DeletedOn => (Item.TrashedAt ?? Item.UpdatedAt).ToLocalTime();
    public string DeletedLabel => DeletedOn.ToString("d MMM yyyy");

    public string ExpiresLabel
    {
        get
        {
            var left = 30 - (int)(DateTime.Now - DeletedOn).TotalDays;
            return left <= 0 ? "Due for removal" : left == 1 ? "1 day left" : $"{left} days left";
        }
    }

    public TrashRow(TodoItem item, string origin, Action changed)
    {
        Item = item;
        Origin = origin;
        _changed = changed;
    }

    private readonly Action _changed;
    partial void OnIsSelectedChanged(bool value) => _changed();
}

public partial class TrashViewModel : ObservableObject
{
    /// <summary>The rows on screen: the first pages of <see cref="_all"/>.</summary>
    [ObservableProperty] private ObservableCollection<TrashRow> _rows = new();
    [ObservableProperty] private int _selectedCount;

    /// <summary>Every item in the trash, in order. Selection and the bulk actions work on all of them.</summary>
    private List<TrashRow> _all = new();

    private readonly MainViewModel _main;

    public bool HasSelection => SelectedCount > 0;
    public bool IsEmpty => _all.Count == 0;
    public bool HasMore => _all.Count > Rows.Count;
    public string MoreLabel => Paging.MoreLabel(_all.Count - Rows.Count);

    public TrashViewModel(MainViewModel main)
    {
        _main = main;
        Reload();
    }

    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(HasSelection));

    public void Reload()
    {
        var workspaces = App.Database.GetAllWorkspaces().ToDictionary(w => w.Id.ToString(), w => w.Name);
        var defaultName = App.Database.GetDefaultWorkspaceName();

        _all = App.Database.GetTrashedTodos().Select(item =>
            {
                var origin = item.IsVault ? "Vault"
                    : item.WorkspaceId != null && workspaces.TryGetValue(item.WorkspaceId, out var name) ? name
                    : defaultName;
                return new TrashRow(item, origin, RecountSelection);
            }).ToList();
        Rows = new ObservableCollection<TrashRow>(_all.Take(Paging.PageSize));

        SelectedCount = 0;
        OnPropertyChanged(nameof(IsEmpty));
        RaisePaging();
        _main.RefreshCounts();
    }

    private void RecountSelection() => SelectedCount = _all.Count(r => r.IsSelected);

    [RelayCommand]
    private void ShowMore()
    {
        foreach (var row in _all.Skip(Rows.Count).Take(Paging.PageSize)) Rows.Add(row);
        RaisePaging();
    }

    private void RaisePaging()
    {
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreLabel));
    }

    [RelayCommand]
    private void Restore(TrashRow? row)
    {
        if (row == null) return;
        RestoreItem(row.Item);
        Reload();
    }

    [RelayCommand]
    private void RestoreSelected()
    {
        foreach (var row in _all.Where(r => r.IsSelected).ToList()) RestoreItem(row.Item);
        Reload();
    }

    private static void RestoreItem(TodoItem item)
    {
        item.Status = item.StatusBeforeTrash
            ?? (item.CompletedAt != null ? TodoStatus.Completed : TodoStatus.Active);
        item.StatusBeforeTrash = null;
        item.TrashedAt = null;
        item.SortOrder = App.Database.GetNextSortOrder(item.WorkspaceId, item.Status);
        App.Database.UpsertTodo(item);
    }

    [RelayCommand]
    private void DeleteForever(TrashRow? row)
    {
        if (row == null) return;
        if (!DialogService.Confirm($"Permanently delete \"{row.Title}\"? This cannot be undone.", "Delete Forever"))
            return;

        App.Database.DeleteTodoPermanently(row.Item.Id);
        Reload();
    }

    [RelayCommand]
    private void DeleteSelectedForever()
    {
        var chosen = _all.Where(r => r.IsSelected).ToList();
        if (chosen.Count == 0) return;
        if (!DialogService.Confirm($"Permanently delete {chosen.Count} item(s)? This cannot be undone.",
                "Delete Forever"))
            return;

        foreach (var row in chosen) App.Database.DeleteTodoPermanently(row.Item.Id);
        Reload();
    }

    [RelayCommand]
    private void EmptyTrash()
    {
        if (_all.Count == 0) return;
        if (!DialogService.Confirm($"Permanently delete all {_all.Count} items in trash? This cannot be undone.",
                "Empty Trash"))
            return;

        foreach (var row in _all.ToList()) App.Database.DeleteTodoPermanently(row.Item.Id);
        Reload();
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        var target = SelectedCount < _all.Count;
        foreach (var row in _all) row.IsSelected = target;
    }
}
