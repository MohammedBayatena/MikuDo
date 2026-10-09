using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>Where the find bar looks when a page shows both: the page as it reads, or its Markdown.</summary>
public enum FindScope { Preview, Markdown }

/// <summary>
/// The find bar over a note or a task: what is looked for and how, and where
/// the search stands. The page it belongs to does the searching; this says
/// when to, and shows the answer. The words and modes carry from one bar to
/// the next, so a search goes on in the next note opened.
/// </summary>
public sealed partial class FindViewModel : ObservableObject
{
    private static string _lastText = string.Empty;
    private static bool _lastCase, _lastWord, _lastRegex;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _text = _lastText;
    [ObservableProperty] private bool _matchCase = _lastCase;
    [ObservableProperty] private bool _wholeWord = _lastWord;
    [ObservableProperty] private bool _useRegex = _lastRegex;
    [ObservableProperty] private FindScope _scope;

    /// <summary>True while the page shows both its Markdown and its preview, so either can be searched.</summary>
    [ObservableProperty] private bool _canChooseScope;

    /// <summary>"Find in note", "Find in task".</summary>
    [ObservableProperty] private string _placeholder = "Find";

    [ObservableProperty] private int _count;
    [ObservableProperty] private int _index = -1;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private string? _error;

    public FindPattern Pattern => new(Text, MatchCase, WholeWord, UseRegex);

    public bool HasText => Text.Length > 0;
    public bool HasError => Error != null;
    public bool HasMatches => Count > 0;
    public bool IsPreview => Scope == FindScope.Preview;
    public bool IsMarkdown => Scope == FindScope.Markdown;

    /// <summary>"3 of 12", "No results", or why the regex cannot be used.</summary>
    public string ResultLabel
        => Error ?? (!HasText ? string.Empty
                     : Count == 0 ? "No results"
                     : $"{(Index < 0 ? "?" : (Index + 1).ToString("N0"))} of {Count:N0}{(HasMore ? "+" : "")}");

    /// <summary>The words or a mode changed, or the scope did: the page searches again.</summary>
    public event Action? SearchChanged;

    /// <summary>Next (+1) or previous (-1).</summary>
    public event Action<int>? StepRequested;

    /// <summary>The bar is closing: the page takes its highlights off, and takes back the caret if the bar had it.</summary>
    public event Action? Closing;

    /// <summary>The page has searched: how many matches, which one is current.</summary>
    public void Show(int count, int index, bool more, string? error)
    {
        Error = error;
        HasMore = more;
        Count = count;
        Index = count == 0 ? -1 : index;
        OnPropertyChanged(nameof(ResultLabel));
    }

    partial void OnTextChanged(string value)
    {
        _lastText = value;
        OnPropertyChanged(nameof(HasText));
        Changed();
    }

    partial void OnMatchCaseChanged(bool value) { _lastCase = value; Changed(); }
    partial void OnWholeWordChanged(bool value) { _lastWord = value; Changed(); }
    partial void OnUseRegexChanged(bool value) { _lastRegex = value; Changed(); }

    partial void OnScopeChanged(FindScope value)
    {
        OnPropertyChanged(nameof(IsPreview));
        OnPropertyChanged(nameof(IsMarkdown));
        Changed();
    }

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(HasMatches));

    private void Changed()
    {
        if (IsOpen) SearchChanged?.Invoke();
    }

    [RelayCommand] private void Next() => StepRequested?.Invoke(1);
    [RelayCommand] private void Previous() => StepRequested?.Invoke(-1);

    [RelayCommand] private void ToggleMatchCase() => MatchCase = !MatchCase;
    [RelayCommand] private void ToggleWholeWord() => WholeWord = !WholeWord;
    [RelayCommand] private void ToggleRegex() => UseRegex = !UseRegex;

    /// <summary>"Preview" or "Markdown", as the bar's two buttons name them.</summary>
    [RelayCommand] private void SetScope(string? scope) => Scope = scope == nameof(FindScope.Markdown) ? FindScope.Markdown : FindScope.Preview;

    [RelayCommand]
    public void Close()
    {
        if (!IsOpen) return;
        Closing?.Invoke();
        IsOpen = false;
        Show(0, -1, false, null);
    }
}
