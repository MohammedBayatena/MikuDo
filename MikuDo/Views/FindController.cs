using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using MikuDo.Controls;
using MikuDo.Services;
using MikuDo.ViewModels;

namespace MikuDo.Views;

/// <summary>
/// Find on a page that shows Markdown in an editor, the preview it makes, or
/// both side by side. The find bar says what to look for; this searches the
/// side being searched, marks every match there, and goes from one to the next.
/// </summary>
/// <remarks>
/// The editor is searched here, in its text as typed, and its matches drawn
/// under the text. The preview is searched by the page itself, in the text as
/// it reads (<see cref="FindScript"/>); its answers come back a moment later,
/// and one a newer search has overtaken is dropped.
/// </remarks>
internal sealed class FindController
{
    private readonly FindViewModel _find;
    private readonly FindBar _bar;
    private readonly TextBox _editor;
    private readonly FindHighlights _marks;
    private readonly WebView2 _preview;
    private readonly Func<bool> _previewReady;
    private readonly Func<bool> _editorShown;
    private readonly Func<bool> _previewShown;

    /// <summary>The editor's matches, in order, and the current one.</summary>
    private List<(int Start, int Length)> _matches = new();
    private int _active = -1;

    /// <summary>The preview's current match, by number, so a page drawn again keeps it.</summary>
    private int _previewActive = -1;

    /// <summary>Bumped by every call to the preview: only the newest answer is shown.</summary>
    private int _version;

    /// <summary>Set while the bar is being set up, so it searches once at the end rather than at every change.</summary>
    private bool _settingUp;

    public FindController(FindViewModel find, FindBar bar, TextBox editor, FindHighlights marks, WebView2 preview,
                          Func<bool> previewReady, Func<bool> editorShown, Func<bool> previewShown)
    {
        _find = find;
        _bar = bar;
        _editor = editor;
        _marks = marks;
        _preview = preview;
        _previewReady = previewReady;
        _editorShown = editorShown;
        _previewShown = previewShown;

        bar.DataContext = find;
        marks.Attach(editor);
        find.SearchChanged += () => Search(reveal: true);
        find.StepRequested += Step;
        find.Closing += OnClosing;
        editor.TextChanged += (_, _) =>
        {
            if (_find.IsOpen && _find.Scope == FindScope.Markdown) SearchEditor(keep: true, reveal: false);
        };
    }

    public bool IsOpen => _find.IsOpen;

    /// <summary>
    /// Opens the bar, or brings the caret back to it. It looks for
    /// <paramref name="text"/> when given; else for a word or phrase picked out
    /// in the editor, else for whatever it looked for last.
    /// </summary>
    public void Open(string? text = null)
    {
        var fromEditor = _editor.IsKeyboardFocusWithin;
        if (text == null && fromEditor && _editor.SelectionLength is > 0 and <= 200 && !_editor.SelectedText.Contains('\n'))
            text = _editor.SelectedText;

        var wasOpen = _find.IsOpen;
        _settingUp = true;
        try
        {
            ChooseScope(wasOpen ? null : fromEditor);
            if (text != null) _find.Text = text;
            _find.IsOpen = true;
        }
        finally
        {
            _settingUp = false;
        }
        Search(reveal: true);
        _bar.FocusBox();
    }

    /// <summary>The next match (+1) or the one before (-1). A closed bar opens on what it looked for last.</summary>
    public void Step(int step)
    {
        if (!_find.IsOpen)
        {
            Open();
            return;
        }

        if (_find.Scope == FindScope.Markdown)
        {
            if (_matches.Count == 0) return;
            _active = ((_active + step) % _matches.Count + _matches.Count) % _matches.Count;
            _marks.Show(_matches, _active);
            _find.Show(_matches.Count, _active, _find.HasMore, _find.Error);
            RevealInEditor();
        }
        else
        {
            CallPreview($"window.mikudoFindStep ? window.mikudoFindStep({step}) : null");
        }
    }

    /// <summary>Ends the search: the bar closes and the marks go, leaving the caret where it is.</summary>
    public void Close()
    {
        if (!_find.IsOpen) return;
        _keepFocus = true;
        _find.Close();
        _keepFocus = false;
    }

    private bool _keepFocus;

    /// <summary>The preview has drawn the page again: the search is run on it again, at the same match.</summary>
    public void PreviewReloaded()
    {
        if (_find.IsOpen && _find.Scope == FindScope.Preview) SearchPreview(_previewActive, reveal: false);
    }

    /// <summary>
    /// Another note came onto the page with the bar open: the search goes on
    /// in it from its top, on whichever of its sides it shows.
    /// </summary>
    public void PageChanged()
    {
        _active = -1;
        _previewActive = -1;
        LayoutChanged();
    }

    /// <summary>The page switched between Edit, Split and Preview: the bar searches what is still there.</summary>
    public void LayoutChanged()
    {
        if (!_find.IsOpen) return;
        var scope = _find.Scope;
        _settingUp = true;
        try { ChooseScope(null); }
        finally { _settingUp = false; }
        if (_find.Scope != scope) Search(reveal: true);
    }

    /// <summary>
    /// Which side is searched. With only one shown, that one; with both, the
    /// one the user is in when the bar opens (<paramref name="fromEditor"/>),
    /// or the one already chosen.
    /// </summary>
    private void ChooseScope(bool? fromEditor)
    {
        var editor = _editorShown();
        var preview = _previewShown();
        _find.CanChooseScope = editor && preview;
        if (editor && !preview) _find.Scope = FindScope.Markdown;
        else if (preview && !editor) _find.Scope = FindScope.Preview;
        else if (fromEditor is { } inEditor) _find.Scope = inEditor ? FindScope.Markdown : FindScope.Preview;
    }

    private void Search(bool reveal)
    {
        if (_settingUp || !_find.IsOpen) return;
        if (_find.Scope == FindScope.Markdown)
        {
            ClearPreview();
            SearchEditor(keep: false, reveal);
        }
        else
        {
            _matches = new();
            _active = -1;
            _marks.Clear();
            SearchPreview(-1, reveal);
        }
    }

    // ── The editor ──────────────────────────────────────────────

    /// <summary>
    /// Searches the editor's text. A new search goes to the first match from
    /// where the last one was (or from the top of the view), so adding letters
    /// to the words keeps the place; <paramref name="keep"/> holds on to the
    /// current match by number, as while the text is being edited.
    /// </summary>
    private void SearchEditor(bool keep, bool reveal)
    {
        var from = _active >= 0 && _active < _matches.Count ? _matches[_active].Start : FirstShownCharacter();
        var keptAt = _active;

        _matches = _find.Pattern.Matches(_editor.Text, out var error, out var more);
        if (_matches.Count == 0) _active = -1;
        else if (keep && keptAt >= 0) _active = Math.Min(keptAt, _matches.Count - 1);
        else
        {
            _active = _matches.FindIndex(m => m.Start >= from);
            if (_active < 0) _active = 0;
        }

        _marks.Show(_matches, _active);
        _find.Show(_matches.Count, _active, more, error);
        if (reveal && _active >= 0) RevealInEditor();
    }

    private int FirstShownCharacter()
    {
        var line = _editor.GetFirstVisibleLineIndex();
        return line < 0 ? 0 : _editor.GetCharacterIndexFromLineIndex(line);
    }

    /// <summary>Scrolls the current match into view, a third of the way down, unless it is in view already.</summary>
    private void RevealInEditor()
    {
        if (_active < 0 || _active >= _matches.Count) return;
        var start = _matches[_active].Start;
        if (start > _editor.Text.Length) return;

        // An editor not laid out yet knows no lines; it is scrolled once it has them.
        var line = _editor.GetLineIndexFromCharacterIndex(start);
        if (line < 0) return;
        var first = _editor.GetFirstVisibleLineIndex();
        var last = _editor.GetLastVisibleLineIndex();
        if (first >= 0 && line > first && line < last) return;

        _editor.ScrollToLine(line);
        _editor.UpdateLayout();
        var at = _editor.GetRectFromCharacterIndex(start);
        if (!at.IsEmpty)
            _editor.ScrollToVerticalOffset(Math.Max(0, _editor.VerticalOffset + at.Top - _editor.ViewportHeight / 3));
    }

    // ── The preview ─────────────────────────────────────────────

    private void SearchPreview(int keep, bool reveal)
    {
        var pattern = JsonSerializer.Serialize(_find.Pattern.ForScript());
        CallPreview($"window.mikudoFind ? window.mikudoFind({pattern}, {keep}, {(reveal ? "true" : "false")}) : null");
    }

    private async void CallPreview(string script)
    {
        if (!_previewReady())
        {
            _find.Show(0, -1, false, null);
            return;
        }

        var version = ++_version;
        string answer;
        try
        {
            answer = await _preview.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            // The page is being replaced; the search runs again once the new one is up.
            return;
        }
        if (version != _version || !_find.IsOpen || _find.Scope != FindScope.Preview) return;

        int count = 0, index = -1;
        var more = false;
        string? error = null;
        try
        {
            using var json = JsonDocument.Parse(answer);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                count = root.GetProperty("count").GetInt32();
                index = root.GetProperty("index").GetInt32();
                more = root.TryGetProperty("more", out var m) && m.ValueKind == JsonValueKind.True;
                if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) error = e.GetString();
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
        }
        _previewActive = index;
        _find.Show(count, index, more, error);
    }

    private void ClearPreview()
    {
        _previewActive = -1;
        if (!_previewReady()) return;
        ++_version;
        try { _ = _preview.CoreWebView2.ExecuteScriptAsync("window.mikudoFindClear && window.mikudoFindClear()"); }
        catch { }
    }

    // ── Closing ─────────────────────────────────────────────────

    /// <summary>
    /// The marks go. Closed from the bar, the caret goes back to the page:
    /// into the editor with the current match picked out, so it can be typed
    /// over straight away, or into the preview.
    /// </summary>
    private void OnClosing()
    {
        var returnFocus = !_keepFocus && _bar.HasFocus;
        var match = _find.Scope == FindScope.Markdown && _active >= 0 && _active < _matches.Count ? _matches[_active] : ((int, int)?)null;

        _marks.Clear();
        _matches = new();
        _active = -1;
        ClearPreview();

        if (!returnFocus) return;
        if (match is { } m && _editorShown() && m.Item1 + m.Item2 <= _editor.Text.Length)
        {
            _editor.Focus();
            Keyboard.Focus(_editor);
            _editor.Select(m.Item1, m.Item2);
        }
        else if (_previewShown() && _previewReady())
        {
            _preview.Focus();
        }
        else if (_editorShown())
        {
            _editor.Focus();
        }
    }
}
