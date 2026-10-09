using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MikuDo.Models;
using MikuDo.Services;

namespace MikuDo.ViewModels;

/// <summary>
/// A command as Quick Access and Settings show it: its words, its keys as they
/// stand now, and what running it does. Running it says whether it did
/// anything, so a key it could not use here goes on to whatever else wants it.
/// </summary>
public sealed class AppCommand : ObservableObject
{
    private readonly Func<bool> _run;
    private readonly Func<string>? _name;

    public AppCommand(CommandDef def, Func<bool> run, Func<string>? name = null)
    {
        Def = def;
        _run = run;
        _name = name;
    }

    public CommandDef Def { get; }
    public string Id => Def.Id;

    /// <summary>The name as it reads now: "Switch layout to Flat" while the layout is Islands.</summary>
    public string Name => _name?.Invoke() ?? Def.Name;

    public string Category => Def.Category;
    public ShortcutArea Area => Def.Area;
    public string Description => Def.Description;
    public Geometry? Icon => Application.Current.TryFindResource(Def.Icon) as Geometry;

    public KeyChord? Chord => Shortcuts.ChordFor(Id);
    public IReadOnlyList<string> Caps => Chord?.Caps ?? Array.Empty<string>();
    public bool HasChord => Chord != null;
    public bool IsChanged => Shortcuts.IsChanged(Id);

    public string ResetLabel => Def.Default is { } keys ? $"Reset to {keys.Label}" : "Reset to no shortcut";

    public bool Run() => _run();

    /// <summary>Everything shown about the command may have changed: its keys, or its name.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
