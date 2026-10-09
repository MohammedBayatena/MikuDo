using System.Windows;
using System.Windows.Media;

namespace MikuDo.Models;

/// <summary>Background/foreground pair for a chip.</summary>
public record ChipColors(Brush Background, Brush Foreground);

/// <summary>
/// Colours for labels, statuses and priorities, read from the active theme's
/// semantic tokens. Anything holding a <see cref="Brush"/> from here must be
/// rebuilt when the theme changes, and <see cref="Invalidate"/> must be called
/// first so the next read sees the new theme.
/// </summary>
public static class Palette
{
    public static bool IsDark => App.IsDark;

    /// <summary>
    /// The theme's brushes by key, so a board rebuild that asks for the same
    /// chip once per card looks each one up only once per theme.
    /// </summary>
    private static readonly Dictionary<string, Brush> Cache = new(StringComparer.Ordinal);

    public static void Invalidate() => Cache.Clear();

    private static Brush R(string key)
    {
        if (Cache.TryGetValue(key, out var known)) return known;
        var brush = Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
        return Cache[key] = brush;
    }

    // ── Labels ──────────────────────────────────────────────────
    // The three design labels keep their exact colours; every other tag is
    // hashed onto one of the remaining swatches so it stays stable across runs.

    private static readonly string[] Swatches = { "Green", "Red", "Amber", "Blue", "Violet", "Cyan", "Magenta", "Olive" };

    /// <summary>The labels a new card on the board offers as one-tap chips: the ones in everyday use.</summary>
    public static readonly string[] QuickLabels = { "Bug", "Enhancement", "Feature", "Think About" };

    /// <summary>Labels offered as one-tap chips. The first three come from the design.</summary>
    public static readonly string[] SuggestedLabels =
        { "Internal", "Urgent", "Lead", "Bug", "Feature", "Fix", "Enhancement", "Experiment", "Think About" };

    private static int SwatchIndex(string label)
    {
        switch (label.Trim().ToLowerInvariant())
        {
            case "internal": return 0;
            case "urgent": return 1;
            case "lead": return 2;
        }

        // FNV-1a keeps the same tag on the same swatch between sessions.
        uint hash = 2166136261;
        foreach (var c in label.Trim().ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619;
        }
        return 3 + (int)(hash % (uint)(Swatches.Length - 3));
    }

    public static ChipColors Label(string label)
    {
        var swatch = Swatches[SwatchIndex(label)];
        return new ChipColors(R($"Label{swatch}SoftBrush"), R($"Label{swatch}Brush"));
    }

    // ── Board columns ───────────────────────────────────────────

    public static string ColumnName(TodoStatus status) => status switch
    {
        TodoStatus.Doing => "Doing",
        TodoStatus.Completed => "Done",
        _ => "To do"
    };

    private static string StatusKey(TodoStatus status) => status switch
    {
        TodoStatus.Doing => "Doing",
        TodoStatus.Completed => "Done",
        _ => "Todo"
    };

    /// <summary>Dot colour shown next to a column heading.</summary>
    public static Brush ColumnDot(TodoStatus status) => R($"Status{StatusKey(status)}DotBrush");

    /// <summary>Halo behind the column dot.</summary>
    public static Brush ColumnDotHalo(TodoStatus status) => R($"Status{StatusKey(status)}HaloBrush");

    /// <summary>Text colour used wherever a status is written out.</summary>
    public static Brush StatusForeground(TodoStatus status) => R($"Status{StatusKey(status)}Brush");

    /// <summary>The soft colour a list's header sits on in the Flat layout.</summary>
    public static Brush ColumnTint(TodoStatus status) => R($"Status{StatusKey(status)}TintBrush");

    /// <summary>
    /// The colour of a list's count on its Flat header: its status's own ink,
    /// except Done, whose dot is the accent rather than its green text.
    /// </summary>
    public static Brush ColumnInk(TodoStatus status)
        => status == TodoStatus.Completed ? R("AccentDarkBrush") : StatusForeground(status);

    /// <summary>
    /// Puts the Flat layout's status tints with the app's resources: each
    /// status's halo taken half way to the page colour. Made again for every
    /// theme, under keys DynamicResource follows, so they change with it.
    /// </summary>
    public static void InstallTints(ResourceDictionary resources)
    {
        if (resources["SurfaceBrush"] is not SolidColorBrush { Color: var surface }) return;
        foreach (var key in new[] { "Todo", "Doing", "Done" })
        {
            if (resources[$"Status{key}HaloBrush"] is not SolidColorBrush { Color: var halo }) continue;
            var tint = new SolidColorBrush(Color.FromRgb(
                (byte)((halo.R + surface.R + 1) / 2),
                (byte)((halo.G + surface.G + 1) / 2),
                (byte)((halo.B + surface.B + 1) / 2)));
            tint.Freeze();
            resources[$"Status{key}TintBrush"] = tint;
        }
    }

    public static readonly TodoStatus[] BoardColumns =
        { TodoStatus.Active, TodoStatus.Doing, TodoStatus.Completed };

    public static TodoStatus StatusFromName(string name) => name switch
    {
        "Doing" => TodoStatus.Doing,
        "Done" => TodoStatus.Completed,
        _ => TodoStatus.Active
    };

    // ── Priority ────────────────────────────────────────────────

    public static ChipColors Priority(TodoPriority priority)
    {
        var key = priority == TodoPriority.None ? "None" : priority.ToString();
        return new ChipColors(R($"Priority{key}SoftBrush"), R($"Priority{key}Brush"));
    }

    /// <summary>A colour from the theme by its key, as a CSS hex string: for HTML the theme cannot reach.</summary>
    public static string Css(string key)
    {
        if (R(key) is not SolidColorBrush { Color: var c }) return "transparent";
        return c.A == 255
            ? $"#{c.R:x2}{c.G:x2}{c.B:x2}"
            : FormattableString.Invariant($"rgba({c.R},{c.G},{c.B},{c.A / 255.0:0.###})");
    }
}
