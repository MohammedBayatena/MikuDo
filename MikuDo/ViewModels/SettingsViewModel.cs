using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>
/// A theme in Settings, with a preview drawn in the theme's own colours: read
/// from its dictionary directly, so every preview shows true whichever theme
/// is on screen.
/// </summary>
public partial class ThemeChoice : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public AppTheme Theme { get; }
    public string Id => Theme.Id;
    public string Name => Theme.Name;
    public string Description => Theme.Description;
    public string Kind => Theme.IsDark ? "Dark" : "Light";

    public Brush Canvas { get; }
    public Brush Surface { get; }
    public Brush Border { get; }
    public Brush TextPrimary { get; }
    public Brush TextMuted { get; }
    public Brush AccentFill { get; }
    public Brush OnAccent { get; }
    public Brush AccentSoft { get; }
    public Brush AccentDark { get; }
    public Brush TodoDot { get; }
    public Brush DoingDot { get; }
    public Brush DoneDot { get; }

    public ThemeChoice(AppTheme theme, bool isSelected)
    {
        Theme = theme;
        _isSelected = isSelected;

        var tokens = new System.Windows.ResourceDictionary { Source = theme.Source };
        Brush B(string key) => tokens[key] as Brush ?? Brushes.Transparent;
        Canvas = B("CanvasBrush");
        Surface = B("SurfaceBrush");
        Border = B("BorderBrush");
        TextPrimary = B("TextPrimaryBrush");
        TextMuted = B("TextMutedBrush");
        AccentFill = B("AccentGradientBrush");
        OnAccent = B("OnAccentBrush");
        AccentSoft = B("AccentSoftBrush");
        AccentDark = B("AccentDarkBrush");
        TodoDot = B("StatusTodoDotBrush");
        DoingDot = B("StatusDoingDotBrush");
        DoneDot = B("StatusDoneDotBrush");
    }
}

/// <summary>One downloadable model in Settings, of whichever kind its store holds.</summary>
public partial class AiModelRow : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _progress;

    public AiModelOption Option { get; }
    public IModelStore Store { get; }
    public string DisplayName => Option.DisplayName;
    public string SizeLabel => Option.SizeLabel;
    public string Note => Option.Note;
    public string ProgressLabel => $"{Progress * 100:0}%";

    public AiModelRow(AiModelOption option, IModelStore store)
    {
        Option = option;
        Store = store;
    }

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(ProgressLabel));
}

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _modelFolder;

    /// <summary>
    /// The mode every task page's Dictate starts in: written up by the AI
    /// model, or exactly as spoken. A page can switch for itself.
    /// </summary>
    [ObservableProperty] private bool _isWriteUpOn = App.Database.GetSetting("DictationWriteUp") != "False";

    public MainViewModel Main { get; }
    public ObservableCollection<ThemeChoice> Themes { get; } = new();
    public ObservableCollection<AiModelRow> Models { get; } = new();
    public ObservableCollection<AiModelRow> SpeechModels { get; } = new();

    /// <summary>Whether writing up dictation can happen at all.</summary>
    public bool HasAiModel => App.AiImport.IsModelDownloaded;

    /// <summary>Settings → Keyboard shortcuts.</summary>
    public ShortcutsViewModel KeyboardShortcuts { get; }

    // The page names keys where it mentions a command, so it follows them when they change.
    public string ScreenshotNote => $"Choose whether {KeysOr("capture.screenshot", "Take a screenshot")} captures the primary monitor or every screen";

    public string ThemeNote => "Every theme is checked for contrast: text reaches at least 4.5:1 against what it sits on, and 7:1 in Contrast. "
                               + $"{KeysOr("theme.toggle", "Toggle theme")} switches between the light and dark theme you used last.";

    private static string KeysOr(string commandId, string name)
        => Services.Shortcuts.ChordFor(commandId) is { } chord ? string.Join("+", chord.Caps) : name;

    private void OnKeysChanged()
    {
        OnPropertyChanged(nameof(ScreenshotNote));
        OnPropertyChanged(nameof(ThemeNote));
    }

    /// <summary>Asked of the page: scroll to a part of it, and to a row of the shortcuts waiting for keys, if one is.</summary>
    public event Action<string, ShortcutRow?>? SectionRequested;

    public void Show(string section, string? recordFor = null)
    {
        // Sent to the shortcuts, the section opens for them.
        if (section == SettingsSections.Shortcuts) KeyboardShortcuts.IsExpanded = true;
        var row = recordFor != null ? KeyboardShortcuts.Record(recordFor) : null;
        SectionRequested?.Invoke(section, row);
    }

    /// <summary>The page has a new model: this one stops following changes.</summary>
    public void Detach() => Services.Shortcuts.Changed -= OnKeysChanged;

    private CancellationTokenSource? _cts;

    public SettingsViewModel(MainViewModel main)
    {
        Main = main;
        _modelFolder = Path.GetDirectoryName(App.AiImport.ModelPath) ?? string.Empty;
        KeyboardShortcuts = main.KeyboardShortcuts;
        KeyboardShortcuts.Fresh();
        Services.Shortcuts.Changed += OnKeysChanged;

        foreach (var theme in ThemeCatalog.All) Themes.Add(new ThemeChoice(theme, theme.Id == App.CurrentTheme.Id));
        Main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.ThemeId)) return;
            foreach (var choice in Themes) choice.IsSelected = choice.Id == Main.ThemeId;
        };

        foreach (var option in AiModelCatalog.All) Models.Add(new AiModelRow(option, App.AiImport));
        foreach (var option in SpeechModelCatalog.All) SpeechModels.Add(new AiModelRow(option, App.Dictation));
        RefreshModels();
    }

    private void RefreshModels()
    {
        foreach (var row in Models.Concat(SpeechModels))
        {
            row.IsSelected = row.Option.Id == row.Store.Model.Id;
            row.IsInstalled = row.Store.Exists(row.Option);
        }
        OnPropertyChanged(nameof(HasAiModel));
    }

    partial void OnIsWriteUpOnChanged(bool value)
        => App.Database.SaveSetting("DictationWriteUp", value ? "True" : "False");

    /// <summary>"Exact" or "WriteUp".</summary>
    [RelayCommand]
    private void SetDictationMode(string? mode) => IsWriteUpOn = mode == "WriteUp";

    [RelayCommand]
    private void SelectModel(AiModelRow? row)
    {
        if (row == null) return;
        row.Store.SelectModel(row.Option);
        RefreshModels();
    }

    [RelayCommand]
    private async Task DownloadModel(AiModelRow? row)
    {
        if (row == null || row.IsDownloading) return;

        row.IsDownloading = true;
        row.Progress = 0;
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p => row.Progress = p);
            await row.Store.DownloadModelAsync(row.Option, progress, _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DialogService.Notify($"Download failed:\n{ex.Message}", row.Store.Purpose);
        }
        finally
        {
            row.IsDownloading = false;
            RefreshModels();
        }
    }

    [RelayCommand]
    private void DeleteModel(AiModelRow? row)
    {
        if (row == null || !row.IsInstalled) return;
        if (!DialogService.Confirm($"Delete {row.DisplayName} from disk?", "Remove model")) return;

        try
        {
            row.Store.Unload();   // a loaded model holds its file open
            File.Delete(row.Store.PathFor(row.Option));
        }
        catch (Exception ex)
        {
            DialogService.Notify($"Could not delete the file:\n{ex.Message}", "Remove model");
        }
        RefreshModels();
    }

    [RelayCommand]
    private void OpenModelFolder()
    {
        try
        {
            Directory.CreateDirectory(ModelFolder);
            Process.Start(new ProcessStartInfo(ModelFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DialogService.Notify($"Could not open the folder:\n{ex.Message}", "Settings");
        }
    }

    public void Cancel() => _cts?.Cancel();
}
