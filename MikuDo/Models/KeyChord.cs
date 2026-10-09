using System.Windows.Input;

namespace MikuDo.Models;

/// <summary>A key and the modifiers held with it: what a keyboard shortcut is.</summary>
public readonly record struct KeyChord(Key Key, ModifierKeys Modifiers)
{
    public bool HasCtrl => Modifiers.HasFlag(ModifierKeys.Control);
    public bool HasAlt => Modifiers.HasFlag(ModifierKeys.Alt);
    public bool HasShift => Modifiers.HasFlag(ModifierKeys.Shift);

    /// <summary>The chord a key press makes, with the modifiers held at that moment.</summary>
    public static KeyChord From(KeyEventArgs e) => new(RealKey(e), Keyboard.Modifiers);

    /// <summary>
    /// The key itself. With Alt held WPF reports <see cref="Key.System"/> and
    /// puts the key in <see cref="KeyEventArgs.SystemKey"/>; an input method
    /// does the same with its own fields.
    /// </summary>
    public static Key RealKey(KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.DeadCharProcessed => e.DeadCharProcessedKey,
        _ => e.Key
    };

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                                                      or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    /// <summary>
    /// Why this chord cannot be a shortcut, or null when it can. A key on its
    /// own would be typed into whatever has the caret, so it needs Ctrl or Alt;
    /// the function keys and Esc are the exceptions.
    /// </summary>
    public string? Problem
    {
        get
        {
            if (IsModifierKey(Key)) return "Hold Ctrl, Alt or Shift and press a key";
            if (Modifiers.HasFlag(ModifierKeys.Windows)) return "Windows keeps the Windows key for itself";
            if (Key == Key.F4 && HasAlt) return "Alt F4 closes the window";
            if (Key is Key.Enter && Modifiers == ModifierKeys.None) return "Enter on its own saves the shortcut";
            if (Key is >= Key.F1 and <= Key.F24 || Key == Key.Escape) return null;
            if (!HasCtrl && !HasAlt) return $"Add Ctrl or Alt: {Name(Key)} on its own is typed as text";
            return null;
        }
    }

    // ── Text ────────────────────────────────────────────────────

    /// <summary>The caps drawn for the chord, modifiers first: Ctrl, Alt, Shift.</summary>
    public IReadOnlyList<string> Caps
    {
        get
        {
            var caps = new List<string>(4);
            if (HasCtrl) caps.Add("Ctrl");
            if (HasAlt) caps.Add("Alt");
            if (HasShift) caps.Add("Shift");
            if (Key != Key.None && !IsModifierKey(Key)) caps.Add(Name(Key));
            return caps;
        }
    }

    /// <summary>The chord in words, as in "Reset to Ctrl T".</summary>
    public string Label => string.Join(" ", Caps);

    /// <summary>How the chord is stored: "Ctrl+Shift+V", the key by its <see cref="Key"/> name.</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (HasCtrl) parts.Add("Ctrl");
        if (HasAlt) parts.Add("Alt");
        if (HasShift) parts.Add("Shift");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    public static KeyChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key)) return null;

        var modifiers = ModifierKeys.None;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                default: return null;
            }
        }
        return new KeyChord(key, modifiers);
    }

    /// <summary>A key as it is printed on the keyboard.</summary>
    public static string Name(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {(int)(key - Key.NumPad0)}",
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Multiply => "Num *",
        Key.Divide => "Num /",
        Key.Decimal => "Num .",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe or Key.OemBackslash => "\\",
        Key.OemTilde => "`",
        Key.Escape => "Esc",
        Key.Enter => "Enter",
        Key.Back => "Backspace",
        Key.Delete => "Del",
        Key.Insert => "Ins",
        Key.PageUp => "PgUp",
        Key.PageDown => "PgDn",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Space => "Space",
        Key.Tab => "Tab",
        _ => key.ToString()
    };
}
