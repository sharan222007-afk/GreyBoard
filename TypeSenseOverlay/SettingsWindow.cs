using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TypeSenseOverlay;

internal sealed class SettingsWindow : Window
{
    private readonly UserSettings _settings;
    private readonly TypingEngine _engine;

    private readonly TextBox _first;
    private readonly TextBox _second;
    private readonly TextBox _third;
    private readonly TextBox _pause;

    private readonly TextBox _advancedKey1;
    private readonly TextBox _advancedKey2;

    private readonly ComboBox _shortcutMode;

    private readonly TextBox _personalCorrections;
    private readonly TextBox _learnedWords;

    private readonly ComboBox _theme;
    private readonly ComboBox _placement;

    private readonly CheckBox _glass;
    private readonly CheckBox _positionLocked;
    private readonly CheckBox _personalLearning;
    private readonly CheckBox _trustedAutocorrect;

    private readonly Slider _transparency;
    private readonly TextBlock _transparencyValue = new TextBlock();

    private static SolidColorBrush Ink =>
        new SolidColorBrush(Color.FromRgb(45, 57, 72));

    private static SolidColorBrush Muted =>
        new SolidColorBrush(Color.FromRgb(105, 117, 133));

    public SettingsWindow(
        UserSettings settings,
        TypingEngine engine)
    {
        _settings = settings;
        _engine = engine;

        base.Title = "Grey Board Settings";
        base.Width = 520.0;
        base.Height = 760.0;
        base.MinHeight = 620.0;
        base.MinWidth = 500.0;

        base.WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        base.Background = Brushes.White;

        Grid root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition
                {
                    Height =
                        new GridLength(
                            1.0,
                            GridUnitType.Star)
                },

                new RowDefinition
                {
                    Height = GridLength.Auto
                }
            }
        };

        StackPanel panel = new StackPanel
        {
            Margin =
                new Thickness(
                    27.0,
                    25.0,
                    27.0,
                    22.0)
        };

        panel.Children.Add(
            new TextBlock
            {
                Text = "Grey Board",
                FontWeight = FontWeights.Bold,
                FontSize = 24.0,
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            22,
                            60,
                            110))
            });

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Prediction, language, and popup controls",
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            100,
                            112,
                            128)),
                Margin =
                    new Thickness(
                        0.0,
                        3.0,
                        0.0,
                        18.0)
            });

        // ------------------------------------------------------------
        // SHORTCUTS
        // ------------------------------------------------------------

        panel.Children.Add(
            Section("Prediction shortcuts"));

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Choose how Grey Board accepts predictions.",
                FontSize = 11.0,
                Foreground = Muted,
                Margin =
                    new Thickness(
                        0.0,
                        0.0,
                        0.0,
                        7.0)
            });

        _shortcutMode =
            AddComboRow(
                panel,
                "Shortcut mode",
                new[]
                {
                    "Classic",
                    "Advanced"
                },
                settings.ShortcutMode == "Advanced"
                    ? "Advanced"
                    : "Classic");

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Classic uses individual prediction shortcuts. " +
                    "Advanced uses a two-key hold/tap gesture.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.0,
                Foreground = Muted,
                Margin =
                    new Thickness(
                        0.0,
                        4.0,
                        0.0,
                        12.0)
            });

        // Classic shortcuts
        panel.Children.Add(
            Section("Classic prediction controls"));

        _first =
            AddShortcutRow(
                panel,
                "First prediction",
                settings.AcceptFirst);

        _second =
            AddShortcutRow(
                panel,
                "Second prediction",
                settings.AcceptSecond);

        _third =
            AddShortcutRow(
                panel,
                "Third prediction",
                settings.AcceptThird);

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "These are used only when Shortcut mode is Classic.",
                FontSize = 11.0,
                Foreground = Muted,
                Margin =
                    new Thickness(
                        0.0,
                        2.0,
                        0.0,
                        12.0)
            });

        // Advanced controls
        panel.Children.Add(
            Section("Advanced prediction gesture"));

        _advancedKey1 =
            AddKeyRow(
                panel,
                "Key 1 — Hold",
                settings.AdvancedKey1);

        _advancedKey2 =
            AddKeyRow(
                panel,
                "Key 2 — Cycle",
                settings.AdvancedKey2);

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Hold Key 1 → tap Key 2 repeatedly to cycle " +
                    "predictions → release Key 1 to replace the " +
                    "active word.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.0,
                Foreground = Muted,
                Margin =
                    new Thickness(
                        0.0,
                        5.0,
                        0.0,
                        15.0)
            });

        // Enable / disable the appropriate settings visually.
        _shortcutMode.SelectionChanged += delegate
        {
            UpdateShortcutModeVisibility();
        };

        UpdateShortcutModeVisibility();

        // ------------------------------------------------------------
        // OTHER SHORTCUTS
        // ------------------------------------------------------------

        panel.Children.Add(
            Section("Other shortcuts"));

        _pause =
            AddShortcutRow(
                panel,
                "Pause / resume",
                settings.PauseShortcut);


        // ------------------------------------------------------------
        // LEARNED WORDS
        // ------------------------------------------------------------

        panel.Children.Add(
            Section(
                "Learned words & protected spellings"));

        LanguageProfile learnedProfile =
            LanguageProfile.Load();

        string learnedText =
            string.Join(
                Environment.NewLine,

                learnedProfile.Words
                    .OrderByDescending(
                        x => x.Value)
                    .ThenBy(
                        x => x.Key)
                    .Take(500)
                    .Select(
                        x =>
                            x.Key +
                            " (" +
                            x.Value +
                            ")")

                    .Concat(
                        _settings.ProtectedWords
                            .Split(
                                new[]
                                {
                                    '\r',
                                    '\n'
                                },

                                StringSplitOptions
                                    .RemoveEmptyEntries |
                                StringSplitOptions
                                    .TrimEntries)

                            .Select(
                                x =>
                                    "[protected] " +
                                    x))

                    .Distinct(
                        StringComparer
                            .OrdinalIgnoreCase));

        _learnedWords =
            new TextBox
            {
                Text = learnedText,
                Height = 130.0,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping =
                    TextWrapping.Wrap,

                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,

                Padding =
                    new Thickness(8.0)
            };

        panel.Children.Add(_learnedWords);

        // ------------------------------------------------------------
        // APPEARANCE
        // ------------------------------------------------------------

        panel.Children.Add(
            Section("Appearance"));

        _theme =
            AddComboRow(
                panel,
                "Color mode",
                new[]
                {
                    "Dark",
                    "Light"
                },
                settings.Theme);

        _glass =
            new CheckBox
            {
                Content =
                    "Glass effect (translucent, Apple-inspired)",

                IsChecked =
                    settings.Glass,

                Margin =
                    new Thickness(
                        0.0,
                        8.0,
                        0.0,
                        6.0),

                Foreground = Ink
            };

        panel.Children.Add(_glass);

        Grid opacityRow =
            new Grid
            {
                Margin =
                    new Thickness(
                        0.0,
                        3.0,
                        0.0,
                        15.0)
            };

        opacityRow.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(150.0)
            });

        opacityRow.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1.0,
                        GridUnitType.Star)
            });

        opacityRow.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(42.0)
            });

        opacityRow.Children.Add(
            new TextBlock
            {
                Text = "Transparency",
                VerticalAlignment =
                    VerticalAlignment.Center,
                Foreground = Ink
            });

        _transparency =
            new Slider
            {
                Minimum = 35.0,
                Maximum = 100.0,

                Value =
                    Math.Clamp(
                        settings.TransparencyPercent,
                        35,
                        100),

                TickFrequency = 5.0,
                IsSnapToTickEnabled = true,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        _transparency.ValueChanged += delegate
        {
            _transparencyValue.Text =
                $"{(int)_transparency.Value}%";
        };

        Grid.SetColumn(
            _transparency,
            1);

        opacityRow.Children.Add(
            _transparency);

        _transparencyValue.Text =
            $"{(int)_transparency.Value}%";

        _transparencyValue.HorizontalAlignment =
            HorizontalAlignment.Right;

        _transparencyValue.VerticalAlignment =
            VerticalAlignment.Center;

        _transparencyValue.Foreground = Muted;

        Grid.SetColumn(
            _transparencyValue,
            2);

        opacityRow.Children.Add(
            _transparencyValue);

        panel.Children.Add(opacityRow);

        // ------------------------------------------------------------
        // POSITION
        // ------------------------------------------------------------

        panel.Children.Add(
            Section("Position"));

        _placement =
            AddComboRow(
                panel,
                "Popup position",
                new[]
                {
                    "Follow typing caret",
                    "Fixed position - use move handle"
                },

                settings.Placement == "Fixed"
                    ? "Fixed position - use move handle"
                    : "Follow typing caret");

        _positionLocked =
            new CheckBox
            {
                Content =
                    "Lock fixed position",

                IsChecked =
                    settings.PositionLocked,

                Margin =
                    new Thickness(
                        0.0,
                        8.0,
                        0.0,
                        16.0),

                Foreground = Ink
            };

        panel.Children.Add(
            _positionLocked);

        _placement.SelectionChanged += delegate
        {
            _positionLocked.IsEnabled =
                _placement.SelectedIndex == 1;

            if (!_positionLocked.IsEnabled)
                _positionLocked.IsChecked = false;
        };

        _positionLocked.IsEnabled =
            _placement.SelectedIndex == 1;

        // ------------------------------------------------------------
        // PERSONAL CORRECTIONS
        // ------------------------------------------------------------

        panel.Children.Add(
            Section(
                "Roman Telugu & personal spellings"));

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Add your own corrections, one per line. " +
                    "Grey Board also remembers corrections when " +
                    "you accept a suggestion.",

                TextWrapping =
                    TextWrapping.Wrap,

                FontSize = 11.0,
                Foreground = Muted,

                Margin =
                    new Thickness(
                        0.0,
                        0.0,
                        0.0,
                        6.0)
            });

        _personalCorrections =
            new TextBox
            {
                Text =
                    settings.PersonalCorrections,

                Height = 112.0,

                AcceptsReturn = true,

                TextWrapping =
                    TextWrapping.Wrap,

                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,

                Padding =
                    new Thickness(8.0),

                ToolTip =
                    "chesthnunaav = chesthunnav"
            };

        panel.Children.Add(
            _personalCorrections);

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Example: chesthnunaav = chesthunnav",

                FontSize = 11.0,
                Foreground = Muted,

                Margin =
                    new Thickness(
                        2.0,
                        5.0,
                        0.0,
                        5.0)
            });

        // ------------------------------------------------------------
        // SCROLL AREA
        // ------------------------------------------------------------

        ScrollViewer scroll =
            new ScrollViewer
            {
                Content = panel,

                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto
            };

        Grid.SetRow(scroll, 0);

        root.Children.Add(scroll);

        // ------------------------------------------------------------
        // FOOTER
        // ------------------------------------------------------------

        Border footer =
            new Border
            {
                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            224,
                            230,
                            239)),

                BorderThickness =
                    new Thickness(
                        0.0,
                        1.0,
                        0.0,
                        0.0),

                Padding =
                    new Thickness(
                        27.0,
                        13.0,
                        27.0,
                        15.0),

                Background =
                    Brushes.White
            };

        StackPanel actions =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        Button cancel =
            new Button
            {
                Content = "Cancel",

                Padding =
                    new Thickness(
                        17.0,
                        8.0,
                        17.0,
                        8.0),

                Margin =
                    new Thickness(
                        0.0,
                        0.0,
                        8.0,
                        0.0)
            };

        cancel.Click += delegate
        {
            Close();
        };

        Button save =
            new Button
            {
                Content = "Save changes",

                Padding =
                    new Thickness(
                        18.0,
                        8.0,
                        18.0,
                        8.0),

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            28,
                            103,
                            242)),

                Foreground =
                    Brushes.White,

                BorderBrush =
                    Brushes.Transparent
            };

        save.Click += Save;

        actions.Children.Add(cancel);
        actions.Children.Add(save);

        footer.Child = actions;

        Grid.SetRow(
            footer,
            1);

        root.Children.Add(footer);

        base.Content = root;
    }

    private void UpdateShortcutModeVisibility()
    {
        bool advanced =
            _shortcutMode.SelectedItem?.ToString()
                ?.Equals(
                    "Advanced",
                    StringComparison.OrdinalIgnoreCase)
            == true;

        SetEnabled(
            _advancedKey1,
            advanced);

        SetEnabled(
            _advancedKey2,
            advanced);

        SetEnabled(
            _first,
            !advanced);

        SetEnabled(
            _second,
            !advanced);

        SetEnabled(
            _third,
            !advanced);
    }

    private static void SetEnabled(
        Control control,
        bool enabled)
    {
        control.IsEnabled = enabled;
        control.Opacity = enabled ? 1.0 : 0.45;
    }

    private static TextBlock Section(
        string value)
    {
        return new TextBlock
        {
            Text = value,

            FontWeight =
                FontWeights.SemiBold,

            FontSize = 14.0,

            Margin =
                new Thickness(
                    0.0,
                    0.0,
                    0.0,
                    5.0),

            Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        40,
                        55,
                        75))
        };
    }

    private static TextBox AddKeyRow(
        Panel panel,
        string label,
        string value)
    {
        Grid row =
            new Grid
            {
                Margin =
                    new Thickness(
                        0.0,
                        3.0,
                        0.0,
                        3.0)
            };

        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1.0,
                        GridUnitType.Star)
            });

        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(156.0)
            });

        row.Children.Add(
            new TextBlock
            {
                Text = label,

                VerticalAlignment =
                    VerticalAlignment.Center,

                Foreground = Ink
            });

        TextBox box =
            new TextBox
            {
                Text = value,
                IsReadOnly = true,
                Focusable = true,

                Padding =
                    new Thickness(
                        7.0,
                        4.0,
                        7.0,
                        4.0),

                ToolTip =
                    "Click here and press the key you want"
            };

        box.PreviewKeyDown += delegate (
            object sender,
            KeyEventArgs e)
        {
            e.Handled = true;

            Key key =
                e.Key == Key.System
                    ? e.SystemKey
                    : e.Key;

            string keyName =
                AdvancedShortcutController
                    .FromWpfKey(key);

            if (string.IsNullOrWhiteSpace(keyName) ||
                keyName == "Unknown")
            {
                box.Text =
                    "Unsupported key";

                return;
            }

            box.Text = keyName;
        };

        Grid.SetColumn(box, 1);

        row.Children.Add(box);

        panel.Children.Add(row);

        return box;
    }

    private static TextBox AddShortcutRow(
        Panel panel,
        string label,
        string value)
    {
        Grid row =
            new Grid
            {
                Margin =
                    new Thickness(
                        0.0,
                        3.0,
                        0.0,
                        3.0)
            };

        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1.0,
                        GridUnitType.Star)
            });

        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(156.0)
            });

        row.Children.Add(
            new TextBlock
            {
                Text = label,

                VerticalAlignment =
                    VerticalAlignment.Center,

                Foreground = Ink
            });

        TextBox box =
            new TextBox
            {
                Text = value,
                IsReadOnly = true,
                Focusable = true,

                Padding =
                    new Thickness(
                        7.0,
                        4.0,
                        7.0,
                        4.0),

                ToolTip =
                    "Click here and press your desired shortcut"
            };

        box.PreviewKeyDown += delegate (
            object sender,
            KeyEventArgs e)
        {
            e.Handled = true;

            Key key =
                e.Key == Key.System
                    ? e.SystemKey
                    : e.Key;

            if (key == Key.LeftCtrl ||
                key == Key.RightCtrl ||
                key == Key.LeftAlt ||
                key == Key.RightAlt ||
                key == Key.LeftShift ||
                key == Key.RightShift ||
                key == Key.LWin ||
                key == Key.RWin)
            {
                return;
            }

            string keyName;

            if (key == Key.Space)
                keyName = "Space";
            else if (key == Key.Tab)
                keyName = "Tab";
            else if (key >= Key.A &&
                     key <= Key.Z)
                keyName =
                    key.ToString()
                        .ToUpperInvariant();
            else if (key >= Key.D0 &&
                     key <= Key.D9)
                keyName =
                    ((int)key -
                     (int)Key.D0)
                    .ToString();
            else
            {
                box.Text =
                    "Unsupported key";

                return;
            }

            ModifierKeys modifiers =
                Keyboard.Modifiers;

            if ((modifiers &
                 (ModifierKeys.Control |
                  ModifierKeys.Alt |
                  ModifierKeys.Shift |
                  ModifierKeys.Windows))
                == ModifierKeys.None)
            {
                box.Text =
                    "Add Ctrl, Alt, Shift or Win";

                return;
            }

            List<string> parts =
                new List<string>();

            if ((modifiers &
                 ModifierKeys.Control) != 0)
            {
                parts.Add("Ctrl");
            }

            if ((modifiers &
                 ModifierKeys.Alt) != 0)
            {
                parts.Add("Alt");
            }

            if ((modifiers &
                 ModifierKeys.Shift) != 0)
            {
                parts.Add("Shift");
            }

            if ((modifiers &
                 ModifierKeys.Windows) != 0)
            {
                parts.Add("Win");
            }

            parts.Add(keyName);

            box.Text =
                string.Join(
                    "+",
                    parts);
        };

        Grid.SetColumn(box, 1);

        row.Children.Add(box);

        panel.Children.Add(row);

        return box;
    }

    private static TextBox AddTextRow(
        Panel panel,
        string label,
        string value)
    {
        Grid row =
            new Grid
            {
                Margin =
                    new Thickness(
                        0.0,
                        3.0,
                        0.0,
                        3.0)
            };

        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1.0,
                        GridUnitType.Star)
            });

        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(218.0)
            });

        row.Children.Add(
            new TextBlock
            {
                Text = label,

                VerticalAlignment =
                    VerticalAlignment.Center,

                Foreground = Ink
            });

        TextBox box =
            new TextBox
            {
                Text = value,

                Padding =
                    new Thickness(6.0)
            };

        Grid.SetColumn(box, 1);

        row.Children.Add(box);

        panel.Children.Add(row);

        return box;
    }

    private static ComboBox AddComboRow(
        Panel panel,
        string label,
        string[] options,
        string value)
    {
        Grid row =
            new Grid
            {
                Margin =
                    new Thickness(
                        0.0,
                        3.0,
                        0.0,
                        3.0)
            };

        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1.0,
                        GridUnitType.Star)
            });

        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(218.0)
            });

        row.Children.Add(
            new TextBlock
            {
                Text = label,

                VerticalAlignment =
                    VerticalAlignment.Center,

                Foreground = Ink
            });

        ComboBox box =
            new ComboBox
            {
                ItemsSource = options,

                SelectedItem = value,

                Padding =
                    new Thickness(4.0)
            };

        Grid.SetColumn(box, 1);

        row.Children.Add(box);

        panel.Children.Add(row);

        return box;
    }

    private void Save(
        object? sender,
        RoutedEventArgs e)
    {
        string[] classicValues =
        {
            _first.Text.Trim(),
            _second.Text.Trim(),
            _third.Text.Trim(),
            _pause.Text.Trim()
        };

        bool advanced =
            _shortcutMode.SelectedItem
                ?.ToString()
                ?.Equals(
                    "Advanced",
                    StringComparison.OrdinalIgnoreCase)
            == true;

        if (classicValues.Any(
                x => !Shortcut.IsValid(x)) ||
            classicValues
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count() != classicValues.Length)
        {
            MessageBox.Show(
                "Use distinct classic shortcuts such as Ctrl+Alt+1, Ctrl+Alt+2, or Ctrl+Alt+3.",
                "Check your shortcuts",
                MessageBoxButton.OK,
                MessageBoxImage.Exclamation);

            return;
        }

        string advancedKey1 =
            _advancedKey1.Text.Trim();

        string advancedKey2 =
            _advancedKey2.Text.Trim();

        if (!AdvancedShortcutController.TryParseKey(
                advancedKey1,
                out _))
        {
            MessageBox.Show(
                "Choose a valid Key 1 for Advanced mode.",
                "Check Advanced shortcut",
                MessageBoxButton.OK,
                MessageBoxImage.Exclamation);

            return;
        }

        if (!AdvancedShortcutController.TryParseKey(
                advancedKey2,
                out _))
        {
            MessageBox.Show(
                "Choose a valid Key 2 for Advanced mode.",
                "Check Advanced shortcut",
                MessageBoxButton.OK,
                MessageBoxImage.Exclamation);

            return;
        }

        if (advancedKey1.Equals(
                advancedKey2,
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Key 1 and Key 2 must be different.",
                "Check Advanced shortcut",
                MessageBoxButton.OK,
                MessageBoxImage.Exclamation);

            return;
        }

        _settings.AcceptFirst =
            classicValues[0];

        _settings.AcceptSecond =
            classicValues[1];

        _settings.AcceptThird =
            classicValues[2];

        _settings.PauseShortcut =
            classicValues[3];

        _settings.ShortcutMode =
            advanced
                ? "Advanced"
                : "Classic";

        _settings.AdvancedKey1 =
            advancedKey1;

        _settings.AdvancedKey2 =
            advancedKey2;

        _settings.Theme =
            _theme.SelectedItem?.ToString()
            ?? "Dark";

        _settings.Glass =
            _glass.IsChecked == true;

        _settings.TransparencyPercent =
            (int)_transparency.Value;

        _settings.Placement =
            _placement.SelectedIndex == 1
                ? "Fixed"
                : "FollowCaret";

        _settings.PositionLocked =
            _positionLocked.IsChecked == true;

        _settings.PersonalCorrections =
            _personalCorrections.Text.Trim();

        _settings.Save();

        _engine.ApplySettings();

        Close();
    }
}