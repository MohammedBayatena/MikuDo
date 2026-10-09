namespace MikuDo.Models;

/// <summary>One of the themes the app ships, each a ResourceDictionary under Themes/.</summary>
public sealed record AppTheme(string Id, string Name, string Description, bool IsDark)
{
    /// <summary>Absolute, so the dictionary loads whatever assembly asks for it.</summary>
    public Uri Source => new($"pack://application:,,,/MikuDo;component/Themes/{Id}.xaml", UriKind.Absolute);
}

/// <summary>
/// The themes, in the order Settings offers them. Every colour in them was
/// fitted and checked for WCAG contrast before it was written.
/// </summary>
public static class ThemeCatalog
{
    public static readonly AppTheme Sun = new("Sun", "Sun",
        "Crisp near-white with violet. The classic light theme.", IsDark: false);

    public static readonly AppTheme Moon = new("Moon", "Moon",
        "Deep navy with soft violet. The classic dark theme.", IsDark: true);

    public static readonly AppTheme MikuSpecial = new("MikuSpecial", "Miku Special",
        "Miku's teal and deep-teal ties on clean white, with her signature pink.", IsDark: false);

    public static readonly AppTheme PastelPink = new("PastelPink", "Pastel Pink",
        "Creamy peach and pastel pink, with warm plum text.", IsDark: false);

    public static readonly AppTheme Contrast = new("Contrast", "Contrast",
        "Classic high contrast: near-black, near-white and yellow. All text 7:1 or more.", IsDark: true);

    public static readonly AppTheme[] All = { Sun, Moon, MikuSpecial, PastelPink, Contrast };

    public static AppTheme Default => Sun;

    /// <summary>A stored theme id; the two values stored before themes had names map to Sun and Moon.</summary>
    public static AppTheme Resolve(string? id) => id switch
    {
        "Light" => Sun,
        "Dark" => Moon,
        _ => All.FirstOrDefault(t => t.Id == id) ?? Default
    };
}
