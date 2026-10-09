using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MikuDo.Models;
using MikuDo.Services;
using MikuDo.ViewModels;
using MikuDo.Views;

namespace MikuDo;

public partial class App : Application
{
    public static DatabaseService Database { get; set; } = null!;
    public static bool IsDark { get; private set; }

    /// <summary>The theme on screen.</summary>
    public static AppTheme CurrentTheme { get; private set; } = ThemeCatalog.Default;

    // Built the first time something asks. The board needs none of them, so
    // paying for a Markdown pipeline and the llama bindings before the window
    // is up only delays the window.
    private static readonly Lazy<EncryptionService> LazyEncryption = new(() => new EncryptionService());
    private static readonly Lazy<MarkdownService> LazyMarkdown = new(() => new MarkdownService());
    private static readonly Lazy<AiImportService> LazyAiImport = new(() => new AiImportService());
    private static readonly Lazy<DictationService> LazyDictation = new(() => new DictationService());

    public static EncryptionService Encryption => LazyEncryption.Value;
    public static MarkdownService Markdown => LazyMarkdown.Value;
    public static AiImportService AiImport => LazyAiImport.Value;
    public static DictationService Dictation => LazyDictation.Value;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogService.Error("Unhandled exception off the UI thread", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogService.Error("Faulted task nobody awaited", args.Exception);
            args.SetObserved();
        };

        // A second start hands its file to the MikuDo already open and goes.
        if (!SingleInstance.TryBecomeFirst(e.Args))
        {
            Shutdown();
            return;
        }

        LogService.Info("MikuDo starting");
        TuneToolTips();

        var splash = new SplashWindow();
        splash.Show();

        try
        {
            splash.SetStatus("Initializing database...");
            await Task.Run(() => Database = new DatabaseService());

            splash.SetStatus("Loading...");
            // "Light" and "Dark" were stored before themes had names; they become Sun and Moon.
            var stored = Database.GetSetting("Theme");
            var theme = ThemeCatalog.Resolve(stored);
            SetTheme(theme, persist: stored != theme.Id);
            Controls.Layout.Current.IsFlat = Database.GetSetting("Layout") == Controls.Layout.Flat;

            var main = new MainViewModel();
            var mainWindow = new MainWindow { DataContext = main };
            // The splash is shown first, so it would otherwise stay the app's
            // MainWindow and dialogs would end up ownerless.
            MainWindow = mainWindow;

            // A Markdown file given by Open with opens in a reader window alone;
            // the main window waits until asked for.
            if (OpenGiven(main, e.Args.FirstOrDefault()) == null) mainWindow.Show();
            splash.Close();

            SingleInstance.Listen(path => Dispatcher.BeginInvoke(() =>
            {
                if (OpenGiven(main, path) == null) ShowMain(mainWindow);
            }));
            _ = Task.Run(OpenWith.Register);
        }
        catch (Exception ex)
        {
            LogService.Error("Startup failed", ex);
            splash.Close();
            Report("MikuDo could not start.", ex);
            Shutdown(1);
        }
    }

    /// <summary>
    /// Opens a Markdown file MikuDo was started with, or handed by a later
    /// start, in a reader window. Null when there was no such file to open.
    /// </summary>
    private static NoteWindow? OpenGiven(MainViewModel main, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !NoteFiles.IsNote(path)) return null;
        return NoteWindow.Open(main, path);
    }

    /// <summary>The main window, shown if it was waiting behind a reader, and brought to the front.</summary>
    private static void ShowMain(Window mainWindow)
    {
        if (!mainWindow.IsVisible) mainWindow.Show();
        BringForward(mainWindow);
    }

    /// <summary>Restores the window if it was minimised, as it was, and brings it to the front.</summary>
    internal static void BringForward(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (window.WindowState == WindowState.Minimized) ShowWindow(handle, 9);
        window.Activate();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    /// <summary>
    /// Windows waits a full second before showing a tooltip and hides it after
    /// five, which makes an icon-heavy toolbar feel unresponsive. Every icon
    /// button here relies on its tooltip to say what it does, so they appear
    /// almost at once and stay long enough to read.
    /// </summary>
    private static void TuneToolTips()
    {
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(280));
        ToolTipService.ShowDurationProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(20000));
        ToolTipService.BetweenShowDelayProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(120));
    }

    /// <summary>How many failures inside <see cref="StormWindow"/> count as unrecoverable.</summary>
    private const int StormLimit = 5;

    private static readonly TimeSpan StormWindow = TimeSpan.FromSeconds(3);

    /// <summary>Past this many dialogs, the rest of a run's errors only reach the log.</summary>
    private const int MaxDialogs = 3;

    private readonly Queue<DateTime> _failures = new();
    private int _dialogs;

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Error("Unhandled exception on the UI thread", e.Exception);
        e.Handled = true;

        // Marking a failed layout pass handled hands control straight back to
        // the layout that threw, which throws again on the spot. Left alone
        // that loop eats the stack until Windows kills the process, which
        // reports nothing at all, so a run of failures this fast is treated as
        // unrecoverable and the app closes while it can still say why.
        if (IsStorm())
        {
            LogService.Error($"{StormLimit} failures inside {StormWindow.TotalSeconds:N0}s; closing", null);
            MessageBox.Show(
                $"MikuDo hit repeated errors and has to close.\n\nWhat happened is recorded in:\n{LogService.FilePath}",
                "MikuDo Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (_dialogs++ < MaxDialogs) Report("Something went wrong.", e.Exception);
    }

    private bool IsStorm()
    {
        var now = DateTime.UtcNow;
        _failures.Enqueue(now);
        while (_failures.Count > 0 && now - _failures.Peek() > StormWindow) _failures.Dequeue();
        return _failures.Count >= StormLimit;
    }

    private static void Report(string headline, Exception error)
        => MessageBox.Show(
            $"{headline}\n\n{error.GetType().Name}: {error.Message}\n\nFull details:\n{LogService.FilePath}",
            "MikuDo Error", MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>
    /// Swaps the one theme dictionary for another. Every colour in the app is a
    /// DynamicResource onto its tokens, so the swap alone recolours what is on
    /// screen; brushes captured in code are rebuilt by whoever holds them.
    /// </summary>
    public static void SetTheme(AppTheme theme, bool persist = true)
    {
        CurrentTheme = theme;
        IsDark = theme.IsDark;
        var merged = Current.Resources.MergedDictionaries;

        for (int i = merged.Count - 1; i >= 0; i--)
        {
            if (IsThemeDictionary(merged[i]))
            {
                merged.RemoveAt(i);
                break;
            }
        }

        merged.Insert(0, new ResourceDictionary { Source = theme.Source });
        Palette.InstallTints(Current.Resources);
        Palette.Invalidate();

        if (persist) Database?.SaveSetting("Theme", theme.Id);
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
        => dictionary.Source is { } source &&
           ThemeCatalog.All.Any(t => source.OriginalString.EndsWith($"Themes/{t.Id}.xaml", StringComparison.OrdinalIgnoreCase));

    protected override void OnExit(ExitEventArgs e)
    {
        if (LazyAiImport.IsValueCreated) LazyAiImport.Value.Dispose();
        if (LazyDictation.IsValueCreated) LazyDictation.Value.Dispose();
        if (LazyEncryption.IsValueCreated) LazyEncryption.Value.LockVault();
        Database?.Dispose();
        base.OnExit(e);
    }
}
