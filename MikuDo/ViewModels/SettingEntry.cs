namespace MikuDo.ViewModels;

/// <summary>The parts of the Settings page a link can open it at. Each is the name of that part in SettingsView.</summary>
public static class SettingsSections
{
    public const string Screenshot = "SectionScreenshot";
    public const string Theme = "SectionTheme";
    public const string Layout = "SectionLayout";
    public const string AutoSave = "SectionAutoSave";
    public const string AiModel = "SectionAiModel";
    public const string Tips = "SectionTips";
    public const string Gif = "SectionGif";
    public const string Dictation = "SectionDictation";
    public const string DictationMode = "SectionDictationMode";
    public const string Backup = "SectionBackup";
    public const string Shortcuts = "SectionShortcuts";
}

/// <summary>
/// A setting as Quick Access finds it: its name and what the page says about
/// it, the area it belongs to, and its value now.
/// </summary>
public sealed record SettingEntry(string Name, string Area, string Description, string Section, Func<MainViewModel, string?> Now)
{
    private static string OnOff(bool on) => on ? "On" : "Off";

    public static IReadOnlyList<SettingEntry> All { get; } = new SettingEntry[]
    {
        new("Theme", "Appearance",
            "Every theme is checked for contrast: text reaches at least 4.5:1 against what it sits on, and 7:1 in Contrast. Toggle theme switches between the light and dark theme you used last.",
            SettingsSections.Theme, _ => App.CurrentTheme.Name),
        new("Layout", "Appearance",
            "Islands sets each list and panel on the page as a card of its own. Flat runs them edge to edge, divided by borders, with each list under a heading in its colour. Works with every theme.",
            SettingsSections.Layout, _ => Controls.Layout.Current.IsFlat ? "Flat" : "Islands"),
        new("Screenshot Mode", "Capture",
            "Choose whether Take a screenshot captures the primary monitor or every screen",
            SettingsSections.Screenshot, main => main.ScreenshotAllScreens ? "All screens" : "Primary monitor"),
        new("Auto Save", "Tasks",
            "Save a task as you edit it and when you leave its page, instead of waiting for Save. A recording still running when a page closes is always kept.",
            SettingsSections.AutoSave, main => OnOff(main.IsAutoSave)),
        new("AI model", "AI",
            "Runs on your CPU, for AI Import, Rewrite with AI, writing up dictation and summaries in exports. With no model installed, AI Import falls back to keyword rules.",
            SettingsSections.AiModel, _ => App.AiImport.IsModelDownloaded ? "installed" : "none installed"),
        new("Tips", "Sidebar",
            "A short tip about a feature, now and then, at the foot of the sidebar.",
            SettingsSections.Tips, main => OnOff(main.IsTipsOn)),
        new("Sidebar GIF", "Sidebar",
            "Pick a GIF and it plays in a box under the tip. With none picked, the sidebar has no box. It only animates while MikuDo is in front of you, so it costs next to nothing.",
            SettingsSections.Gif, main => main.HasSidebarGif ? "chosen" : "none"),
        new("Dictation", "Dictation",
            "Speak on a task's page and your words go into its description. The speech model runs on your CPU, so nothing you say leaves this PC. Until one is downloaded, Dictate stays greyed out.",
            SettingsSections.Dictation, _ => null),
        new("Default dictation mode", "Dictation",
            "Exact puts your words in as spoken. Write up has the AI Import model turn them into a tidy description: filler words out, lists as bullet points.",
            SettingsSections.DictationMode, _ => App.Database.GetSetting("DictationWriteUp") != "False" ? "Write up" : "Exact"),
        new("Database Backup", "Data",
            "Export or import a full backup of all your data",
            SettingsSections.Backup, _ => null),
        new("Keyboard shortcuts", "Keyboard",
            "Change the keys any command runs on, find a command by its keys, or put them all back as they started.",
            SettingsSections.Shortcuts, _ => Services.Shortcuts.ChangedCount is var n and > 0 ? $"{n} changed" : null),
    };
}
