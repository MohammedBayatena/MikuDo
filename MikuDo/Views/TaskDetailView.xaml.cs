using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MikuDo.Controls;
using MikuDo.Models;
using MikuDo.Services;
using MikuDo.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace MikuDo.Views;

public partial class TaskDetailView : UserControl, IFindHost
{
    private bool _webViewReady;
    private readonly FindController _finder;

    public TaskDetailView()
    {
        InitializeComponent();
        _finder = new FindController(new FindViewModel { Placeholder = "Find in task" }, FindBarView, MarkdownBox, EditorMarks,
                                     PreviewWebView, () => _webViewReady,
                                     () => Vm?.ShowEditor == true, () => Vm?.ShowPreview == true);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        CommandManager.AddPreviewCanExecuteHandler(this, OnPreviewCanExecute);
        CommandManager.AddPreviewExecutedHandler(this, OnPreviewCommand);
        PreviewKeyDown += OnPagePreviewKeyDown;
    }

    // ── Pasting images ──────────────────────────────────────────

    /// <summary>
    /// Ctrl+V anywhere on the page attaches the clipboard's image. In a text
    /// box, a clipboard that also holds text pastes the text, unless that text
    /// is only the image's address.
    /// </summary>
    /// <remarks>
    /// This cannot wait for a text box's own Paste: a text box treats Paste as
    /// unavailable whenever the clipboard has no text, so for an image alone
    /// its Paste never runs.
    /// </remarks>
    private void OnPagePreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc in the description closes the find bar before it would leave the page.
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && _finder.IsOpen && !FindBarView.HasFocus
            && (MarkdownBox.IsKeyboardFocusWithin || PreviewWebView.IsKeyboardFocusWithin))
        {
            _finder.Close();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control || Vm == null) return;
        var image = Keyboard.FocusedElement is TextBoxBase ? PastesAsImage() : TaskDetailViewModel.ClipboardHasImages();
        if (image) e.Handled = Vm.PasteImages();
    }

    /// <summary>A text box's right-click Paste is offered for an image too, and attaches it.</summary>
    private void OnPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || Vm == null || !PastesAsImage()) return;
        e.CanExecute = true;
        e.Handled = true;
    }

    private void OnPreviewCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || Vm == null || !PastesAsImage()) return;
        e.Handled = Vm.PasteImages();
    }

    /// <summary>
    /// Whether a paste into a text box attaches the clipboard's image rather
    /// than pasting its text. Copying from Word or Excel gives text and a
    /// picture of it, and the text is what was meant. A browser's Copy image
    /// can add the image's address as text, and the image is what was meant.
    /// </summary>
    private static bool PastesAsImage()
    {
        if (!TaskDetailViewModel.ClipboardHasImages()) return false;
        var text = ClipboardText().Trim();
        return text.Length == 0 || IsAddress(text);
    }

    private static bool IsAddress(string text)
        => !text.Any(char.IsWhiteSpace)
           && (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("data:image", StringComparison.OrdinalIgnoreCase));

    private static string ClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Silences the preview as the page goes. The view is kept for the next
    /// task, and the browser inside it goes on playing a voice memo started in
    /// the preview until the page in it is replaced, so it is replaced now.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SubtaskChip.IsOpen = false;
        _finder.Close();
        if (!_webViewReady) return;

        try
        {
            _ = PreviewWebView.CoreWebView2?.ExecuteScriptAsync(
                "document.querySelectorAll('audio,video').forEach(function(m){m.pause();});");
            PreviewWebView.NavigateToString("<!doctype html><html><body></body></html>");
        }
        catch
        {
            // The browser can already be gone during shutdown; nothing is playing then.
        }
    }

    private TaskDetailViewModel? Vm => DataContext as TaskDetailViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        MarkdownBox.Focus();

        // The view is reused for every task, so Loaded runs again each time it
        // comes back. The browser is already up and already wired.
        if (_webViewReady)
        {
            RenderPreview();
            return;
        }

        try
        {
            await PreviewWebView.EnsureCoreWebView2Async();
            Services.WebAssets.Map(PreviewWebView.CoreWebView2);
            PreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            PreviewWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            PreviewWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            PreviewWebView.NavigationStarting += (_, args) =>
            {
                if (args.Uri != null && !args.Uri.StartsWith("data:") && args.Uri != "about:blank")
                    args.Cancel = true;
            };
            PreviewWebView.CoreWebView2.WebMessageReceived += OnWebMessage;
            PreviewWebView.NavigationCompleted += OnNavigationCompleted;
            await PreviewWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(FindScript.Source);

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
        if (e.OldValue is TaskDetailViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnVmPropertyChanged;
            oldVm.Comments.CollectionChanged -= Comments_CollectionChanged;
            oldVm.DictationReady -= OnDictationReady;
        }
        if (e.NewValue is TaskDetailViewModel newVm)
        {
            newVm.PropertyChanged += OnVmPropertyChanged;
            newVm.Comments.CollectionChanged += Comments_CollectionChanged;
            newVm.DictationReady += OnDictationReady;
        }

        // A different task means different markdown; the browser is still
        // showing the last one. A search in the last one ends with it.
        SubtaskChip.IsOpen = false;
        _finder.Close();
        RenderPreview();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskDetailViewModel.RenderedHtml)) RenderPreview();
        else if (e.PropertyName == nameof(TaskDetailViewModel.EditorMode))
        {
            SubtaskChip.IsOpen = false;
            if (Vm?.ShowComments == true) _finder.Close();
            // Once the page is laid out the new way, so the editor knows which lines it shows.
            else Dispatcher.BeginInvoke(_finder.LayoutChanged, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    // ── Find ────────────────────────────────────────────────────

    public string FindNoun => "task";

    /// <summary>Opens the find bar over the description; from Comments, the description comes back first.</summary>
    public bool OpenFind(string? text = null)
    {
        if (Vm is not { } vm) return false;
        vm.ShowDescription();
        _finder.Open(text);
        return true;
    }

    public bool FindStep(int step)
    {
        if (Vm is not { ShowComments: false }) return false;
        _finder.Step(step);
        return true;
    }

    private void RenderPreview()
    {
        if (!_webViewReady || Vm == null) return;
        try { PreviewWebView.NavigateToString(Vm.RenderedHtml); } catch { }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || Vm == null) return;
        try
        {
            await PreviewWebView.CoreWebView2.ExecuteScriptAsync(Vm.GetMediaControlsScript());
            _finder.PreviewReloaded();
            await PreviewWebView.CoreWebView2.ExecuteScriptAsync(Services.WebAssets.MermaidScript());
        }
        catch { }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (Vm == null) return;

        var message = e.TryGetWebMessageAsString();
        if (message == null) return;

        if (message.StartsWith("remove_voice:") && int.TryParse(message.AsSpan(13), out var voice))
            Vm.RemoveVoice(voice);
        else if (message.StartsWith("remove_image:") && int.TryParse(message.AsSpan(13), out var image))
            Vm.RemoveImage(image);
        else if (message.StartsWith("open_image:") && int.TryParse(message.AsSpan(11), out var shown))
            Vm.OpenImageNumber(shown);
        else if (message == "paste" && TaskDetailViewModel.ClipboardHasImages())
            Vm.PasteImages();
    }

    // ── Right-click menus ───────────────────────────────────────

    /// <summary>
    /// The editor's menu: cut, copy and paste, then what the formatting bar
    /// does, each with its shortcut where it has one.
    /// </summary>
    private void MarkdownBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (MarkdownBox.ContextMenu is not { } menu || Vm is not { } vm) return;

        var editable = !MarkdownBox.IsReadOnly;
        MenuItem? Format(string header, string action, string? gesture = null) => editable
            ? Menus.Item(header, () =>
            {
                MarkdownEditing.Apply(MarkdownBox, action);
                MarkdownBox.Focus();
            }, gesture)
            : null;
        MenuItem Edit(string header, ICommand command)
        {
            var item = Menus.Item(header, command);
            item.CommandTarget = MarkdownBox;
            return item;
        }

        // The checklist items selected, or with nothing selected, all of them, as subtasks.
        var selected = editable && vm.CanMakeSubtasks && MarkdownBox.SelectionLength > 0 ? SelectedChecklist().Items.Count : 0;
        var whole = editable && MarkdownBox.SelectionLength == 0 ? vm.ChecklistCount : 0;

        Menus.Refill(menu,
            selected > 0 ? Menus.Item(selected == 1 ? "Make a subtask of this item" : $"Make {selected} subtasks of these items",
                                      () => MakeSubtasks(selectionOnly: true)) : null,
            whole > 0 ? Menus.Item(whole == 1 ? "Make the checklist item a subtask" : $"Make the {whole} checklist items subtasks",
                                   () => MakeSubtasks(selectionOnly: false)) : null,
            Menus.Gap,
            Edit("Cut", ApplicationCommands.Cut),
            Edit("Copy", ApplicationCommands.Copy),
            Edit("Paste", ApplicationCommands.Paste),
            editable && TaskDetailViewModel.ClipboardHasImages() ? Menus.Item("Paste image", () => vm.PasteImages()) : null,
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
            editable ? Menus.Sub("List", new object?[]
            {
                Format("Bulleted list", "bullet"),
                Format("Numbered list", "number"),
                Format("Checkbox list", "task")
            }) : null,
            editable ? Menus.Sub("Insert", new object?[]
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
            }) : null);
    }

    private void Thumbnail_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: GalleryImage image } element) return;
        e.Handled = true;

        if (image.IsAddSlot)
        {
            Menus.Open(element,
                Menus.Item("Attach an image…", vm.AttachImageCommand),
                TaskDetailViewModel.ClipboardHasImages() ? Menus.Item("Paste image", () => vm.PasteImages(), "Ctrl+V") : null);
            return;
        }

        Menus.Open(element,
            Menus.Item("Open full screen", vm.OpenImageCommand, image),
            Menus.Item("Save image…", vm.SaveImageCommand, image),
            Menus.Gap,
            Menus.Danger(Menus.Item("Remove image…", vm.RemoveImageTileCommand, image)));
    }

    private void File_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: AttachedFile file } element) return;
        e.Handled = true;
        Menus.Open(element,
            Menus.Item("Open", vm.OpenFileCommand, file),
            Menus.Gap,
            Menus.Danger(Menus.Item("Remove file", vm.RemoveFileCommand, file)));
    }

    private void Voice_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: VoiceMemoViewModel memo } element) return;
        e.Handled = true;
        Menus.Open(element,
            Menus.Item(memo.IsPlaying ? "Pause" : "Play", vm.TogglePlayCommand, memo),
            Menus.Gap,
            Menus.Danger(Menus.Item("Delete voice memo", vm.DeleteVoiceCommand, memo)));
    }

    private void Subtask_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: SubtaskRow row } element) return;
        e.Handled = true;
        Menus.Open(element,
            Menus.Item("Open subtask", vm.OpenSubtaskCommand, row),
            Menus.Item(row.IsDone ? "Mark as not done" : "Mark done", () => row.IsDone = !row.IsDone),
            Menus.Gap,
            Menus.Item("Unlink from this task", vm.UnlinkSubtaskCommand, row));
    }

    /// <summary>A comment's menu. While it is being edited its box has the usual text menu instead.</summary>
    private void Comment_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: CommentViewModel comment } element) return;
        if (comment.IsEditing) return;
        e.Handled = true;
        Menus.Open(element,
            Menus.Item("Edit", vm.EditCommentCommand, comment),
            Menus.Item("Copy text", vm.CopyCommentCommand, comment),
            Menus.Gap,
            Menus.Danger(Menus.Item("Delete comment", vm.DeleteCommentCommand, comment)));
    }

    // ── Formatting ──────────────────────────────────────────────

    /// <summary>One handler for the whole bar; each button names its action in its Tag.</summary>
    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not ButtonBase { Tag: string action } button) return;
        e.Handled = true;

        if (action == "diagram")
        {
            MenuItem Diagram(string header, string kind) => Menus.Item(header, () =>
            {
                MarkdownEditing.Apply(MarkdownBox, kind);
                MarkdownBox.Focus();
            });
            Menus.OpenBelow(button,
                Diagram("Flowchart", "diagram-flowchart"),
                Diagram("Sequence", "diagram-sequence"),
                Diagram("State", "diagram-state"),
                Diagram("Gantt", "diagram-gantt"),
                Diagram("Mind map", "diagram-mindmap"));
            return;
        }

        MarkdownEditing.Apply(MarkdownBox, action);
        MarkdownBox.Focus();
    }

    /// <summary>
    /// The editor's own shortcuts, and Enter carrying a list on. Handling them
    /// here also keeps Ctrl+B from reaching the window, where it would fold
    /// the sidebar while the user meant bold.
    /// </summary>
    private void MarkdownBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;

        if (mods == ModifierKeys.Control)
        {
            var action = e.Key switch
            {
                Key.B => "bold",
                Key.I => "italic",
                Key.E => "code",
                _ => null
            };
            if (action == null) return;

            MarkdownEditing.Apply(MarkdownBox, action);
            e.Handled = true;
            return;
        }

        if (mods == ModifierKeys.None && e.Key == Key.Enter && MarkdownEditing.ContinueList(MarkdownBox))
            e.Handled = true;
    }

    // ── Checklist to subtasks ───────────────────────────────────

    /// <summary>The selected items when the selection takes in any, otherwise every item in the description.</summary>
    private void MakeSubtasks_Click(object sender, RoutedEventArgs e)
        => MakeSubtasks(selectionOnly: MarkdownBox.SelectionLength > 0 && SelectedChecklist().Items.Count > 0);

    private void SelectionSubtasks_Click(object sender, RoutedEventArgs e) => MakeSubtasks(selectionOnly: true);

    /// <summary>
    /// Makes subtasks of checklist items and takes their lines, and the lines
    /// indented under them, out of the description. The text changes as one
    /// edit, so Ctrl+Z puts the lines back; Undo on the board takes the tasks.
    /// </summary>
    private void MakeSubtasks(bool selectionOnly)
    {
        if (Vm is not { CanMakeSubtasks: true } vm || MarkdownBox.IsReadOnly) return;
        SubtaskChip.IsOpen = false;

        var (items, firstLine) = selectionOnly ? SelectedChecklist() : (NoteFiles.Checklist(MarkdownBox.Text), 0);
        if (items.Count == 0 || vm.MakeSubtasks(items) == 0) return;

        var taken = items.SelectMany(i => i.Children.Select(c => c.Line).Prepend(i.Line))
                         .Select(line => line + firstLine)
                         .ToHashSet();
        ReplaceText(MarkdownBox, WithoutLines(MarkdownBox.Text, taken));
    }

    /// <summary>
    /// The checklist items on the lines the selection touches, and the line
    /// their numbers count from. An item takes the lines indented under it
    /// along. When the selection holds only items indented under one it leaves
    /// out, those are taken as items of their own.
    /// </summary>
    private (List<NoteItem> Items, int FirstLine) SelectedChecklist()
    {
        var text = MarkdownBox.Text;
        if (MarkdownBox.SelectionLength == 0 || text.Length == 0) return (new List<NoteItem>(), 0);

        var start = Math.Min(MarkdownBox.SelectionStart, text.Length);
        var end = Math.Min(start + MarkdownBox.SelectionLength, text.Length);
        // A selection ending at the very start of a line leaves that line out.
        if (end > start && text[end - 1] == '\n') end--;
        var from = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var lineEnd = text.IndexOf('\n', end);
        var to = lineEnd < 0 ? text.Length : lineEnd + 1;

        var first = text.AsSpan(0, from).Count('\n');
        var last = text.AsSpan(0, Math.Max(from, to - 1)).Count('\n');
        var items = NoteFiles.Checklist(text).Where(i => i.Line >= first && i.Line <= last).ToList();
        return items.Count > 0 ? (items, 0) : (NoteFiles.Checklist(text[from..to]), first);
    }

    /// <summary>
    /// The text without the lines numbered in <paramref name="taken"/>, each
    /// with its line break. Where lines came out between two blank ones, one
    /// blank line is left, not two.
    /// </summary>
    private static string WithoutLines(string text, HashSet<int> taken)
    {
        var lines = System.Text.RegularExpressions.Regex.Split(text, "(?<=\\n)");
        var kept = new List<string>();
        var gap = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (taken.Contains(i))
            {
                gap = true;
                continue;
            }
            var blank = lines[i].Trim().Length == 0;
            if (blank && gap && kept.Count > 0 && kept[^1].Trim().Length == 0) continue;
            kept.Add(lines[i]);
            if (!blank) gap = false;
        }
        return string.Concat(kept);
    }

    /// <summary>Gives the editor <paramref name="next"/> by retyping only the part that differs, so the view stays where it is.</summary>
    private static void ReplaceText(TextBox box, string next)
    {
        var old = box.Text;
        var head = 0;
        while (head < old.Length && head < next.Length && old[head] == next[head]) head++;
        var tail = 0;
        while (tail < old.Length - head && tail < next.Length - head && old[old.Length - 1 - tail] == next[next.Length - 1 - tail]) tail++;

        var inserted = next.Substring(head, next.Length - head - tail);
        box.Select(head, old.Length - head - tail);
        box.SelectedText = inserted;
        box.Select(head + inserted.Length, 0);
    }

    /// <summary>Checklist lines selected in the editor bring up a chip that makes them subtasks.</summary>
    private void MarkdownBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is not { CanMakeSubtasks: true } || !MarkdownBox.IsKeyboardFocusWithin || MarkdownBox.IsReadOnly
            || MarkdownBox.SelectionLength < 3 && !MarkdownBox.SelectedText.Contains('\n'))
        {
            SubtaskChip.IsOpen = false;
            return;
        }

        var count = SelectedChecklist().Items.Count;
        if (count == 0)
        {
            SubtaskChip.IsOpen = false;
            return;
        }

        SubtaskChipLabel.Text = count == 1 ? "Make a subtask" : $"Make {count} subtasks";
        var end = MarkdownBox.SelectionStart + MarkdownBox.SelectionLength;
        var rect = MarkdownBox.GetRectFromCharacterIndex(Math.Max(0, end - 1), true);
        if (rect.IsEmpty) return;
        SubtaskChip.HorizontalOffset = Math.Min(Math.Max(0, rect.Left - 40), Math.Max(0, MarkdownBox.ActualWidth - 200));
        SubtaskChip.VerticalOffset = Math.Min(rect.Bottom + 2, MarkdownBox.ActualHeight - 30);
        SubtaskChip.IsOpen = true;
    }

    /// <summary>The chip goes when the editor does: another click, another window.</summary>
    private void MarkdownBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => SubtaskChip.IsOpen = false;

    // ── Sidebar width ───────────────────────────────────────────

    /// <summary>The editor column's minimum plus the margins on either side of it.</summary>
    private const double EditorRoom = 380 + 36;

    /// <summary>The handle moves with the edge, so each step reports only the change since the last.</summary>
    private void SidebarEdge_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (Vm == null) return;

        var widest = Math.Max(TaskDetailViewModel.MinSidebarWidth, Body.ActualWidth - EditorRoom);
        Vm.SidebarWidth = Math.Clamp(Vm.SidebarWidth - e.HorizontalChange,
                                     TaskDetailViewModel.MinSidebarWidth, widest);
    }

    private void SidebarEdge_DragCompleted(object sender, DragCompletedEventArgs e) => Vm?.SaveSidebarWidth();

    private void SidebarEdge_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;
        Vm.SidebarWidth = TaskDetailViewModel.DefaultSidebarWidth;
        Vm.SaveSidebarWidth();
        e.Handled = true;
    }

    // ── Subtask picker ──────────────────────────────────────────

    /// <summary>Opening the picker puts the caret in its search box.</summary>
    private void SubtaskSearch_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;
        Dispatcher.BeginInvoke(() => Keyboard.Focus(box), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Escape closes the picker rather than leaving the page.</summary>
    private void SubtaskSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Vm == null) return;
        Vm.IsSubtaskPickerOpen = false;
        e.Handled = true;
    }

    // ── Rewrite with AI ─────────────────────────────────────────

    /// <summary>
    /// Rewrites the selection, or the whole description when nothing is
    /// selected, and types the result in through the selection so Ctrl+Z
    /// takes it back. The editor is read-only meanwhile, so the text it
    /// replaces is still the text it read.
    /// </summary>
    private async void Rewrite_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        if (!Vm.CanRewrite)
        {
            Vm.IsRewriteHelpOpen = true;
            return;
        }

        var box = MarkdownBox;
        var whole = box.SelectionLength == 0;
        var start = whole ? 0 : box.SelectionStart;
        var length = whole ? box.Text.Length : box.SelectionLength;

        var rewritten = await Vm.RewriteAsync(box.Text.Substring(start, length));
        if (rewritten == null) return;

        box.Select(start, length);
        box.SelectedText = rewritten;
        box.Select(start + rewritten.Length, 0);
        box.Focus();
    }

    // ── Dictation ───────────────────────────────────────────────

    /// <summary>
    /// Puts dictated words at the caret when the editor has it, otherwise at
    /// the end, as a paragraph of their own. Typed in through the selection,
    /// so Ctrl+Z takes them out again.
    /// </summary>
    private void OnDictationReady(string text)
    {
        var box = MarkdownBox;
        if (!box.IsKeyboardFocused) box.Select(box.Text.Length, 0);

        var before = box.Text[..box.SelectionStart];
        var lead = before.Length == 0 || before.EndsWith("\n\n") || before.EndsWith("\r\n\r\n") ? ""
                 : before.EndsWith('\n') ? "\n"
                 : "\n\n";
        box.SelectedText = lead + text + "\n";
        box.Select(box.SelectionStart + box.SelectionLength, 0);
        box.Focus();
    }

    // ── Comments ────────────────────────────────────────────────

    /// <summary>Opening the tab puts the caret in the composer and the latest comment in view.</summary>
    private void CommentBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        Dispatcher.BeginInvoke(() =>
        {
            CommentScroll.ScrollToEnd();
            Keyboard.Focus(CommentBox);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Enter starts a new line; Ctrl+Enter posts.</summary>
    private void CommentBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control || Vm == null) return;
        Vm.PostCommentCommand.Execute(null);
        e.Handled = true;
    }

    private void CommentEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;
        Dispatcher.BeginInvoke(() =>
        {
            Keyboard.Focus(box);
            box.CaretIndex = box.Text.Length;
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Ctrl+Enter keeps the edit; Escape drops it rather than leaving the page.</summary>
    private void CommentEdit_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CommentViewModel comment } || Vm == null) return;

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Vm.SaveCommentEditCommand.Execute(comment);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Vm.CancelCommentEditCommand.Execute(comment);
            e.Handled = true;
        }
    }

    /// <summary>A comment just posted scrolls into view.</summary>
    private void Comments_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add) return;
        Dispatcher.BeginInvoke(() => CommentScroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ── Popup pickers ───────────────────────────────────────────

    private void StatusPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string status })
            Vm?.SetStatusCommand.Execute(status);
    }

    private void TagSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string label })
            Vm?.AddTagCommand.Execute(label);
    }

    private void PriorityPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoPriority priority })
            Vm?.SetPriorityCommand.Execute(priority);
    }

    // ── Inline inputs ───────────────────────────────────────────

    private void TagInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Vm?.AddTagCommand.Execute(null);
        e.Handled = true;
    }

    private void SubtaskCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskOption option })
            Vm?.LinkSubtaskCommand.Execute(option);
    }
}
