using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Services;

namespace MikuDo.ViewModels;

public record Shortcut(string Keys, string Action);
public record FeatureNote(string Name, string Description);

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty] private string _updateStatus = string.Empty;

    public string AppName => "MikuDo";

    public string VersionLine
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

            // Assembly.Location is empty in a single-file build, so date the
            // executable itself.
            var build = "—";
            try
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    build = File.GetLastWriteTime(path).ToString("yyyyMMdd");
            }
            catch { }

            return $"Version {version} · Build {build}";
        }
    }

    public Shortcut[] Shortcuts { get; } =
    {
        new("Ctrl + Numpad 1", "Screenshot — captures the screen and attaches it to the open or a new task"),
        new("Ctrl + N", "New task in the current workspace"),
        new("Ctrl + S", "Save the open task"),
        new("Ctrl + K", "Jump to search"),
        new("Escape", "Close the top dialog, or leave the task detail"),
        new("Ctrl + T", "Toggle dark / light theme"),
        new("Ctrl + Shift + V", "Go to Vault"),
        new("Ctrl + Shift + T", "Go to Trash")
    };

    public FeatureNote[] Features { get; } =
    {
        new("Kanban, List and Table", "Three views over the same board, with drag-and-drop between columns"),
        new("AI Import", "Turns pasted notes into tasks with a small language model running on your CPU"),
        new("Markdown Editor", "Write descriptions in markdown with a live preview"),
        new("Screenshots", "Capture and attach screenshots with Ctrl+Numpad1"),
        new("Voice Memos", "Record audio notes straight into a task"),
        new("Attachments", "Keep images and files with the task they belong to"),
        new("Encrypted Vault", "AES-256 encrypted section for sensitive tasks"),
        new("Workspaces", "Organise tasks into separate boards"),
        new("Trash", "Deleted tasks stay restorable for 30 days"),
        new("Draft Auto-save", "Unsaved new tasks are kept as drafts when you leave")
    };

    [RelayCommand]
    private void CheckForUpdates()
        => UpdateStatus = $"{VersionLine} — this is a local build, no update channel is configured.";
}
