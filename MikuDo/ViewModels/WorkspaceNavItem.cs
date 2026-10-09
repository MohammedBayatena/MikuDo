using CommunityToolkit.Mvvm.ComponentModel;
using MikuDo.Models;

namespace MikuDo.ViewModels;

/// <summary>A row in the sidebar WORKSPACE list. A null workspace is the default one.</summary>
public partial class WorkspaceNavItem : ObservableObject
{
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _name;

    /// <summary>The row shows a text box in place of its name.</summary>
    [ObservableProperty] private bool _isRenaming;

    /// <summary>What the text box holds until the rename is committed.</summary>
    [ObservableProperty] private string _draftName = string.Empty;

    public Workspace? Workspace { get; }
    public string? Id => Workspace?.Id.ToString();
    public bool CanDelete => Workspace != null;

    public WorkspaceNavItem(Workspace? workspace, int count, string defaultName)
    {
        Workspace = workspace;
        _name = workspace == null || string.IsNullOrWhiteSpace(workspace.Name) ? defaultName : workspace.Name;
        _count = count;
    }
}
