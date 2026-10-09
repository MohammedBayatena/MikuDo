using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;
using Microsoft.Win32;

namespace MikuDo.ViewModels;

/// <summary>A task the importer suggests. The user edits and ticks before saving.</summary>
public partial class ProposedTask : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _description;

    public ExtractedTask Source { get; }
    public string StatusName => Palette.ColumnName(Source.Status);
    public System.Windows.Media.Brush StatusForeground => Palette.StatusForeground(Source.Status);
    public List<TagChip> Labels { get; }
    public string SubtaskLabel => Source.Subtasks.Count == 0
        ? string.Empty
        : $"{Source.Subtasks.Count} subtask{(Source.Subtasks.Count == 1 ? "" : "s")}";
    public string PriorityLabel => Source.Priority == TodoPriority.None ? string.Empty : Source.Priority.ToString();

    public ProposedTask(ExtractedTask source)
    {
        Source = source;
        _title = source.Title;
        _description = source.Description;
        Labels = source.Labels.Select(l => new TagChip(l)).ToList();
    }
}

public partial class AiImportViewModel : ObservableObject
{
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _fileLabel = "No file chosen";
    [ObservableProperty] private string _stage = "Input";     // Input | Downloading | Working | Preview
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private bool _usedFallback;
    [ObservableProperty] private ObservableCollection<ProposedTask> _proposals = new();

    private readonly MainViewModel _main;
    private readonly string? _workspaceId;
    private CancellationTokenSource? _cts;

    public string WorkspaceLabel { get; }
    public string ModelName => App.AiImport.Model.DisplayName;
    public string ModelSizeLabel => App.AiImport.Model.SizeLabel;

    public bool IsInput => Stage == "Input";
    public bool IsDownloading => Stage == "Downloading";
    public bool IsWorking => Stage == "Working";
    public bool IsPreview => Stage == "Preview";

    public bool NeedsModel => !App.AiImport.IsModelDownloaded;

    /// <summary>Why the model did not take part, shown when the rules ran alone.</summary>
    public string FallbackReason => App.AiImport.LastError is { Length: > 0 } error
        ? $"The model could not run ({error}) — keyword rules were used."
        : "No model installed — status, priority and labels came from keyword rules.";
    public string DownloadPercentLabel => $"{DownloadProgress * 100:0}%";
    public int SelectedCount => Proposals.Count(p => p.IsSelected);
    public string CreateLabel => SelectedCount == 1 ? "Create 1 task" : $"Create {SelectedCount} tasks";
    public bool CanConvert => !string.IsNullOrWhiteSpace(InputText);

    public AiImportViewModel(MainViewModel main, string? workspaceId)
    {
        _main = main;
        _workspaceId = workspaceId;
        WorkspaceLabel = App.Database.GetWorkspaceById(workspaceId ?? "")?.Name
                         ?? App.Database.GetDefaultWorkspaceName();
    }

    partial void OnStageChanged(string value)
    {
        OnPropertyChanged(nameof(IsInput));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(IsPreview));
    }

    partial void OnInputTextChanged(string value) => OnPropertyChanged(nameof(CanConvert));

    partial void OnDownloadProgressChanged(double value) => OnPropertyChanged(nameof(DownloadPercentLabel));

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CreateLabel));
    }

    // ── Input ───────────────────────────────────────────────────

    [RelayCommand]
    private void PickFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a note",
            Filter = "Text notes|*.txt;*.md;*.markdown;*.csv;*.log|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            InputText = File.ReadAllText(dlg.FileName);
            FileLabel = Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex)
        {
            DialogService.Notify($"Could not read the file:\n{ex.Message}", "AI Import");
        }
    }

    // ── Model download ──────────────────────────────────────────

    [RelayCommand]
    private async Task DownloadModel()
    {
        Stage = "Downloading";
        DownloadProgress = 0;
        StatusText = $"Downloading {ModelName}…";
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);
            await App.AiImport.DownloadModelAsync(App.AiImport.Model, progress, _cts.Token);
            OnPropertyChanged(nameof(NeedsModel));
            Stage = "Input";
            StatusText = string.Empty;
        }
        catch (OperationCanceledException)
        {
            Stage = "Input";
            StatusText = string.Empty;
        }
        catch (Exception ex)
        {
            Stage = "Input";
            StatusText = string.Empty;
            DialogService.Notify(
                $"The model could not be downloaded:\n{ex.Message}\n\nImport still works using keyword rules.",
                "AI Import");
        }
    }

    // ── Conversion ──────────────────────────────────────────────

    [RelayCommand]
    private async Task Convert()
    {
        if (!CanConvert) return;

        Stage = "Working";
        UsedFallback = !App.AiImport.IsModelDownloaded;
        StatusText = UsedFallback ? "Reading your notes…" : "Starting the on-device model…";
        _cts = new CancellationTokenSource();

        try
        {
            var status = new Progress<string>(s => StatusText = s);
            var text = InputText;
            var results = await Task.Run(() => App.AiImport.ExtractAsync(text, status, _cts.Token), _cts.Token);

            Proposals = new ObservableCollection<ProposedTask>(results.Select(r => new ProposedTask(r)));
            foreach (var p in Proposals) p.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProposedTask.IsSelected)) NotifySelectionChanged();
            };

            UsedFallback = !App.AiImport.IsModelLoaded;
            OnPropertyChanged(nameof(FallbackReason));
            NotifySelectionChanged();

            if (Proposals.Count == 0)
            {
                Stage = "Input";
                StatusText = "No tasks found in that text.";
                return;
            }
            Stage = "Preview";
            StatusText = string.Empty;
        }
        catch (OperationCanceledException)
        {
            Stage = "Input";
            StatusText = string.Empty;
        }
        catch (Exception ex)
        {
            Stage = "Input";
            StatusText = string.Empty;
            DialogService.Notify($"Import failed:\n{ex.Message}", "AI Import");
        }
    }

    [RelayCommand]
    private void BackToInput()
    {
        Stage = "Input";
        StatusText = string.Empty;
    }

    [RelayCommand]
    private void SelectAll()
    {
        var target = SelectedCount < Proposals.Count;
        foreach (var p in Proposals) p.IsSelected = target;
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void RemoveProposal(ProposedTask? proposal)
    {
        if (proposal == null) return;
        Proposals.Remove(proposal);
        NotifySelectionChanged();
        if (Proposals.Count == 0) Stage = "Input";
    }

    [RelayCommand]
    private void SaveTasks()
    {
        var chosen = Proposals.Where(p => p.IsSelected).ToList();
        if (chosen.Count == 0) return;

        foreach (var proposal in chosen)
        {
            var source = proposal.Source;
            var title = proposal.Title.Trim();
            if (title.Length == 0) continue;

            // A subtask is a task, so each indented bullet becomes one and is
            // linked; one ticked in the note ("[x]") arrives done.
            var childIds = new List<string>();
            for (var i = 0; i < source.Subtasks.Count; i++)
            {
                var line = source.Subtasks[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var done = i < source.SubtasksDone.Count && source.SubtasksDone[i];
                var childStatus = done ? TodoStatus.Completed : TodoStatus.Active;
                var child = new TodoItem
                {
                    Title = line.Trim(),
                    Status = childStatus,
                    CompletedAt = done ? DateTime.UtcNow : null,
                    WorkspaceId = _workspaceId,
                    SortOrder = App.Database.GetNextSortOrder(_workspaceId, childStatus)
                };
                App.Database.UpsertTodo(child);
                childIds.Add(child.IdText);
            }

            App.Database.UpsertTodo(new TodoItem
            {
                Title = title,
                Description = proposal.Description.Trim(),
                Status = source.Status,
                Priority = source.Priority,
                WorkspaceId = _workspaceId,
                Tags = source.Labels,
                SubtaskIds = childIds,
                SortOrder = App.Database.GetNextSortOrder(_workspaceId, source.Status),
                CompletedAt = source.Status == TodoStatus.Completed ? DateTime.UtcNow : null
            });
        }

        _main.CloseAiImportCommand.Execute(null);
        _main.AfterTasksCreated(_workspaceId);
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }
}
