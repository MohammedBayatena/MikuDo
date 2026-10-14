using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MikuDo.Controls;

/// <summary>
/// How large things are drawn, and where a note's page sits. App size makes the
/// whole app bigger: an accessibility choice whose first step is the normal
/// size, so it never shrinks below it. Text size zooms a note in either reader.
/// A note's page runs across the whole width or sits in a centred column. Each
/// is stored as a setting, and every window follows a change on the spot.
/// </summary>
public sealed partial class Display : ObservableObject
{
    public static Display Current { get; } = new();

    /// <summary>The sizes Settings offers for the whole app. The first is the normal size, and the smallest.</summary>
    public static readonly double[] AppSizes = { 1.0, 1.1, 1.25, 1.4, 1.6 };

    /// <summary>The steps a note's text zooms through, as a browser's do.</summary>
    public static readonly double[] NoteZooms = { 0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppScaleLabel))]
    private double _appScale = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteZoomLabel), nameof(IsNoteZoomed), nameof(CanZoomNoteIn), nameof(CanZoomNoteOut))]
    private double _noteZoom = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteWidthId))]
    private bool _isNoteCentered;

    /// <summary>
    /// One transform shared by the content of every window, so a new size
    /// reaches all of them at once. Popups, menus and tooltips take the
    /// transform of what they open from, so they grow with it.
    /// </summary>
    public ScaleTransform AppTransform { get; } = new(1, 1);

    public string AppScaleLabel => Percent(AppScale);
    public string NoteZoomLabel => Percent(NoteZoom);
    public bool IsNoteZoomed => Math.Abs(NoteZoom - 1) > 0.001;
    public bool CanZoomNoteIn => NoteZoom < NoteZooms[^1] - 0.001;
    public bool CanZoomNoteOut => NoteZoom > NoteZooms[0] + 0.001;

    /// <summary>"Full" or "Centered", as Settings names the two.</summary>
    public string NoteWidthId => IsNoteCentered ? Centered : Full;

    public const string Full = "Full";
    public const string Centered = "Centered";

    public static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    partial void OnAppScaleChanged(double value) => AppTransform.ScaleX = AppTransform.ScaleY = value;

    /// <summary>Reads the stored sizes; anything stored that is not offered settles on the nearest that is.</summary>
    public void Load()
    {
        AppScale = Nearest(AppSizes, Read("AppScale"));
        NoteZoom = Nearest(NoteZooms, Read("NoteZoom"));
        IsNoteCentered = App.Database.GetSetting("NoteWidth") == Centered;
    }

    public void SetAppScale(double scale)
    {
        AppScale = Nearest(AppSizes, scale);
        Save("AppScale", AppScale);
    }

    /// <summary>One step bigger or smaller; a step past either end stays at the end.</summary>
    public void StepNoteZoom(int step)
    {
        var at = Array.IndexOf(NoteZooms, Nearest(NoteZooms, NoteZoom));
        SetNoteZoom(NoteZooms[Math.Clamp(at + step, 0, NoteZooms.Length - 1)]);
    }

    public void SetNoteZoom(double zoom)
    {
        NoteZoom = Nearest(NoteZooms, zoom);
        Save("NoteZoom", NoteZoom);
    }

    public void SetNoteCentered(bool centered)
    {
        IsNoteCentered = centered;
        App.Database.SaveSetting("NoteWidth", centered ? Centered : Full);
    }

    private static double Read(string key)
        => double.TryParse(App.Database.GetSetting(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 1;

    private static void Save(string key, double value)
        => App.Database.SaveSetting(key, value.ToString("0.###", CultureInfo.InvariantCulture));

    private static double Nearest(double[] steps, double value) => steps.MinBy(s => Math.Abs(s - value));

    // ── Windows ─────────────────────────────────────────────────

    /// <summary>
    /// Draws <paramref name="root"/>, a window's content, at the app's size. The
    /// strip along the top that drags the window grows with the title bar it
    /// sits under, which is <paramref name="captionHeight"/> at the normal size.
    /// </summary>
    public static void Scale(Window window, FrameworkElement root, double captionHeight)
    {
        root.LayoutTransform = Current.AppTransform;

        void SyncCaption()
        {
            if (WindowChrome.GetWindowChrome(window) is not { } chrome) return;
            if (chrome.IsFrozen)
            {
                chrome = (WindowChrome)chrome.Clone();
                WindowChrome.SetWindowChrome(window, chrome);
            }
            chrome.CaptionHeight = captionHeight * Current.AppScale;
        }

        PropertyChangedEventHandler changed = (_, e) =>
        {
            if (e.PropertyName == nameof(AppScale)) SyncCaption();
        };
        SyncCaption();
        Current.PropertyChanged += changed;
        window.Closed += (_, _) => Current.PropertyChanged -= changed;
    }
}
