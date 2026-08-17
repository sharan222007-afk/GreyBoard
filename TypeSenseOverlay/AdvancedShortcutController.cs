using System;
using System.Windows.Input;

namespace TypeSenseOverlay;

internal sealed class AdvancedShortcutController
{
    public enum Action
    {
        None,
        CycleNext,
        Commit
    }

    private int _key1 = 16; // Shift
    private int _key2 = 18; // Alt

    private bool _key1Down;
    private bool _key2Down;
    private bool _gestureActive;
    private bool _hasSelection;

    public bool Enabled { get; set; } = true;

    public string Key1Name { get; private set; } = "Shift";
    public string Key2Name { get; private set; } = "Alt";

    public void Configure(string key1, string key2)
    {
        if (TryParseKey(key1, out int parsedKey1))
        {
            _key1 = parsedKey1;
            Key1Name = KeyNameFromVirtualKey(parsedKey1);
        }

        if (TryParseKey(key2, out int parsedKey2))
        {
            _key2 = parsedKey2;
            Key2Name = KeyNameFromVirtualKey(parsedKey2);
        }

        Reset();
    }

    public Action HandleKeyDown(int key)
    {
        if (!Enabled)
            return Action.None;

        key = NormalizeKey(key);

        // Key 1 = hold key.
        if (key == _key1)
        {
            _key1Down = true;
            _key2Down = false;
            _gestureActive = false;
            _hasSelection = false;

            return Action.None;
        }

        // Key 2 only activates when Key 1 is held.
        if (key == _key2 && _key1Down)
        {
            if (_key2Down)
                return Action.None;

            _key2Down = true;
            _gestureActive = true;
            _hasSelection = true;

            return Action.CycleNext;
        }

        return Action.None;
    }

    public Action HandleKeyUp(int key)
    {
        if (!Enabled)
            return Action.None;

        key = NormalizeKey(key);

        if (key == _key2)
        {
            _key2Down = false;
            return Action.None;
        }

        if (key == _key1)
        {
            bool commit =
                _gestureActive &&
                _key1Down &&
                _hasSelection;

            _key1Down = false;
            _key2Down = false;
            _gestureActive = false;
            _hasSelection = false;

            if (commit)
                return Action.Commit;
        }

        return Action.None;
    }

    public bool IsGestureActive
    {
        get { return _gestureActive && _key1Down; }
    }

    public bool IsKey1Down
    {
        get { return _key1Down; }
    }

    public bool IsKey2Down
    {
        get { return _key2Down; }
    }

    public bool IsKey2(int key)
    {
        return NormalizeKey(key) == _key2;
    }

    public bool IsKey1(int key)
    {
        return NormalizeKey(key) == _key1;
    }

    public void Reset()
    {
        _key1Down = false;
        _key2Down = false;
        _gestureActive = false;
        _hasSelection = false;
    }

    public static int NormalizeKey(int key)
    {
        switch (key)
        {
            case 160:
            case 161:
                return 16;

            case 162:
            case 163:
                return 17;

            case 164:
            case 165:
                return 18;

            default:
                return key;
        }
    }

    public static bool TryParseKey(
        string value,
        out int virtualKey)
    {
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string key = value.Trim();

        if (key.Equals("Shift", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 16;
            return true;
        }

        if (key.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Control", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 17;
            return true;
        }

        if (key.Equals("Alt", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 18;
            return true;
        }

        if (key.Equals("Caps Lock", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("CapsLock", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 20;
            return true;
        }

        if (key.Equals("Tab", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 9;
            return true;
        }

        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 32;
            return true;
        }

        if (key.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 13;
            return true;
        }

        if (key.Equals("Backspace", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 8;
            return true;
        }

        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return true;
        }

        return false;
    }

    public static string KeyNameFromVirtualKey(int key)
    {
        key = NormalizeKey(key);

        switch (key)
        {
            case 16:
                return "Shift";

            case 17:
                return "Ctrl";

            case 18:
                return "Alt";

            case 20:
                return "Caps Lock";

            case 9:
                return "Tab";

            case 32:
                return "Space";

            case 13:
                return "Enter";

            case 8:
                return "Backspace";
        }

        if (key >= 65 && key <= 90)
            return ((char)key).ToString();

        if (key >= 48 && key <= 57)
            return ((char)key).ToString();

        return "Unknown";
    }

    public static string FromWpfKey(Key key)
    {
        switch (key)
        {
            case Key.LeftShift:
            case Key.RightShift:
                return "Shift";

            case Key.LeftCtrl:
            case Key.RightCtrl:
                return "Ctrl";

            case Key.LeftAlt:
            case Key.RightAlt:
                return "Alt";

            case Key.CapsLock:
                return "Caps Lock";

            case Key.Tab:
                return "Tab";

            case Key.Space:
                return "Space";

            case Key.Enter:
                return "Enter";

            case Key.Back:
                return "Backspace";
        }

        if (key >= Key.A && key <= Key.Z)
            return key.ToString().ToUpperInvariant();

        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        return string.Empty;
    }
}