using CommunityToolkit.Mvvm.ComponentModel;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>A row in the sidebar WORKSPACE list. A null workspace is the default one.</summary>
public partial class WorkspaceNavItem : ObservableObject
{
    /// <summary>Tasks still to finish: the badge on the row.</summary>
    [ObservableProperty] private int _count;

    /// <summary>The tasks in each list, and when one last changed: what the preview of its square tells.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Breakdown), nameof(ChangedText), nameof(ToDoCount), nameof(DoingCount), nameof(DoneCount))]
    private WorkspaceSummary _summary = new(0, 0, 0, null);

    public string Breakdown => $"{Summary.ToDo} to do · {Summary.Doing} doing · {Summary.Done} done";

    public int ToDoCount => Summary.ToDo;
    public int DoingCount => Summary.Doing;
    public int DoneCount => Summary.Done;

    public string ChangedText => Summary.Changed is { } at ? "Last change " + When.Phrase(at) : "No tasks yet";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _name;

    /// <summary>The row shows a text box in place of its name.</summary>
    [ObservableProperty] private bool _isRenaming;

    /// <summary>What the text box holds until the rename is committed.</summary>
    [ObservableProperty] private string _draftName = string.Empty;

    public Workspace? Workspace { get; }
    public string? Id => Workspace?.Id.ToString();
    public bool CanDelete => Workspace != null;

    public WorkspaceNavItem(Workspace? workspace, string defaultName)
    {
        Workspace = workspace;
        _name = workspace == null || string.IsNullOrWhiteSpace(workspace.Name) ? defaultName : workspace.Name;
    }

    /// <summary>Takes the workspace's figures as last counted; one with no tasks has none.</summary>
    public void Take(WorkspaceSummary? summary)
    {
        Summary = summary ?? new WorkspaceSummary(0, 0, 0, null);
        Count = Summary.Open;
    }
}
