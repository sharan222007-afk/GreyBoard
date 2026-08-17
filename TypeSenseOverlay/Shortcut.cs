using System;
using System.Collections.Concurrent;

namespace TypeSenseOverlay;

internal readonly struct ParsedShortcut
{
    public ParsedShortcut(int key, bool control, bool alt, bool shift, bool valid)
    {
        Key = key;
        Control = control;
        Alt = alt;
        Shift = shift;
        IsValid = valid;
    }

    public int Key { get; }
    public bool Control { get; }
    public bool Alt { get; }
    public bool Shift { get; }
    public bool IsValid { get; }
}

internal static class Shortcut
{
    private static readonly ConcurrentDictionary<string, ParsedShortcut> Cache =
        new ConcurrentDictionary<string, ParsedShortcut>(StringComparer.OrdinalIgnoreCase);

    public static bool IsValid(string value) => Get(value).IsValid;

    public static bool Matches(string value, int key)
    {
        return Matches(value, key, Native.ControlDown, Native.AltDown, Native.ShiftDown);
    }

    public static bool Matches(string value, int key, bool controlDown, bool altDown, bool shiftDown)
    {
        ParsedShortcut shortcut = Get(value);
        return shortcut.IsValid &&
               shortcut.Key == key &&
               shortcut.Control == controlDown &&
               shortcut.Alt == altDown &&
               shortcut.Shift == shiftDown;
    }

    public static ParsedShortcut Get(string value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return Cache.GetOrAdd(normalized, Parse);
    }

    private static ParsedShortcut Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ParsedShortcut(0, false, false, false, false);

        int key = 0;
        int keyTokenCount = 0;
        bool control = false;
        bool alt = false;
        bool shift = false;

        string[] parts = value.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
            return new ParsedShortcut(0, false, false, false, false);

        foreach (string rawPart in parts)
        {
            string part = rawPart;

            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                if (control)
                    return new ParsedShortcut(0, false, false, false, false);

                control = true;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                if (alt)
                    return new ParsedShortcut(0, false, false, false, false);

                alt = true;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                if (shift)
                    return new ParsedShortcut(0, false, false, false, false);

                shift = true;
                continue;
            }

            int parsedKey = 0;

            if (part.Equals("Space", StringComparison.OrdinalIgnoreCase))
                parsedKey = 32;
            else if (part.Equals("Tab", StringComparison.OrdinalIgnoreCase))
                parsedKey = 9;
            else if (part.Equals("Enter", StringComparison.OrdinalIgnoreCase))
                parsedKey = 13;
            else if (part.Equals("Backspace", StringComparison.OrdinalIgnoreCase))
                parsedKey = 8;
            else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                parsedKey = char.ToUpperInvariant(part[0]);
            else
                return new ParsedShortcut(0, false, false, false, false);

            keyTokenCount++;

            if (keyTokenCount > 1)
                return new ParsedShortcut(0, false, false, false, false);

            key = parsedKey;
        }

        bool valid =
            keyTokenCount == 1 &&
            key != 0 &&
            (control || alt || shift);

        return new ParsedShortcut(key, control, alt, shift, valid);
    }
}
