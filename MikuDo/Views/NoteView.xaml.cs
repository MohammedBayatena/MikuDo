using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MikuDo.Controls;
using MikuDo.Services;
using MikuDo.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace MikuDo.Views;

public partial class NoteView : UserControl, IFindHost
{
    private const string NoteHost = NoteViewModel.NoteHost;

    private bool _webViewReady;
    private string? _mappedFolder;
    private int _scrollY;
    private NoteViewModel? _wired;
    private readonly FindController _finder;

    public NoteView()
    {
        InitializeComponent();
        _finder = new FindController(new FindViewModel { Placeholder = "Find in note" }, FindBarView, MarkdownBox, EditorMarks,
                                     PreviewWebView, () => _webViewReady,
                                     () => Vm?.ShowEditor == true, () => Vm?.ShowPreview == true);
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            SelectionChip.IsOpen = false;
            _finder.Close();
        };
        DataContextChanged += OnDataContextChanged;
        PreviewKeyDown += OnPagePreviewKeyDown;
    }

    // ── Find ────────────────────────────────────────────────────

    public string FindNoun => "note";

    public bool OpenFind(string? text = null)
    {
        if (Vm == null) return false;
        _finder.Open(text);
        return true;
    }

    public bool FindStep(int step)
    {
        if (Vm == null) return false;
        _finder.Step(step);
        return true;
    }

    /// <summary>Esc in the editor or the preview closes the find bar first.</summary>
    private void OnPagePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None || !_finder.IsOpen || FindBarView.HasFocus) return;
        if (!MarkdownBox.IsKeyboardFocusWithin && !PreviewWebView.IsKeyboardFocusWithin) return;
        SelectionChip.IsOpen = false;
        _finder.Close();
        e.Handled = true;
    }

    private NoteViewModel? Vm => DataContext as NoteViewModel;

    // ── The preview ─────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_webViewReady)
        {
            RenderPreview();
            return;
        }

        try
        {
            await PreviewWebView.EnsureCoreWebView2Async();
            var core = PreviewWebView.CoreWebView2;
            WebAssets.Map(core);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                OpenLink(args.Uri);
            };
            core.WebMessageReceived += OnWebMessage;
            PreviewWebView.NavigationCompleted += OnNavigationCompleted;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(FindScript.Source);

            _webViewReady = true;
            PreviewWebView.Visibility = Visibility.Visible;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            RenderPreview();
        }
        catch
        {
            if (PreviewPlaceholder.Child is TextBlock tb)
                tb.Text = "WebView2 is not available, so the preview cannot render.";
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_wired != null)
        {
            _wired.PropertyChanged -= OnVmPropertyChanged;
            _wired.Main.PropertyChanged -= OnMainPropertyChanged;
            _wired.TasksChanged -= RenderPreview;
        }
        _wired = Vm;
        if (_wired != null)
        {
            _wired.PropertyChanged += OnVmPropertyChanged;
            _wired.Main.PropertyChanged += OnMainPropertyChanged;
            _wired.TasksChanged += RenderPreview;
        }

        _scrollY = 0;
        SelectionChip.IsOpen = false;
        Dispatcher.BeginInvoke(_finder.PageChanged, System.Windows.Threading.DispatcherPriority.Loaded);
        RenderPreview();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteViewModel.RenderedHtml)) RenderPreview();
        else if (e.PropertyName == nameof(NoteViewModel.EditorMode))
        {
            SelectionChip.IsOpen = false;
            // Once the page is laid out the new way, so the editor knows which lines it shows.
            Dispatcher.BeginInvoke(_finder.LayoutChanged, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else if (e.PropertyName == nameof(NoteViewModel.ReaderDialog)) SyncStill();
    }

    private void RenderPreview()
    {
        if (!_webViewReady || Vm is not { } vm) return;

        if (_mappedFolder != vm.Folder)
        {
            try
            {
                if (_mappedFolder != null) PreviewWebView.CoreWebView2.ClearVirtualHostNameToFolderMapping(NoteHost);
                PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(NoteHost, vm.Folder, CoreWebView2HostResourceAccessKind.Allow);
                _mappedFolder = vm.Folder;
            }
            catch (Exception ex)
            {
                LogService.Error($"Could not serve {vm.Folder} to the preview", ex);
            }
        }

        try { PreviewWebView.NavigateToString(vm.RenderedHtml); } catch { }
    }

    /// <summary>
    /// Only the page's own text loads in the preview. A link to another note
    /// opens it here; any other link opens in the browser.
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var uri = args.Uri ?? string.Empty;
        if (uri.StartsWith("data:") || uri == "about:blank") return;
        args.Cancel = true;
        OpenLink(uri);
    }

    private void OpenLink(string uri)
    {
        if (Vm is not { } vm || !Uri.TryCreate(uri, UriKind.Absolute, out var link)) return;

        if (link.Host.Equals(NoteHost, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Uri.UnescapeDataString(link.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(vm.Folder, relative));
            if (NoteFiles.IsNote(target)) vm.FollowNoteLink(target);
            else if (File.Exists(target)) Launch(target);
            return;
        }

        if (link.IsFile && NoteFiles.IsNote(link.LocalPath)) vm.FollowNoteLink(link.LocalPath);
        else if (link.Scheme is "http" or "https" or "mailto") Launch(uri);
    }

    private static void Launch(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { LogService.Error($"Could not open {target}", ex); }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || Vm is not { } vm) return;
        try
        {
            await PreviewWebView.CoreWebView2.ExecuteScriptAsync(PageScript(vm.TasksMade(), _scrollY));
            _finder.PreviewReloaded();
            await PreviewWebView.CoreWebView2.ExecuteScriptAsync(WebAssets.MermaidScript());
        }
        catch { }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (Vm is not { } vm || e.TryGetWebMessageAsString() is not { } message) return;

        if (message.StartsWith("task:")) vm.AddTaskForItem(message[5..]);
        else if (message.StartsWith("scroll:") && int.TryParse(message.AsSpan(7), out var y)) _scrollY = y;
        else if (message == "save") vm.SaveCommand.Execute(null);
        else if (message == "paste" && TaskDetailViewModel.ClipboardHasImages()) { }
    }

    /// <summary>
    /// The page's behaviour: a + Task on each open checklist item, a mark on
    /// those already made into tasks, in-page links that scroll, the scroll
    /// position kept across renders, and Ctrl+S passed back to the app.
    /// </summary>
    private static string PageScript(HashSet<string> made, int scrollY) => """
        (function () {
          var made =
        """ + JsonSerializer.Serialize(made) + """
        ;
          function key(t) { return String(t || '').replace(/\s+/g, ' ').trim().toLowerCase(); }
          document.querySelectorAll('li.task-list-item').forEach(function (li) {
            var own = document.createElement('span');
            own.className = 'mikudo-own';
            var nested = null;
            Array.prototype.slice.call(li.childNodes).forEach(function (n) {
              if (n.nodeType === 1 && (n.tagName === 'UL' || n.tagName === 'OL')) { nested = nested || n; return; }
              if (n.nodeType === 1 && n.tagName === 'INPUT') return;
              if (nested) return;
              own.appendChild(n);
            });
            li.insertBefore(own, nested);
            var box = li.querySelector(':scope > input[type=checkbox]');
            if (box && box.checked) li.classList.add('done');
            if (li.parentElement.closest('li.task-list-item')) return;
            var text = own.textContent.trim();
            var tag = document.createElement('button');
            tag.type = 'button';
            if (made.indexOf(key(text)) >= 0) {
              tag.className = 'mikudo-made';
              tag.textContent = '✓ Task made';
            } else if (!li.classList.contains('done')) {
              tag.className = 'mikudo-task';
              tag.title = 'Make this a task';
              tag.innerHTML = '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg>Task';
              tag.addEventListener('click', function (e) {
                e.preventDefault(); e.stopPropagation();
                window.chrome.webview.postMessage('task:' + text);
              });
            } else return;
            li.insertBefore(tag, nested);
          });
          document.addEventListener('click', function (e) {
            var a = e.target.closest && e.target.closest('a');
            if (!a) return;
            var href = a.getAttribute('href') || '';
            if (href.charAt(0) !== '#') return;
            e.preventDefault();
            var target = document.getElementById(decodeURIComponent(href.slice(1)));
            if (target) target.scrollIntoView({ behavior: 'smooth', block: 'start' });
          });
          window.mikudoHeading = function (i) {
            var found = document.querySelectorAll('h1, h2, h3')[i];
            if (found) found.scrollIntoView({ behavior: 'smooth', block: 'start' });
          };
          window.scrollTo(0,
        """ + scrollY + """
        );
          var pending = false;
          window.addEventListener('scroll', function () {
            if (pending) return;
            pending = true;
            setTimeout(function () { pending = false; window.chrome.webview.postMessage('scroll:' + Math.round(window.scrollY)); }, 150);
          });
          document.addEventListener('keydown', function (e) {
            if (e.ctrlKey && !e.altKey && (e.key === 's' || e.key === 'S')) { e.preventDefault(); window.chrome.webview.postMessage('save'); }
          });
        })();
        """;

    // ── A dialog over the page ──────────────────────────────────

    private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.MakeTasks)) SyncStill();
    }

    /// <summary>
    /// The preview is a browser window of its own, drawn over anything WPF puts
    /// above it. While a dialog is open over the page, a still of it stands in.
    /// A reader window holds its own dialog; the Notes page's is the main window's.
    /// </summary>
    private async void SyncStill()
    {
        if (Vm is not { } vm) return;
        var dialogOpen = vm.IsReader ? vm.ReaderDialog != null : vm.Main.MakeTasks != null;

        if (dialogOpen && _webViewReady && PreviewWebView.Visibility == Visibility.Visible)
        {
            try
            {
                using var stream = new MemoryStream();
                await PreviewWebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                stream.Position = 0;
                var still = new BitmapImage();
                still.BeginInit();
                still.CacheOption = BitmapCacheOption.OnLoad;
                still.StreamSource = stream;
                still.EndInit();
                still.Freeze();
                PreviewStill.Source = still;
                PreviewStill.Width = PreviewWebView.ActualWidth;
                PreviewStill.Height = PreviewWebView.ActualHeight;
            }
            catch
            {
                PreviewStill.Source = null;
            }
            PreviewStill.Visibility = Visibility.Visible;
            PreviewWebView.Visibility = Visibility.Hidden;
        }
        else if (!dialogOpen && _webViewReady)
        {
            PreviewWebView.Visibility = Visibility.Visible;
            PreviewStill.Visibility = Visibility.Collapsed;
            PreviewStill.Source = null;
        }
    }

    // ── Toolbar ─────────────────────────────────────────────────

    private void MakeTasks_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        Menus.OpenBelow(MakeTasksButton,
            Menus.Item(vm.HasOpenItems ? $"From the checklist ({vm.OpenItems} open)…" : "From the checklist…",
                       vm.MakeTasksCommand, "items"),
            Menus.Item("The whole note as one task…", vm.MakeTasksCommand, "whole"));
    }

    private void Outline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: OutlineEntry entry } button || Vm is not { } vm) return;

        foreach (var other in FindRows(this)) other.Tag = "off";
        button.Tag = "on";

        if (_webViewReady) _ = PreviewWebView.CoreWebView2.ExecuteScriptAsync($"window.mikudoHeading && window.mikudoHeading({entry.Index})");
        if (vm.ShowEditor)
        {
            var at = MarkdownBox.GetCharacterIndexFromLineIndex(Math.Min(entry.Line, Math.Max(0, MarkdownBox.LineCount - 1)));
            if (at >= 0)
            {
                MarkdownBox.Select(at, 0);
                MarkdownBox.ScrollToLine(Math.Min(entry.Line, Math.Max(0, MarkdownBox.LineCount - 1)));
            }
        }
    }

    private static IEnumerable<Button> FindRows(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Button { DataContext: OutlineEntry } row) yield return row;
            foreach (var deeper in FindRows(child)) yield return deeper;
        }
    }

    // ── The editor ──────────────────────────────────────────────

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not ButtonBase { Tag: string action } button) return;
        e.Handled = true;

        if (action == "diagram")
        {
            OpenDiagramMenu(button);
            return;
        }

        MarkdownEditing.Apply(MarkdownBox, action);
        MarkdownBox.Focus();
    }

    private void OpenDiagramMenu(FrameworkElement anchor)
    {
        MenuItem Diagram(string header, string action) => Menus.Item(header, () =>
        {
            MarkdownEditing.Apply(MarkdownBox, action);
            MarkdownBox.Focus();
        });
        Menus.OpenBelow(anchor,
            Diagram("Flowchart", "diagram-flowchart"),
            Diagram("Sequence", "diagram-sequence"),
            Diagram("State", "diagram-state"),
            Diagram("Gantt", "diagram-gantt"),
            Diagram("Mind map", "diagram-mindmap"));
    }

    private void MarkdownBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.Control)
        {
            var action = e.Key switch { Key.B => "bold", Key.I => "italic", Key.E => "code", _ => null };
            if (action == null) return;
            MarkdownEditing.Apply(MarkdownBox, action);
            e.Handled = true;
            return;
        }

        if (mods == ModifierKeys.None && e.Key == Key.Enter && MarkdownEditing.ContinueList(MarkdownBox))
            e.Handled = true;
        else if (e.Key == Key.Escape) SelectionChip.IsOpen = false;
    }

    private void MarkdownBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (MarkdownBox.ContextMenu is not { } menu || Vm is not { } vm) return;

        MenuItem Format(string header, string action, string? gesture = null) => Menus.Item(header, () =>
        {
            MarkdownEditing.Apply(MarkdownBox, action);
            MarkdownBox.Focus();
        }, gesture);
        MenuItem Edit(string header, ICommand command)
        {
            var item = Menus.Item(header, command);
            item.CommandTarget = MarkdownBox;
            return item;
        }

        var lines = SelectedLines();
        Menus.Refill(menu,
            lines > 0 ? Menus.Item(lines == 1 ? "Make a task of this line…" : $"Make {lines} tasks of these lines…",
                                   () => SelectionToTasks(asOne: false)) : null,
            Menus.Gap,
            Edit("Cut", ApplicationCommands.Cut),
            Edit("Copy", ApplicationCommands.Copy),
            Edit("Paste", ApplicationCommands.Paste),
            Edit("Select all", ApplicationCommands.SelectAll),
            Menus.Gap,
            Format("Bold", "bold", "Ctrl+B"),
            Format("Italic", "italic", "Ctrl+I"),
            Format("Strikethrough", "strike"),
            Format("Code", "code", "Ctrl+E"),
            Format("Link", "link"),
            Menus.Gap,
            Format("Heading", "heading"),
            Format("Quote", "quote"),
            Menus.Sub("List", new object?[]
            {
                Format("Bulleted list", "bullet"),
                Format("Numbered list", "number"),
                Format("Checkbox list", "task")
            }),
            Menus.Sub("Insert", new object?[]
            {
                Format("Code block", "codeblock"),
                Format("Table", "table"),
                Format("Horizontal rule", "rule"),
                Menus.Gap,
                Format("Flowchart", "diagram-flowchart"),
                Format("Sequence diagram", "diagram-sequence"),
                Format("State diagram", "diagram-state"),
                Format("Gantt chart", "diagram-gantt"),
                Format("Mind map", "diagram-mindmap")
            }));
    }

    /// <summary>The non-blank lines the selection touches; none for a selection inside one line.</summary>
    private int SelectedLines()
    {
        if (MarkdownBox.SelectionLength == 0) return 0;
        var text = MarkdownBox.SelectedText;
        if (!text.Contains('\n') && MarkdownBox.SelectionLength < 2) return 0;
        return NoteFiles.Passage(text, 0).Count;
    }

    /// <summary>The line the caret is on, and the chip offering the selected lines as tasks.</summary>
    private void MarkdownBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.CaretLine = Math.Max(0, MarkdownBox.GetLineIndexFromCharacterIndex(MarkdownBox.CaretIndex)) + 1;

        var lines = MarkdownBox.IsKeyboardFocusWithin ? SelectedLines() : 0;
        if (lines == 0 || !MarkdownBox.SelectedText.Contains('\n') && MarkdownBox.SelectionLength < 3)
        {
            SelectionChip.IsOpen = false;
            return;
        }

        SelectionTasksLabel.Text = lines == 1 ? "Make a task" : $"Make {lines} tasks";
        var end = MarkdownBox.SelectionStart + MarkdownBox.SelectionLength;
        var rect = MarkdownBox.GetRectFromCharacterIndex(Math.Max(0, end - 1), true);
        if (rect.IsEmpty) return;
        SelectionChip.HorizontalOffset = Math.Min(Math.Max(0, rect.Left - 40), Math.Max(0, MarkdownBox.ActualWidth - 260));
        SelectionChip.VerticalOffset = Math.Min(rect.Bottom + 2, MarkdownBox.ActualHeight - 30);
        SelectionChip.IsOpen = true;
    }

    private void SelectionTasks_Click(object sender, RoutedEventArgs e) => SelectionToTasks(asOne: false);

    private void SelectionOneTask_Click(object sender, RoutedEventArgs e) => SelectionToTasks(asOne: true);

    private void SelectionToTasks(bool asOne)
    {
        if (Vm is not { } vm || MarkdownBox.SelectionLength == 0) return;
        SelectionChip.IsOpen = false;
        var first = MarkdownBox.GetLineIndexFromCharacterIndex(MarkdownBox.SelectionStart);
        vm.MakeTasksFromSelection(MarkdownBox.SelectedText, first, asOne);
    }
}
