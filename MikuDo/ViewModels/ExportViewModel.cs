using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Services;
using Microsoft.Win32;

namespace MikuDo.ViewModels;

/// <summary>The Export to Markdown modal: builds the text, then offers to copy or save it.</summary>
public partial class ExportViewModel : ObservableObject
{
    [ObservableProperty] private string _markdown = string.Empty;
    [ObservableProperty] private bool _isWorking = true;
    [ObservableProperty] private string _status = "Building the list…";
    [ObservableProperty] private string _notice = string.Empty;

    private readonly MainViewModel _main;
    private readonly CancellationTokenSource _cts = new();

    public string Title { get; }
    public string Subtitle { get; }

    /// <summary>The text is finished and there is some.</summary>
    public bool IsReady => !IsWorking && Markdown.Length > 0;

    partial void OnIsWorkingChanged(bool value) => OnPropertyChanged(nameof(IsReady));
    partial void OnMarkdownChanged(string value) => OnPropertyChanged(nameof(IsReady));

    public ExportViewModel(MainViewModel main, string title, IReadOnlyList<ExportGroup> groups)
    {
        _main = main;
        Title = title;

        var count = groups.Sum(g => g.Items.Count);
        var longOnes = groups.SelectMany(g => g.Items).Count(MarkdownExport.NeedsSummary);
        Subtitle = (count == 1 ? "1 task" : $"{count} tasks")
                   + (longOnes == 0 ? string.Empty
                      : $" · {longOnes} long description{(longOnes == 1 ? "" : "s")} will be summarized");

        _ = BuildAsync(groups);
    }

    private async Task BuildAsync(IReadOnlyList<ExportGroup> groups)
    {
        var progress = new Progress<string>(s => Status = s);
        try
        {
            // The model decodes on whatever thread drives it, so summaries run
            // on the pool and only the finished text comes back here.
            Markdown = await MarkdownExport.BuildAsync(
                Title, groups,
                (text, ct) => Task.Run(() => App.AiImport.SummarizeAsync(text, ct), ct),
                progress, _cts.Token);
            Status = string.Empty;
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
        }
        catch (Exception ex)
        {
            LogService.Error("Export failed", ex);
            Status = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private void Copy()
    {
        if (Markdown.Length == 0) return;
        try
        {
            Clipboard.SetText(Markdown);
            Notice = "Copied to the clipboard";
        }
        catch (Exception ex)
        {
            // Another program holding the clipboard open is the usual cause.
            LogService.Error("Clipboard copy failed", ex);
            Notice = "The clipboard is busy; try again";
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (Markdown.Length == 0) return;

        var dialog = new SaveFileDialog
        {
            Title = "Save as Markdown",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            FileName = SafeFileName(Title) + ".md"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, Markdown);
            Notice = $"Saved {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            LogService.Error("Export save failed", ex);
            Notice = $"Could not save: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close() => _main.CloseExportCommand.Execute(null);

    public void Cancel() => _cts.Cancel();

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "tasks" : cleaned;
    }
}
