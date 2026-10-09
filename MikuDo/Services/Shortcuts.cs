using System.Text.Json;
using System.Windows.Input;
using MikuDo.Models;

namespace MikuDo.Services;

/// <summary>Where a command does its work: the groups of Settings → Keyboard shortcuts.</summary>
public enum ShortcutArea { General, Board, TaskPage, Notes, Windows }

/// <summary>
/// A command anyone can run from Quick Access or a key. <see cref="Category"/>
/// is the word Quick Access shows beside it; <see cref="Area"/> is the group
/// Settings lists it under.
/// </summary>
public sealed record CommandDef(
    string Id, string Name, string Category, ShortcutArea Area, string Description, string Icon, KeyChord? Default);

/// <summary>
/// Every command, the key each starts on, and the keys the user has changed.
/// Only the changes are stored, as one setting: the id of each changed
/// command with its keys, or an empty string for a command left with none.
/// </summary>
public static class Shortcuts
{
    public const string SettingKey = "Shortcuts";

    private static KeyChord K(Key key, ModifierKeys modifiers = ModifierKeys.None) => new(key, modifiers);
    private const ModifierKeys Ctrl = ModifierKeys.Control;
    private const ModifierKeys CtrlShift = ModifierKeys.Control | ModifierKeys.Shift;

    public static IReadOnlyList<CommandDef> All { get; } = new CommandDef[]
    {
        // General
        new("quick.open", "Quick Access", "General", ShortcutArea.General,
            "The search bar: tasks, notes, settings and commands", "IconSearch", K(Key.K, Ctrl)),
        new("quick.tasks", "Go to a task", "Navigation", ShortcutArea.General,
            "Quick Access with # typed: tasks only", "IconHash", K(Key.G, Ctrl)),
        new("quick.commands", "Run a command", "General", ShortcutArea.General,
            "Quick Access with > typed: commands only", "IconPrompt", K(Key.P, CtrlShift)),
        new("quick.settings", "Search settings", "General", ShortcutArea.General,
            "Quick Access with ? typed: settings only", "IconSettings", null),
        new("find.open", "Find in the page", "Find", ShortcutArea.General,
            "In a note or a task, every match marked, with next and previous; on a board, its search", "IconSearch", K(Key.F, Ctrl)),
        new("find.next", "Find next", "Find", ShortcutArea.General,
            "The next match in the note or task on screen", "IconArrowDown", K(Key.F3)),
        new("find.previous", "Find previous", "Find", ShortcutArea.General,
            "The match before, in the note or task on screen", "IconArrowUp", K(Key.F3, ModifierKeys.Shift)),
        new("app.save", "Save", "General", ShortcutArea.General,
            "The task or note on screen", "IconSave", K(Key.S, Ctrl)),
        new("app.undo", "Undo", "General", ShortcutArea.General,
            "Takes back the last move, rename or delete on a board", "IconUndo", null),
        new("app.settings", "Open Settings", "General", ShortcutArea.General,
            "Capture, appearance, AI, dictation and backup", "IconSettings", K(Key.OemComma, Ctrl)),
        new("app.shortcuts", "Keyboard shortcuts…", "General", ShortcutArea.General,
            "This list, where any command can have its keys changed", "IconKeyboard", null),
        new("theme.toggle", "Toggle theme", "Appearance", ShortcutArea.General,
            "Between the light and dark theme used last", "IconMoon", K(Key.T, Ctrl)),
        new("theme.choose", "Choose theme…", "Appearance", ShortcutArea.General,
            "Opens Settings at the themes", "IconMoon", null),
        new("layout.switch", "Switch layout", "Appearance", ShortcutArea.General,
            "Between Islands and Flat", "IconLayout", null),
        new("nav.dashboard", "Go to Dashboard", "Navigation", ShortcutArea.General,
            "Every workspace at a glance", "IconDashboard", null),
        new("nav.vault", "Go to Vault", "Navigation", ShortcutArea.General,
            "Tasks kept locked behind the vault password", "IconLock", K(Key.V, CtrlShift)),
        new("nav.trash", "Go to Trash", "Navigation", ShortcutArea.General,
            "Deleted tasks, to restore or remove for good", "IconTrash", K(Key.T, CtrlShift)),
        new("nav.about", "Go to About", "Navigation", ShortcutArea.General,
            "The version, and what MikuDo is made with", "IconAbout", null),
        new("capture.screenshot", "Take a screenshot into a task", "Capture", ShortcutArea.General,
            "Into the task on screen, or a new one", "IconCamera", K(Key.NumPad1, Ctrl)),
        new("capture.screens", "Switch screenshots between screens", "Capture", ShortcutArea.General,
            "The primary monitor only, or every screen", "IconCamera", null),

        // Board
        new("board.newtask", "New task", "Board", ShortcutArea.Board,
            "In the workspace on screen", "IconPlus", K(Key.N, Ctrl)),
        new("board.rename", "Rename task", "Board", ShortcutArea.Board,
            "The card that is picked out", "IconPencil", K(Key.F2)),
        new("board.kanban", "Show the board as Kanban", "Board", ShortcutArea.Board,
            "Columns of cards", "IconKanban", null),
        new("board.list", "Show the board as a List", "Board", ShortcutArea.Board,
            "One row per task, under each list's heading", "IconList", null),
        new("board.table", "Show the board as a Table", "Board", ShortcutArea.Board,
            "Every task in one table", "IconTable", null),
        new("board.import", "Import tasks with AI…", "Board", ShortcutArea.Board,
            "Turns pasted text into tasks", "IconSparkle", null),
        new("board.export", "Export the board…", "Board", ShortcutArea.Board,
            "The workspace on screen, as Markdown", "IconExport", null),
        new("board.newworkspace", "New workspace", "Board", ShortcutArea.Board,
            "Adds one to the sidebar, ready to be named", "IconWorkspace", null),
        new("board.nextworkspace", "Next workspace", "Board", ShortcutArea.Board,
            "The one below in the sidebar", "IconArrowRight", null),
        new("board.previousworkspace", "Previous workspace", "Board", ShortcutArea.Board,
            "The one above in the sidebar", "IconArrowLeft", null),

        // Task page
        new("task.back", "Back to the board", "Task page", ShortcutArea.TaskPage,
            "Saves the task and leaves its page", "IconArrowLeft", K(Key.Escape)),
        new("task.completesubtasks", "Mark all subtasks done", "Task page", ShortcutArea.TaskPage,
            "Every subtask of the task on screen", "IconCheck", null),
        new("task.attachimage", "Attach an image…", "Task page", ShortcutArea.TaskPage,
            "Picks a picture to add to the task", "IconImage", null),
        new("task.attachfile", "Attach a file…", "Task page", ShortcutArea.TaskPage,
            "Picks a file to keep with the task", "IconPaperclip", null),
        new("task.panel", "Show or hide the task's side panel", "Task page", ShortcutArea.TaskPage,
            "Status, priority, labels and subtasks", "IconSidebarRight", null),

        // Notes
        new("note.new", "New note…", "Notes", ShortcutArea.Notes,
            "Asks where to save it, then opens it", "IconNoteSmall", null),
        new("note.open", "Open file…", "Notes", ShortcutArea.Notes,
            "A Markdown file from anywhere", "IconFolder", K(Key.O, Ctrl)),
        new("note.folder", "Open folder…", "Notes", ShortcutArea.Notes,
            "Puts a folder of notes under Notes", "IconFolder", null),
        new("quick.notes", "Search notes", "Notes", ShortcutArea.Notes,
            "Quick Access with n: typed: notes only", "IconNoteSmall", null),
        new("note.popout", "Open the note in a new window", "Notes", ShortcutArea.Notes,
            "The note on screen moves to a reader window", "IconPopOut", null),
        new("note.edit", "Edit the note", "Notes", ShortcutArea.Notes,
            "The Markdown on its own", "IconPencil", null),
        new("note.split", "Edit and preview side by side", "Notes", ShortcutArea.Notes,
            "The Markdown beside the page it makes", "IconTable", null),
        new("note.preview", "Preview the note", "Notes", ShortcutArea.Notes,
            "The page only, as it reads", "IconNoteSmall", null),
        new("note.maketasks", "Make tasks from the note…", "Notes", ShortcutArea.Notes,
            "Its checklist, or the whole note as one task", "IconSubtasks", null),
        new("note.clearrecent", "Clear recent notes", "Notes", ShortcutArea.Notes,
            "Empties Recent; the files stay where they are", "IconClock", null),

        // Windows
        new("window.sidebar", "Show or hide the sidebar", "Window", ShortcutArea.Windows,
            "The main menu on the left", "IconSidebarLeft", K(Key.B, Ctrl)),
        new("window.closereader", "Close the reader window", "Window", ShortcutArea.Windows,
            "A note open in a window of its own", "IconClose", K(Key.W, Ctrl)),
        new("window.minimize", "Minimise MikuDo", "Window", ShortcutArea.Windows,
            "Down to the taskbar", "IconMinimize", null),
        new("window.maximize", "Maximise or restore MikuDo", "Window", ShortcutArea.Windows,
            "Fills the screen, or goes back to its size", "IconMaximize", null),
    };

    private static readonly Dictionary<string, CommandDef> ById = All.ToDictionary(d => d.Id);

    public static CommandDef? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>Raised whenever any command's keys change, reset included.</summary>
    public static event Action? Changed;

    /// <summary>The keys the user gave, by command id; null means none at all.</summary>
    private static Dictionary<string, KeyChord?>? _changes;

    private static Dictionary<string, KeyChord?> Changes => _changes ??= Load();

    private static Dictionary<string, KeyChord?> Load()
    {
        var changes = new Dictionary<string, KeyChord?>();
        var stored = App.Database.GetSetting(SettingKey);
        if (string.IsNullOrEmpty(stored)) return changes;
        try
        {
            foreach (var (id, keys) in JsonSerializer.Deserialize<Dictionary<string, string>>(stored) ?? new())
            {
                if (!ById.ContainsKey(id)) continue;
                changes[id] = KeyChord.Parse(keys);
            }
        }
        catch (JsonException) { }
        return changes;
    }

    /// <summary>Keys are stored as they read, "Ctrl+Alt+T", not with the plus escaped.</summary>
    private static readonly JsonSerializerOptions Plain = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static void Save()
        => App.Database.SaveSetting(SettingKey, JsonSerializer.Serialize(
            Changes.ToDictionary(c => c.Key, c => c.Value?.ToString() ?? string.Empty), Plain));

    /// <summary>Reads the stored keys again: after a backup is imported, the database is a different one.</summary>
    public static void Reload()
    {
        _changes = null;
        Changed?.Invoke();
    }

    public static KeyChord? ChordFor(string id)
        => Changes.TryGetValue(id, out var changed) ? changed : Find(id)?.Default;

    public static bool IsChanged(string id) => Changes.ContainsKey(id);

    public static int ChangedCount => Changes.Count;

    /// <summary>The command <paramref name="chord"/> runs, if any.</summary>
    public static CommandDef? Holder(KeyChord chord)
        => All.FirstOrDefault(d => ChordFor(d.Id) == chord);

    /// <summary>A command's keys as menus and tips write them, "Ctrl+O"; null for none.</summary>
    public static string? GestureText(string id) => ChordFor(id) is { } chord ? string.Join("+", chord.Caps) : null;

    /// <summary>True when the key press is the chord <paramref name="id"/> runs on.</summary>
    public static bool Is(string id, KeyEventArgs e) => ChordFor(id) is { } chord && chord == KeyChord.From(e);

    /// <summary>
    /// Gives <paramref name="id"/> these keys, or none. Keys back at the
    /// command's own start are stored as no change at all.
    /// </summary>
    public static void Set(string id, KeyChord? chord)
    {
        if (Find(id) is not { } def) return;
        if (def.Default == chord) Changes.Remove(id);
        else Changes[id] = chord;
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Gives <paramref name="id"/> keys another command holds, which is left
    /// with none: one key press never runs two commands.
    /// </summary>
    public static void Move(string id, KeyChord chord)
    {
        if (Holder(chord) is { } other && other.Id != id) LeaveWithout(other);
        Set(id, chord);
    }

    public static void Reset(string id)
    {
        if (Find(id) is not { } def) return;
        // Its own keys may have gone to another command since; that one gives them up.
        if (def.Default is { } keys && Holder(keys) is { } other && other.Id != id) LeaveWithout(other);
        Changes.Remove(id);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Takes a command's keys away; one with none to start with is simply back at its start.</summary>
    private static void LeaveWithout(CommandDef def)
    {
        if (def.Default == null) Changes.Remove(def.Id);
        else Changes[def.Id] = null;
    }

    public static void ResetAll()
    {
        Changes.Clear();
        Save();
        Changed?.Invoke();
    }
}
