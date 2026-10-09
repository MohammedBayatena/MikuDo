using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>One of the kind labels on a suggestion's row; the chosen one is filled in.</summary>
public partial class KindChoice : ObservableObject
{
    [ObservableProperty] private bool _isChosen;

    public string Label { get; }
    public Brush Background { get; }
    public Brush Foreground { get; }
    public LabelSuggestion Row { get; }

    public KindChoice(LabelSuggestion row, string label, bool chosen)
    {
        Row = row;
        Label = label;
        _isChosen = chosen;
        var colors = Palette.Label(label);
        Background = colors.Background;
        Foreground = colors.Foreground;
    }
}

/// <summary>A task and the kind label suggested for it. A ticked row gets its chosen label on Apply.</summary>
public partial class LabelSuggestion : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public TodoItem Item { get; }
    public string Title => Item.Title;

    /// <summary>The labels the task already carries.</summary>
    public List<TagChip> Labels { get; }
    public bool HasLabels => Labels.Count > 0;

    public List<KindChoice> Choices { get; }

    /// <summary>The AI or the rules had an answer; otherwise the row waits for the user to pick one.</summary>
    public bool WasSuggested { get; }

    public string? Chosen => Choices.FirstOrDefault(c => c.IsChosen)?.Label;

    public LabelSuggestion(TodoItem item, string? suggested)
    {
        Item = item;
        WasSuggested = suggested != null;
        _isSelected = WasSuggested;
        Labels = item.Tags.Select(t => new TagChip(t)).ToList();
        Choices = AiImportService.KindLabels.Select(l => new KindChoice(this, l, l == suggested)).ToList();
    }

    /// <summary>Picks a label for this task, ticking the row; picking the chosen one again clears it.</summary>
    public void Choose(KindChoice choice)
    {
        var picking = !choice.IsChosen;
        foreach (var c in Choices) c.IsChosen = picking && ReferenceEquals(c, choice);
        IsSelected = picking;
    }
}

/// <summary>
/// Suggests a kind label (Bug, Enhancement, Feature, Think About) for every
/// task in a list or a selection that has none yet, for the user to check
/// and apply in one go.
/// </summary>
public partial class LabelSuggestViewModel : ObservableObject
{
    [ObservableProperty] private string _stage = "Working";   // Working | Review | Empty
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _usedRulesOnly;

    private readonly MainViewModel _main;
    private readonly string? _workspaceId;
    private readonly List<TodoItem> _tasks;
    private CancellationTokenSource? _cts;

    /// <summary>What the labels are for: a list's name, or how many tasks are selected.</summary>
    public string Scope { get; }

    /// <summary>Tasks left out because they already carry a kind label.</summary>
    public int AlreadyLabelled { get; }

    public ObservableCollection<LabelSuggestion> Suggestions { get; } = new();

    public bool IsWorking => Stage == "Working";
    public bool IsReview => Stage == "Review";
    public bool IsEmpty => Stage == "Empty";

    public string Subtitle => _tasks.Count == 1 ? "1 task without a kind label" : $"{_tasks.Count} tasks without a kind label";

    public int SelectedCount => Suggestions.Count(s => s.IsSelected && s.Chosen != null);
    public bool CanApply => SelectedCount > 0;
    public string ApplyLabel => SelectedCount == 1 ? "Add 1 label" : $"Add {SelectedCount} labels";

    public string Notice
    {
        get
        {
            var skipped = AlreadyLabelled switch
            {
                0 => string.Empty,
                1 => "1 task already has a kind label and is left out. ",
                _ => $"{AlreadyLabelled} tasks already have a kind label and are left out. "
            };
            var how = UsedRulesOnly
                ? "No AI model installed, so these come from keywords only; get the model in Settings."
                : "Suggested on this PC by the AI Import model. Click a label to change it.";
            return skipped + how;
        }
    }

    public string EmptyText => AlreadyLabelled > 0
        ? "Every task here already has a kind label."
        : "There are no tasks here to label.";

    public LabelSuggestViewModel(MainViewModel main, string? workspaceId, string scope, IReadOnlyCollection<TodoItem> tasks)
    {
        _main = main;
        _workspaceId = workspaceId;
        Scope = scope;
        _tasks = tasks.Where(t => !t.IsVault && !t.Tags.Any(IsKind)).ToList();
        AlreadyLabelled = tasks.Count - _tasks.Count;
    }

    private static bool IsKind(string tag)
        => AiImportService.KindLabels.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase);

    partial void OnStageChanged(string value)
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(IsReview));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnUsedRulesOnlyChanged(bool value) => OnPropertyChanged(nameof(Notice));

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyLabel));
    }

    // ── Suggesting ──────────────────────────────────────────────

    private static readonly Regex Media = new(@"!?\[[^\]]*\]\((?:img|voice)://\d+\)", RegexOptions.Compiled);
    private static readonly Regex Space = new(@"\s+", RegexOptions.Compiled);

    /// <summary>What the model reads of a task: its title, then its description without media links.</summary>
    private static string TextOf(TodoItem task)
    {
        var description = Space.Replace(Media.Replace(task.Description ?? string.Empty, " "), " ").Trim();
        return description.Length == 0 ? task.Title : $"{task.Title}. {description}";
    }

    public async Task RunAsync()
    {
        if (_tasks.Count == 0)
        {
            Stage = "Empty";
            return;
        }

        UsedRulesOnly = !App.AiImport.IsModelDownloaded;
        StatusText = UsedRulesOnly ? "Reading your tasks…" : "Starting the on-device model…";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var texts = _tasks.Select(TextOf).ToList();
            var status = new Progress<string>(s => StatusText = s);
            var kinds = await Task.Run(() => App.AiImport.SuggestKindsAsync(texts, status, token), token);
            if (token.IsCancellationRequested) return;

            UsedRulesOnly = !App.AiImport.IsModelLoaded;
            var rows = _tasks.Select((task, i) => new LabelSuggestion(task, kinds[i]))
                             .OrderBy(r => r.WasSuggested ? 0 : 1)
                             .ToList();
            foreach (var row in rows)
            {
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(LabelSuggestion.IsSelected)) RaiseSelection();
                };
                Suggestions.Add(row);
            }

            Stage = "Review";
            RaiseSelection();
        }
        catch (OperationCanceledException)
        {
            // Closed while it was still reading.
        }
        catch (Exception ex)
        {
            _main.CloseLabelSuggestCommand.Execute(null);
            DialogService.Notify($"Labels could not be suggested:\n{ex.Message}", "Suggest labels");
        }
    }

    [RelayCommand]
    private void Choose(KindChoice? choice) => choice?.Row.Choose(choice);

    [RelayCommand]
    private void SelectAll()
    {
        var ready = Suggestions.Where(s => s.Chosen != null).ToList();
        var target = ready.Any(s => !s.IsSelected);
        foreach (var row in ready) row.IsSelected = target;
    }

    /// <summary>Adds each ticked row's label to its task, as one step Undo takes back.</summary>
    [RelayCommand]
    private void Apply()
    {
        var snapshots = new List<TodoItem>();
        foreach (var row in Suggestions.Where(s => s.IsSelected && s.Chosen != null))
        {
            // Read afresh, so an edit made since the dialog opened is kept.
            var task = App.Database.GetTodoById(row.Item.Id);
            if (task == null || task.Tags.Contains(row.Chosen!, StringComparer.OrdinalIgnoreCase)) continue;

            snapshots.Add(task.Snapshot());
            task.Tags = task.Tags.Append(row.Chosen!).ToList();
            App.Database.UpsertTodo(task);
        }

        _main.CloseLabelSuggestCommand.Execute(null);
        if (snapshots.Count == 0) return;

        _main.PushUndo(snapshots.Count == 1 ? "add label" : "add labels", _workspaceId, () =>
        {
            foreach (var snapshot in snapshots) App.Database.UpsertTodo(snapshot);
        });
        _main.RefreshCurrentPage();
    }

    [RelayCommand]
    private void Close() => _main.CloseLabelSuggestCommand.Execute(null);

    public void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }
}
