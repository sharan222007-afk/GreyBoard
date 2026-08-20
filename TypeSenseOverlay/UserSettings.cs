using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TypeSenseOverlay;

internal sealed class UserSettings
{
    public string Theme { get; set; } = "Dark";
    public bool Glass { get; set; }
    public int TransparencyPercent { get; set; } = 94;
    public string Placement { get; set; } = "FollowCaret";
    public bool PositionLocked { get; set; }
    public double FixedLeft { get; set; } = 80.0;
    public double FixedTop { get; set; } = 80.0;

    // Classic prediction shortcuts
    public string AcceptFirst { get; set; } = "Ctrl+Alt+1";
    public string AcceptSecond { get; set; } = "Ctrl+Alt+2";
    public string AcceptThird { get; set; } = "Ctrl+Alt+3";

    // Prediction shortcut mode:
    // Classic = individual shortcuts for prediction 1/2/3
    // Advanced = hold Key 1 + tap Key 2 to cycle, release Key 1 to commit
    public string ShortcutMode { get; set; } = "Classic";

    // Advanced prediction gesture defaults.
    public string AdvancedKey1 { get; set; } = "Shift";
    public string AdvancedKey2 { get; set; } = "Alt";

    public string PauseShortcut { get; set; } = "Ctrl+Alt+P";
    public string PersonalCorrections { get; set; } = "";
    public string ProtectedWords { get; set; } = "";
    public string PendingAutocorrections { get; set; } = "";
    public string SelectedLanguagePack { get; set; } = "English";
    public string EnabledLanguagePacks { get; set; } = "English,Tenglish";
    public bool PersonalLearningEnabled { get; set; } = true;
    public bool TrustedAutocorrectEnabled { get; set; } = true;

    // Runtime-only lookup caches. They are rebuilt whenever settings are saved.
    private readonly HashSet<string> _protectedWordCache =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _personalCorrectionCache =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Deckboard",
            "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new UserSettings();

            string json = File.ReadAllText(FilePath);

            UserSettings? settings =
                JsonSerializer.Deserialize<UserSettings>(json);

            if (settings == null)
                return new UserSettings();

            bool migrated = false;

            // Migrate old prediction shortcut defaults.
            if (settings.AcceptFirst.Equals(
                    "Tab",
                    StringComparison.OrdinalIgnoreCase) ||
                settings.AcceptFirst.Equals(
                    "Alt+Shift+B",
                    StringComparison.OrdinalIgnoreCase))
            {
                settings.AcceptFirst = "Ctrl+Alt+1";
                migrated = true;
            }

            if (settings.AcceptSecond.Equals(
                    "Ctrl+2",
                    StringComparison.OrdinalIgnoreCase) ||
                settings.AcceptSecond.Equals(
                    "Alt+Shift+N",
                    StringComparison.OrdinalIgnoreCase))
            {
                settings.AcceptSecond = "Ctrl+Alt+2";
                migrated = true;
            }

            if (settings.AcceptThird.Equals(
                    "Ctrl+3",
                    StringComparison.OrdinalIgnoreCase) ||
                settings.AcceptThird.Equals(
                    "Alt+Shift+M",
                    StringComparison.OrdinalIgnoreCase))
            {
                settings.AcceptThird = "Ctrl+Alt+3";
                migrated = true;
            }

            // Older settings files will not have these properties.
            // Explicitly restore the new defaults when they are missing.
            if (string.IsNullOrWhiteSpace(settings.ShortcutMode))
            {
                settings.ShortcutMode = "Classic";
                migrated = true;
            }

            if (string.IsNullOrWhiteSpace(settings.AdvancedKey1))
            {
                settings.AdvancedKey1 = "Shift";
                migrated = true;
            }

            if (string.IsNullOrWhiteSpace(settings.AdvancedKey2))
            {
                settings.AdvancedKey2 = "Alt";
                migrated = true;
            }

            settings.RebuildLookupCaches();

            if (migrated)
                settings.Save();

            return settings;
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save()
    {
        try
        {
            RebuildLookupCaches();
            string? directory = Path.GetDirectoryName(FilePath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(this));
        }
        catch
        {
        }
    }

    private void RebuildLookupCaches()
    {
        _protectedWordCache.Clear();
        foreach (string word in ProtectedWords.Split(
                     new[] { '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            _protectedWordCache.Add(word);
        }

        _personalCorrectionCache.Clear();
        foreach (string line in PersonalCorrections.Split(
                     new[] { '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                _personalCorrectionCache[parts[0]] = parts[1].ToLowerInvariant();
        }
    }

    public bool IsProtectedWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        if (_protectedWordCache.Count == 0 && !string.IsNullOrWhiteSpace(ProtectedWords))
            RebuildLookupCaches();

        return _protectedWordCache.Contains(word.Trim());
    }

    public void RecordAutocorrection(
        string typed,
        string replacement)
    {
        if (string.IsNullOrWhiteSpace(typed) ||
            string.IsNullOrWhiteSpace(replacement) ||
            typed.Equals(
                replacement,
                StringComparison.OrdinalIgnoreCase) ||
            IsProtectedWord(typed))
        {
            return;
        }

        var lines = PendingAutocorrections
            .Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .ToList();

        string prefix =
            typed.Trim().ToLowerInvariant() + " ->";

        if (!lines.Any(x =>
                x.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add(
                typed.Trim().ToLowerInvariant() +
                " -> " +
                replacement.Trim().ToLowerInvariant());

            PendingAutocorrections =
                string.Join(
                    Environment.NewLine,
                    lines.TakeLast(120));

            Save();
        }
    }

    public bool ConfirmOriginalWord(string typed)
    {
        if (string.IsNullOrWhiteSpace(typed) ||
            IsProtectedWord(typed))
        {
            return false;
        }

        var pending = PendingAutocorrections
            .Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .ToList();

        string prefix =
            typed.Trim().ToLowerInvariant() + " ->";

        int index = pending.FindIndex(x =>
            x.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase));

        if (index < 0)
            return false;

        pending.RemoveAt(index);

        var protectedWords = ProtectedWords
            .Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .ToList();

        protectedWords.Add(
            typed.Trim().ToLowerInvariant());

        var corrections = PersonalCorrections
            .Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(x =>
            {
                string key = x.Split('=', 2)[0].Trim();

                return !key.Equals(
                    typed.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        PersonalCorrections =
            string.Join(
                Environment.NewLine,
                corrections.TakeLast(120));

        ProtectedWords =
            string.Join(
                Environment.NewLine,
                protectedWords
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .TakeLast(300));

        PendingAutocorrections =
            string.Join(
                Environment.NewLine,
                pending.TakeLast(120));

        Save();

        return true;
    }

    public string? PreferredFor(string typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return null;

        if (_personalCorrectionCache.Count == 0 && !string.IsNullOrWhiteSpace(PersonalCorrections))
            RebuildLookupCaches();

        return _personalCorrectionCache.TryGetValue(typed.Trim(), out string? preferred)
            ? preferred
            : null;
    }

    public void RememberCorrection(
        string typed,
        string preferred)
    {
        if (!typed.Equals(
                preferred,
                StringComparison.OrdinalIgnoreCase) &&
            typed.Length >= 3)
        {
            List<string> lines =
                PersonalCorrections.Split(
                    new char[2] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .ToList();

            string replacement =
                typed.ToLowerInvariant() +
                " = " +
                preferred.ToLowerInvariant();

            int index = lines.FindIndex(
                line =>
                    line.Split('=', 2)[0]
                        .Trim()
                        .Equals(
                            typed,
                            StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
                lines[index] = replacement;
            else
                lines.Add(replacement);

            PersonalCorrections =
                string.Join(
                    Environment.NewLine,
                    lines.TakeLast(120));

            Save();
        }
    }
}