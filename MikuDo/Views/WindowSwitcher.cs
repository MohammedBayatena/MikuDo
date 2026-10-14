using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MikuDo.Controls;

namespace MikuDo.Views;

/// <summary>
/// Moving between MikuDo's windows: the main window and every reader window.
/// The windows button in each title bar lists them, Quick Access finds them by
/// name, and Next MikuDo window steps through them.
/// </summary>
public static class WindowSwitcher
{
    private static MainWindow? Main => Application.Current?.MainWindow as MainWindow;

    /// <summary>The windows a step goes through: the main window while it is shown, then each reader in the order it opened.</summary>
    private static List<Window> Cycle()
    {
        var windows = new List<Window>();
        if (Main is { IsVisible: true } main) windows.Add(main);
        windows.AddRange(NoteWindow.All);
        return windows;
    }

    /// <summary>
    /// Brings forward the window <paramref name="step"/> on from <paramref name="current"/>,
    /// round at the ends. False when there is no other window to go to.
    /// </summary>
    public static bool Next(Window current, int step = 1)
    {
        var windows = Cycle();
        if (windows.Count < 2) return false;
        var at = windows.IndexOf(current);
        var next = windows[((at + step) % windows.Count + windows.Count) % windows.Count];
        if (ReferenceEquals(next, current)) return false;
        Show(next);
        return true;
    }

    /// <summary>Brings a window forward; the main window comes back first if it was hidden behind the readers.</summary>
    public static void Show(Window window)
    {
        switch (window)
        {
            case NoteWindow reader:
                reader.BringForward();
                break;
            case MainWindow main:
                if (!main.IsVisible) main.Show();
                App.BringForward(main);
                break;
        }
    }

    /// <summary>Opens the list of windows under <paramref name="anchor"/>, the one it sits in ticked.</summary>
    public static void OpenMenu(FrameworkElement anchor)
    {
        if (Window.GetWindow(anchor) is { } current) Menus.OpenBelow(anchor, Menu(current));
    }

    private static object?[] Menu(Window current)
    {
        var items = new List<object?>();
        if (Main is { } main) items.Add(Row("MikuDo", "main window", main, current));
        items.Add(Menus.Gap);
        foreach (var reader in NoteWindow.All)
            items.Add(Row(reader.Note.IsDirty ? reader.Note.FileName + "  •" : reader.Note.FileName, reader.Note.FolderName, reader, current));
        if (NoteWindow.All.Count > 0)
        {
            items.Add(Menus.Gap);
            items.Add(Menus.Item(NoteWindow.All.Count == 1 ? "Close the reader window" : "Close all reader windows", NoteWindow.CloseAll));
        }
        return items.ToArray();
    }

    private static MenuItem Row(string name, string detail, Window window, Window current)
    {
        var row = Menus.Check(name, ReferenceEquals(window, current), new RelayCommand(() => Show(window)));
        row.InputGestureText = detail;
        return row;
    }
}

/// <summary>How many reader windows are open, for the windows buttons in the title bars.</summary>
public sealed partial class OpenReaders : ObservableObject
{
    public static OpenReaders Current { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Any))]
    private int _count;

    public bool Any => Count > 0;

    /// <summary>Raised whenever a reader window opens, closes or shows another note.</summary>
    public event Action? Changed;

    internal void Update()
    {
        Count = NoteWindow.All.Count;
        Changed?.Invoke();
    }
}
