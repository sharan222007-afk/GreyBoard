using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TypeSenseOverlay;

internal sealed class LanguagePack
{
    public string Name { get; set; } = "";
    public string Language { get; set; } = "";
    public List<string> Vocabulary { get; set; } = new();
    public Dictionary<string, string> Autocorrect { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class LanguagePackManager
{
    private static string UserPacksPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Deckboard", "Languages");

    private static string ProjectPacksPath =>
        Path.Combine(Directory.GetCurrentDirectory(), "Languages");

    private static string BasePacksPath =>
        Path.Combine(AppContext.BaseDirectory, "Languages");

    private static IEnumerable<string> CandidatePackRoots()
    {
        // In development, the project Languages folder is the one the user
        // edits. In a built app, AppContext.BaseDirectory is used. User data
        // remains a fallback for existing installations.
        yield return ProjectPacksPath;
        yield return BasePacksPath;
        yield return UserPacksPath;
    }

    public static IReadOnlyList<string> GetInstalledPackNames()
    {
        EnsureDefaultPacks();

        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in CandidatePackRoots())
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string directory in Directory.EnumerateDirectories(root))
                {
                    string? name = Path.GetFileName(directory);
                    if (!string.IsNullOrWhiteSpace(name) &&
                        File.Exists(Path.Combine(directory, "pack.json")))
                    {
                        names.Add(name);
                    }
                }
            }
            catch
            {
            }
        }

        if (names.Count == 0)
        {
            names.Add("English");
            names.Add("Tenglish");
        }

        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static LanguagePack? Load(string name)
    {
        EnsureDefaultPacks();

        LanguagePack? bestPack = null;
        int bestVocabularyCount = -1;

        foreach (string root in CandidatePackRoots())
        {
            string path = Path.Combine(root, name, "pack.json");
            if (!File.Exists(path))
                continue;

            try
            {
                LanguagePack? pack = JsonSerializer.Deserialize<LanguagePack>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (pack == null || pack.Vocabulary.Count == 0)
                    continue;

                // Prefer the richest valid pack. This prevents an old tiny
                // per-user fallback pack from silently overriding the bundled
                // 124k-word English pack.
                if (pack.Vocabulary.Count > bestVocabularyCount)
                {
                    bestPack = pack;
                    bestVocabularyCount = pack.Vocabulary.Count;
                }
            }
            catch
            {
            }
        }

        return bestPack;
    }

    public static void OpenPacksFolder()
    {
        EnsureDefaultPacks();

        string root = Directory.Exists(ProjectPacksPath)
            ? ProjectPacksPath
            : UserPacksPath;

        try
        {
            Directory.CreateDirectory(root);
            Process.Start(new ProcessStartInfo
            {
                FileName = root,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static void EnsureDefaultPacks()
    {
        try
        {
            Directory.CreateDirectory(UserPacksPath);
            EnsurePack("English", "English",
                new[] { "the", "to", "and", "I", "you", "a", "is", "it", "that", "for", "hello", "help", "good", "great", "new", "few" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["helo"] = "hello"
                });

            EnsurePack("Tenglish", "Telugu (Roman/Tenglish)",
                new[] { "em", "enti", "ela", "unnav", "bagunna", "bagunnava", "bagunnav", "nenu", "nuvvu", "meeru", "avunu", "kadu", "kaadu", "ledu", "sare", "inka", "ippudu", "vachava", "cheppu", "naaku", "neeku", "enduku", "ekkada", "eppudu", "chesa", "chesanu", "chesav", "chesaru", "chestha", "chesthanu", "chestunnanu", "chestunnav", "chestunnaru", "thinnanu", "thinnav" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
        }
    }

    private static void EnsurePack(string folder, string language, IEnumerable<string> vocabulary, Dictionary<string, string> autocorrect)
    {
        string directory = Path.Combine(UserPacksPath, folder);
        string file = Path.Combine(directory, "pack.json");
        if (File.Exists(file))
            return;

        Directory.CreateDirectory(directory);

        var pack = new LanguagePack
        {
            Name = folder,
            Language = language,
            Vocabulary = vocabulary.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Autocorrect = autocorrect
        };

        File.WriteAllText(file, JsonSerializer.Serialize(pack, new JsonSerializerOptions { WriteIndented = true }));
    }
}
