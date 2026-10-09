using System.ComponentModel;
using MikuDo.Models;

namespace MikuDo.Services;

/// <summary>
/// A command's keys as the page writes them, for binding: <c>[note.open]</c>
/// of <see cref="Keys"/> reads "Ctrl O" and of <see cref="InParens"/>
/// " (Ctrl+O)", or nothing for a command without keys. Both follow the keys
/// as the user changes them.
/// </summary>
public sealed class ShortcutText : INotifyPropertyChanged
{
    /// <summary>"Ctrl O": a hint beside a button.</summary>
    public static ShortcutText Keys { get; } = new(chord => chord.Label);

    /// <summary>" (Ctrl+O)": the end of a tooltip.</summary>
    public static ShortcutText InParens { get; } = new(chord => $" ({string.Join("+", chord.Caps)})");

    private readonly Func<KeyChord, string> _write;

    private ShortcutText(Func<KeyChord, string> write)
    {
        _write = write;
        Shortcuts.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public string this[string commandId] => Shortcuts.ChordFor(commandId) is { } chord ? _write(chord) : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
}
