using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MikuDo.ViewModels;

/// <summary>The sidebar's own state: its width, and how its sections are folded.</summary>
public partial class MainViewModel
{
    public const double DefaultSidebarWidth = 236;

    /// <summary>Narrow enough to give the page room, wide enough still for a row of workspace squares.</summary>
    public const double MinSidebarWidth = 210;

    public const double MaxSidebarWidth = 440;

    /// <summary>Room a workspace square takes in the row: the square and the gap after it.</summary>
    public const double WorkspaceSquareSlot = 38 + 6;

    /// <summary>
    /// What the sidebar keeps for itself beside the row of squares: its padding
    /// either side, the row's own margin, and room for its scroll bar.
    /// </summary>
    private const double SquareRowChrome = 14 * 2 + 2 + 17;

    /// <summary>
    /// Squares that fit across the sidebar as it is now wide, never fewer than
    /// two. With more workspaces than that, the last square counts the rest.
    /// Worked out from the sidebar's width, not the row's, since the row grows
    /// with the squares in it.
    /// </summary>
    public int WorkspaceSquareCount => Math.Max(2, (int)((SidebarWidth - SquareRowChrome + 6) / WorkspaceSquareSlot));

    /// <summary>The open sidebar's width, as last dragged.</summary>
    [ObservableProperty] private double _sidebarWidth = StoredSidebarWidth();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWorkspaceList), nameof(ShowWorkspaceSquares))]
    private bool _isWorkspacesOpen = App.Database.GetSetting("SidebarWorkspacesOpen") != "False";

    /// <summary>Workspaces as one row of squares rather than the full list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWorkspaceList), nameof(ShowWorkspaceSquares))]
    private bool _isWorkspacesCompact = App.Database.GetSetting("SidebarWorkspacesCompact") == "True";

    [ObservableProperty] private bool _isNotesOpen = App.Database.GetSetting("SidebarNotesOpen") != "False";

    public bool ShowWorkspaceList => IsWorkspacesOpen && !IsWorkspacesCompact;
    public bool ShowWorkspaceSquares => IsWorkspacesOpen && IsWorkspacesCompact;

    /// <summary>The workspaces in the row of squares: the first few, always with the active one among them.</summary>
    [ObservableProperty] private IReadOnlyList<WorkspaceNavItem> _workspaceSquares = Array.Empty<WorkspaceNavItem>();

    /// <summary>Workspaces left out of the row of squares, counted on its last square.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreWorkspaces))]
    private int _moreWorkspaces;

    public bool HasMoreWorkspaces => MoreWorkspaces > 0;

    private static double StoredSidebarWidth()
        => double.TryParse(App.Database.GetSetting("SidebarWidth"), NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            ? Math.Clamp(width, MinSidebarWidth, MaxSidebarWidth)
            : DefaultSidebarWidth;

    public void SaveSidebarWidth()
        => App.Database.SaveSetting("SidebarWidth", SidebarWidth.ToString("0", CultureInfo.InvariantCulture));

    partial void OnIsWorkspacesOpenChanged(bool value)
        => App.Database.SaveSetting("SidebarWorkspacesOpen", value ? "True" : "False");

    partial void OnIsWorkspacesCompactChanged(bool value)
        => App.Database.SaveSetting("SidebarWorkspacesCompact", value ? "True" : "False");

    partial void OnIsNotesOpenChanged(bool value)
        => App.Database.SaveSetting("SidebarNotesOpen", value ? "True" : "False");

    [RelayCommand]
    private void ToggleWorkspacesSection() => IsWorkspacesOpen = !IsWorkspacesOpen;

    [RelayCommand]
    private void ToggleNotesSection() => IsNotesOpen = !IsNotesOpen;

    [RelayCommand]
    private void ToggleWorkspacesCompact()
    {
        IsWorkspacesCompact = !IsWorkspacesCompact;
        IsWorkspacesOpen = true;
    }

    /// <summary>The full list again, from the row of squares.</summary>
    [RelayCommand]
    private void ShowAllWorkspaces()
    {
        IsWorkspacesCompact = false;
        IsWorkspacesOpen = true;
    }

    /// <summary>The row of squares is <paramref name="width"/> wide: as many squares as fit, never fewer than two.</summary>
    /// <summary>A wider or narrower sidebar fits more or fewer squares.</summary>
    partial void OnSidebarWidthChanged(double value) => SyncWorkspaceSquares();

    /// <summary>Fills the row of squares; the set changes only when a workspace comes, goes or becomes active.</summary>
    private void SyncWorkspaceSquares()
    {
        var all = Workspaces;
        List<WorkspaceNavItem> shown;
        if (all.Count <= WorkspaceSquareCount)
        {
            shown = all.ToList();
        }
        else
        {
            shown = all.Take(WorkspaceSquareCount - 1).ToList();
            if (all.FirstOrDefault(w => w.IsActive) is { } active && !shown.Contains(active)) shown[^1] = active;
        }

        if (!shown.SequenceEqual(WorkspaceSquares)) WorkspaceSquares = shown;
        MoreWorkspaces = all.Count - shown.Count;
    }
}
