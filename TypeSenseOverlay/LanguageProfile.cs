using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TypeSenseOverlay;

internal sealed class LanguageProfile
{
    private static readonly string[] Defaults =
    {
        "the","to","and","I","you","a","is","it","that","for","with","this","my","on","we","have",
        "just","really","would","love","your","in","of","hello","help","helpful","good","great",
        "going","go","come","coming","what","when","where","why","how","who","which","can","could",
        "will","should","shall","do","does","did","done","doing","am","are","was","were","be","been",
        "being","he","she","they","them","his","her","their","me","us","our","not","no","yes","okay",
        "ok","please","thanks","thank","sorry","today","tomorrow","yesterday","now","later","here",
        "there","home","work","time","want","need","like","know","think","see","look","tell","say",
        "said","make","made","take","give","get","got","came","went","one","two","three","first",
        "last","new","old","more","most","some","very","only","also","even","because","before",
        "after","about","from","into","without","computer","phone","message","friend","family",
        "college","school","student","learn","learning","programming","project","meeting",
        "suit","suitable","suitability","suitably","suited","suits","suiting","suggest","suggestion",
        "suggestions","complete","completion","different","important","possible","problem","question",
        "answer","sure","maybe","always","never","already","again","together","around","under",
        "between","during","every","each","both","many","better","best","next","something","someone",
        "anything","everything","nothing","everyone","people","place","person","thing","things",
        "market","house","store","message","start","finish","study","call","check","send","feel",
        "right","wrong","easy","hard","ready","happy","sorry"
    };

    private static readonly string[] RomanTelugu =
    {
        "em","enti","ela","unnav","chesthunnav","thinnava","bagunnava","bagunna","nenu","nuvvu",
        "meeru","avunu","kadu","kaadu","ledu","sare","inka","ippudu","vellava","vastava","vachava",
        "emaina","cheppu","naaku","neeku","enduku","ekkada","eppudu","aithe","chesa","chesanu",
        "chesav","chesaru","chestha","chesthanu","chestunnanu","chestunnav","chestunnaru","thinnanu",
        "thinnav","vellali","velthunna","velthunnanu","vastunnanu","vastunna","ivala","repu",
        "ninna","manchi","baaga","leduga","avunu","kaavali","kaadu","enti","enduku","ela"
    };

    private static readonly string[] CommonWordsOrdered =
    {
        "the","of","and","to","in","a","is","that","for","it","as","was","with","be","by","on","not",
        "he","I","this","are","or","his","from","at","which","but","have","an","had","you","were",
        "their","they","one","all","we","can","her","has","there","been","if","more","when","will",
        "would","who","so","what","up","about","out","do","like","just","time","into","than","could",
        "our","my","your","me","good","new","some","very","them","these","then","now","people","also",
        "only","other","how","want","because","well","make","over","think","see","know","take","come",
        "go","look","use","find","give","need","tell","work","should","try","ask","feel","really",
        "right","back","much","where","help","before","great","through","life","first","last","long",
        "little","same","another","while","day","still","here","something","anything","everything",
        "nothing","please","thanks","thank","sorry","hello","today","tomorrow","later","home","school",
        "college","student","learn","learning","computer","phone","message","friend","family","project",
        "meeting","programming","different","important","possible","problem","question","answer","sure",
        "maybe","always","never","already","again","together","around","after","under","without",
        "between","during","every","each","both","many","most","better","best","next","complete",
        "completion","suggest","suggestion","suggestions","suitable","suitability","suitably","suited",
        "suits","suiting","someone","everyone"
    };

    private static readonly Dictionary<string, int> CommonFrequency =
        BuildFrequencyMap(CommonWordsOrdered);

    private static readonly Dictionary<string, Dictionary<string, int>> SeedContexts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["i"] = Map("am:140","want:110","will:90","can:80","have:70","need:65","like:60","think:55","was:35"),
            ["i\u001fwant"] = Map("to:180"),
            ["i\u001fam"] = Map("going:150","a:70","not:60","really:50","looking:45"),
            ["i\u001fneed"] = Map("to:160","a:80","some:60","help:55"),
            ["i\u001fwould"] = Map("like:150","love:100","be:75"),
            ["i\u001fwant\u001fto"] = Map("go:130","learn:120","know:95","make:85","get:80","see:70"),
            ["i\u001fam\u001fgoing"] = Map("to:180"),
            ["going\u001fto"] = Map("the:120","college:100","school:90","work:85","go:60","be:50"),
            ["want\u001fto"] = Map("learn:120","go:110","know:85","make:75","get:70","see:60"),
            ["need\u001fto"] = Map("go:100","get:95","find:90","know:75","make:65"),
            ["looking\u001ffor"] = Map("a:140","the:100","some:75","my:60"),
            ["how\u001fare"] = Map("you:180","things:65"),
            ["thank\u001fyou"] = Map("for:170","so:90"),
            ["see\u001fyou"] = Map("soon:120","tomorrow:100","there:75","again:65"),
            ["can\u001fyou"] = Map("help:150","please:125","tell:105","give:95","send:85"),
            ["could\u001fyou"] = Map("please:150","help:135","tell:105","send:90"),
            ["please\u001fhelp"] = Map("me:160","with:110"),
            ["what\u001fis"] = Map("the:140","this:90","that:80"),
            ["where\u001fis"] = Map("the:140","it:95","my:75"),
            ["there\u001fis"] = Map("a:145","no:75","nothing:55"),
            ["it\u001fis"] = Map("a:125","not:90","the:80","really:65"),
            ["i\u001flike"] = Map("to:135","the:90","this:80","it:70"),
            ["i\u001fthink"] = Map("that:140","it:90","the:75","we:65"),
            ["i\u001fknow"] = Map("that:130","the:80","it:75","you:65"),
            ["nenu"] = Map("bagunna:150","chesthunnanu:120","velthunnanu:110","vellali:90"),
            ["nuvvu"] = Map("ela:150","em:105","ekkada:85","unnav:75"),
            ["ela\u001funnav"] = Map("ippudu:85","ivala:70","baaga:65"),
            ["nenu\u001fcollege"] = Map("ki:100","lo:70"),
            ["college\u001fki"] = Map("vellali:130","velthunna:105","velthunnanu:90"),
            ["nenu\u001fcollege\u001fki"] = Map("vellali:160","velthunna:120","velthunnanu:100")
        };

    private static readonly Dictionary<string, int> SeedPairs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["em|chesthunnav"] = 90,
            ["em|enti"] = 70,
            ["chesthunnav|thinnava"] = 80,
            ["nuvvu|ela"] = 65,
            ["ela|unnav"] = 75,
            ["bagunnava|nuvvu"] = 60,
            ["nenu|bagunna"] = 65
        };

    private static readonly HashSet<string> CommonVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "go","get","give","make","take","learn","know","see","come","tell","help","find","use","try",
        "ask","send","call","check","start","finish","do","be","have","want","need","like","love",
        "look","work","study","learn","complete"
    };

    private static readonly HashSet<string> CommonNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "college","school","work","home","house","store","market","computer","phone","message",
        "project","meeting","problem","question","answer","time","day","place","person","student",
        "friend","family","world","thing","things","completion","suggestion"
    };

    private static readonly HashSet<string> CommonAdjectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "good","great","bad","new","old","different","important","possible","sure","ready","happy",
        "sorry","better","best","right","wrong","easy","hard","suitable","complete"
    };

    private static readonly HashSet<string> DefaultSet =
        new(Defaults, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> RomanTeluguSet =
        new(RomanTelugu, StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> Words { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> Pairs { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, int>> ContextNext { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    private List<string> _packVocabulary = new();
    private List<string>? _vocabularyCache;
    private HashSet<string>? _vocabularySet;
    private Dictionary<string, List<string>>? _prefixBuckets;
    private List<string>? _learnedHotWords;
    private Dictionary<string, string> _autocorrect =
        new(StringComparer.OrdinalIgnoreCase);
    private string _languageName = "English";
    private bool _dirty;

    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Deckboard",
        "profile.json");

    internal bool IsDirty => _dirty;

    public static LanguageProfile Load()
    {
        try
        {
            if (!File.Exists(Path))
                return new LanguageProfile();

            LanguageProfile profile =
                JsonSerializer.Deserialize<LanguageProfile>(File.ReadAllText(Path))
                ?? new LanguageProfile();

            profile.InvalidateVocabularyIndex();
            profile._learnedHotWords = null;
            profile._dirty = false;
            return profile;
        }
        catch
        {
            return new LanguageProfile();
        }
    }

    public void SetLanguagePack(LanguagePack? pack)
    {
        _languageName = pack?.Name ?? "English";
        _autocorrect = pack?.Autocorrect != null
            ? new Dictionary<string, string>(pack.Autocorrect, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        _packVocabulary = pack?.Vocabulary
            .Where(IsUsableWord)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string>();

        InvalidateVocabularyIndex();
    }

    private void InvalidateVocabularyIndex()
    {
        _vocabularyCache = null;
        _vocabularySet = null;
        _prefixBuckets = null;
        _learnedHotWords = null;
    }

    private void EnsureVocabularyIndex()
    {
        if (_vocabularyCache != null &&
            _vocabularySet != null &&
            _prefixBuckets != null &&
            _learnedHotWords != null)
            return;

        IEnumerable<string> extraWords =
            IsRomanLanguage()
                ? RomanTelugu
                : Array.Empty<string>();

        _vocabularyCache = _packVocabulary
            .Concat(Defaults)
            .Concat(CommonWordsOrdered)
            .Concat(extraWords)
            .Concat(Words.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsUsableWord)
            .ToList();

        _vocabularySet = new HashSet<string>(_vocabularyCache, StringComparer.OrdinalIgnoreCase);
        _prefixBuckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string word in _vocabularyCache)
            AddWordToPrefixIndex(word);

        _learnedHotWords = Words
            .Where(x => IsUsableWord(x.Key))
            .OrderByDescending(x => x.Value)
            .Take(160)
            .Select(x => x.Key)
            .ToList();
    }

    private void AddWordToPrefixIndex(string word)
    {
        if (_prefixBuckets == null || string.IsNullOrWhiteSpace(word))
            return;

        string normalized = word.Trim().ToLowerInvariant();
        // Long prefixes need their own buckets. Indexing up to 12 characters
        // keeps lookups tiny for normal typing while avoiding a full-vocabulary
        // scan for prefixes such as "suitabilit".
        int maxPrefix = Math.Min(12, normalized.Length);

        for (int length = 1; length <= maxPrefix; length++)
        {
            string prefix = normalized[..length];

            if (!_prefixBuckets.TryGetValue(prefix, out List<string>? bucket))
            {
                bucket = new List<string>();
                _prefixBuckets[prefix] = bucket;
            }

            bucket.Add(word.Trim());
        }
    }

    private static bool IsUsableWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        word = word.Trim();

        if (word.Length < 2 || word.Length > 48)
            return false;

        return word.All(c => char.IsLetter(c) || c == '\'' || c == '-');
    }

    private bool IsRomanLanguage() =>
        _languageName.Contains("Telugu", StringComparison.OrdinalIgnoreCase) ||
        _languageName.Contains("Tenglish", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<string> GetCandidatesForPrefix(string prefix)
    {
        EnsureVocabularyIndex();

        string normalized = prefix.Trim().ToLowerInvariant();

        if (normalized.Length == 0)
            return _vocabularyCache!;

        string key = normalized[..Math.Min(12, normalized.Length)];

        if (_prefixBuckets!.TryGetValue(key, out List<string>? bucket))
            return bucket;

        return Array.Empty<string>();
    }

    private List<string> GetFuzzyPrefixCandidates(string prefix, int maxPool = 96)
    {
        EnsureVocabularyIndex();

        string normalized = prefix.Trim().ToLowerInvariant();
        if (normalized.Length < 3)
            return new List<string>();

        int keyLength = Math.Min(3, normalized.Length);
        string key = normalized[..keyLength];

        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

        // Keep the exact short-prefix bucket in the fuzzy pool. This is
        // essential for long typoed prefixes such as "suitabili".
        keys.Add(key);

        // Internal adjacent transposition, e.g. "nistable" -> "instable".
        for (int i = 0; i < key.Length - 1; i++)
        {
            char[] chars = key.ToCharArray();
            (chars[i], chars[i + 1]) = (chars[i + 1], chars[i]);
            keys.Add(new string(chars));
        }

        // One-character deletion from the prefix, useful for an accidental
        // duplicated character such as "suuita" -> "suitable".
        for (int i = 0; i < key.Length; i++)
            keys.Add(key.Remove(i, 1));

        // One-character replacement in the short prefix. This is deliberately
        // limited to the first three characters, keeping the lookup bounded.
        const string alphabet = "abcdefghijklmnopqrstuvwxyz";
        for (int i = 0; i < key.Length; i++)
        {
            foreach (char replacement in alphabet)
            {
                if (replacement == key[i])
                    continue;

                char[] chars = key.ToCharArray();
                chars[i] = replacement;
                keys.Add(new string(chars));
            }
        }

        List<string> pool = new(maxPool);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string candidateKey in keys)
        {
            if (!_prefixBuckets!.TryGetValue(candidateKey, out List<string>? bucket))
                continue;

            foreach (string word in bucket)
            {
                if (!seen.Add(word))
                    continue;

                pool.Add(word);

                if (pool.Count >= maxPool)
                    return pool;
            }
        }

        return pool;
    }

    public void Learn(string word, string previous)
    {
        Learn(
            word,
            string.IsNullOrWhiteSpace(previous)
                ? Array.Empty<string>()
                : new[] { previous });
    }

    public void Learn(string word, IReadOnlyList<string> recentContext)
    {
        if (!IsUsableWord(word))
            return;

        string normalized = word.Trim().ToLowerInvariant();

        Words[normalized] = Words.GetValueOrDefault(normalized) + 1;

        List<string> history = recentContext
            .Where(IsUsableWord)
            .Select(x => x.Trim().ToLowerInvariant())
            .TakeLast(4)
            .ToList();

        if (history.Count > 0)
        {
            string previous = history[^1];
            string pair = previous + "|" + normalized;
            Pairs[pair] = Pairs.GetValueOrDefault(pair) + 1;
        }

        for (int length = 1; length <= Math.Min(4, history.Count); length++)
        {
            string key = BuildContextKey(history.TakeLast(length));

            if (!ContextNext.TryGetValue(key, out Dictionary<string, int>? next))
            {
                next = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ContextNext[key] = next;
            }

            next[normalized] = next.GetValueOrDefault(normalized) + 1;
        }

        EnsureVocabularyIndex();

        if (_vocabularySet!.Add(normalized))
        {
            _vocabularyCache!.Add(normalized);
            AddWordToPrefixIndex(normalized);
        }

        _learnedHotWords = null;
        _dirty = true;
    }

    public bool IsKnownWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        EnsureVocabularyIndex();
        return _vocabularySet!.Contains(word.Trim());
    }

    public string? TryGetAutocorrection(string typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return null;

        return _autocorrect.TryGetValue(typed.Trim(), out string? correction)
            ? correction
            : null;
    }

    private static Dictionary<string, int> BuildFrequencyMap(IEnumerable<string> words)
    {
        Dictionary<string, int> result =
            new(StringComparer.OrdinalIgnoreCase);

        int rank = 0;

        foreach (string raw in words)
        {
            if (!IsUsableWord(raw))
                continue;

            string word = raw.ToLowerInvariant();

            int score = Math.Max(24, 260 - rank * 2);
            result[word] = Math.Max(result.GetValueOrDefault(word), score);

            rank++;
        }

        foreach (string word in new[]
        {
            "suit","suitable","suitability","suitably","suited","suits","suiting",
            "suggestion","suggestions","helpful","something","someone","everything"
        })
        {
            result[word] = Math.Max(result.GetValueOrDefault(word), 125);
        }

        return result;
    }

    private int GlobalPrior(string word)
    {
        int score = CommonFrequency.GetValueOrDefault(word);

        if (score == 0)
        {
            score = word.Length switch
            {
                <= 2 => 22,
                3 => 20,
                4 => 18,
                5 => 15,
                6 => 12,
                _ => 8
            };
        }

        if (DefaultSet.Contains(word))
            score += 28;

        if (RomanTeluguSet.Contains(word))
            score += 28;

        score += Math.Min(Words.GetValueOrDefault(word) * 12, 300);
        return score;
    }

    private int ContextScore(IReadOnlyList<string> history, string word)
    {
        if (history.Count == 0)
            return 0;

        int best = 0;

        for (int length = Math.Min(4, history.Count); length >= 1; length--)
        {
            string key = BuildContextKey(history.TakeLast(length));

            int seed = 0;
            if (SeedContexts.TryGetValue(key, out Dictionary<string, int>? seedNext))
                seed = seedNext.GetValueOrDefault(word);

            int learned = 0;
            if (ContextNext.TryGetValue(key, out Dictionary<string, int>? learnedNext))
                learned = learnedNext.GetValueOrDefault(word);

            int combined = seed + Math.Min(learned * 45, 500);

            if (combined > best)
            {
                int multiplier = length switch
                {
                    4 => 4,
                    3 => 3,
                    2 => 2,
                    _ => 1
                };

                best = combined * multiplier;
            }
        }

        string previous = history[^1];
        best += Math.Min(
            Pairs.GetValueOrDefault(previous + "|" + word) * 34,
            320);

        best += SeedPairs.GetValueOrDefault(previous + "|" + word);
        return best;
    }

    private int HeuristicContextScore(IReadOnlyList<string> history, string word)
    {
        if (history.Count == 0)
            return 0;

        string last = history[^1];
        int score = 0;

        if (last.Equals("to", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("can", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("will", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("should", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("could", StringComparison.OrdinalIgnoreCase))
        {
            if (CommonVerbs.Contains(word))
                score += 70;
        }

        if (last.Equals("the", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("my", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("your", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("our", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("a", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("an", StringComparison.OrdinalIgnoreCase))
        {
            if (CommonNouns.Contains(word) || CommonAdjectives.Contains(word))
                score += 45;
        }

        if (last.Equals("is", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("are", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("was", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("were", StringComparison.OrdinalIgnoreCase))
        {
            if (CommonAdjectives.Contains(word))
                score += 55;
        }

        if (IsRomanLanguage() && RomanTeluguSet.Contains(last))
        {
            if (RomanTeluguSet.Contains(word))
                score += 65;
        }

        return score;
    }

    private int ScoreWord(
        IReadOnlyList<string> history,
        string word,
        string prefix,
        bool completion)
    {
        int score = GlobalPrior(word);
        score += ContextScore(history, word);
        score += HeuristicContextScore(history, word);

        if (completion && prefix.Length > 0)
        {
            int prefixLength = prefix.Length;
            int remaining = Math.Max(0, word.Length - prefixLength);

            // Strong prefix fidelity. A longer shared prefix should dominate
            // generic frequency so common words cannot crowd out the user's
            // actual completion.
            score += Math.Min(prefixLength * 42, 300);
            score -= Math.Min(remaining * 2, 34);

            if (word.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                score -= 5000;
        }

        return score;
    }

    public List<SuggestionCandidate> CandidateModels(
        string previous,
        string prefix,
        string recentContext = "")
    {
        List<string> history = ParseContext(recentContext);

        string normalizedPrevious = previous.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(normalizedPrevious) &&
            (history.Count == 0 ||
             !history[^1].Equals(normalizedPrevious, StringComparison.OrdinalIgnoreCase)))
        {
            history.Add(normalizedPrevious);
        }

        if (history.Count > 4)
            history = history.GetRange(history.Count - 4, 4);

        string normalizedPrefix = prefix.Trim();

        if (normalizedPrefix.Length == 0)
            return NextWordCandidates(history);

        List<SuggestionCandidate> result = new(8);
        HashSet<string> seen =
            new(StringComparer.OrdinalIgnoreCase);

        void AddRealPrefixCandidate(string word, int penalty = 0)
        {
            if (!IsUsableWord(word) ||
                !word.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                word.Equals(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(word))
                return;

            result.Add(new SuggestionCandidate(
                word,
                ScoreWord(history, word, normalizedPrefix, true) - penalty,
                0,
                SuggestionKind.Completion));
        }

        // Fast path: exact prefix index.
        foreach (string word in GetCandidatesForPrefix(normalizedPrefix))
            AddRealPrefixCandidate(word);

        // Fuzzy path: only when exact candidates are insufficient. Retrieval
        // is bounded by short prefix buckets; expensive edit distance is never
        // run over the full vocabulary.
        if (result.Count < 3 && normalizedPrefix.Length >= 3)
        {
            string lowerPrefix = normalizedPrefix.ToLowerInvariant();

            foreach (string word in GetFuzzyPrefixCandidates(normalizedPrefix))
            {
                string lowerWord = word.ToLowerInvariant();

                // Compare the typed prefix only with the corresponding beginning
                // of the candidate, not with the candidate's whole suffix.
                string comparable = lowerWord[..Math.Min(
                    lowerWord.Length,
                    lowerPrefix.Length)];

                int distance = DamerauLevenshtein(
                    lowerPrefix,
                    comparable);

                int allowedDistance =
                    lowerPrefix.Length <= 5 ? 1 : 2;

                if (distance <= allowedDistance)
                {
                    int typoPenalty = distance * 120;

                    if (word.StartsWith(
                        normalizedPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        typoPenalty = 0;
                    }

                    if (!seen.Add(word))
                        continue;

                    result.Add(new SuggestionCandidate(
                        word,
                        ScoreWord(
                            history,
                            word,
                            normalizedPrefix,
                            true) - typoPenalty,
                        0,
                        SuggestionKind.Completion));
                }

                if (result.Count >= 12)
                    break;
            }
        }

        // If a typo still leaves the exact candidate pool short, use a small
        // deterministic vocabulary fallback. This is only for prefix mode and
        // is bounded by the cached hot-word list, never the full vocabulary.
        if (result.Count < 3)
        {
            EnsureVocabularyIndex();

            foreach (string word in _learnedHotWords!.Take(160))
            {
                if (!word.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                AddRealPrefixCandidate(word, 20);

                if (result.Count >= 8)
                    break;
            }
        }

        if (result.Count < 3 && !IsRomanLanguage())
            AddMorphologicalCompletions(normalizedPrefix, result, seen);

        // The strip is always three items. Prefer real completions/corrections;
        // only use contextual predictions to fill missing slots after all
        // prefix-aware candidates have been exhausted.
        if (result.Count < 3)
        {
            foreach (SuggestionCandidate candidate in NextWordCandidates(history))
            {
                if (!seen.Add(candidate.Word))
                    continue;

                result.Add(new SuggestionCandidate(
                    candidate.Word,
                    candidate.Score - 250,
                    0,
                    SuggestionKind.Prediction));

                if (result.Count >= 3)
                    break;
            }
        }

        return result
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Word.Length)
            .ThenBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private void AddMorphologicalCompletions(
        string prefix,
        List<SuggestionCandidate> result,
        HashSet<string> seen)
    {
        EnsureVocabularyIndex();

        // Only derive from an actual dictionary/base candidate. This prevents
        // arbitrary invented strings from becoming suggestions.
        string? baseWord = GetCandidatesForPrefix(prefix)
            .Where(x =>
                x.Length > prefix.Length &&
                x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(GlobalPrior)
            .ThenBy(x => x.Length)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(baseWord))
            return;

        // If the base itself already starts with the requested prefix, it is a
        // legitimate candidate and should remain available before variants.
        if (baseWord.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !baseWord.Equals(prefix, StringComparison.OrdinalIgnoreCase) &&
            seen.Add(baseWord))
        {
            result.Add(new SuggestionCandidate(
                baseWord,
                GlobalPrior(baseWord) - 10,
                0,
                SuggestionKind.Completion));

            if (result.Count >= 3)
                return;
        }

        string[] variants;

        if (baseWord.EndsWith("e", StringComparison.OrdinalIgnoreCase))
        {
            variants = new[]
            {
                baseWord + "s",
                baseWord[..^1] + "ed",
                baseWord[..^1] + "ing"
            };
        }
        else
        {
            variants = new[]
            {
                baseWord + "s",
                baseWord + "ed",
                baseWord + "ing"
            };
        }

        foreach (string variant in variants)
        {
            if (result.Count >= 3)
                break;

            if (!IsUsableWord(variant) ||
                !variant.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(variant))
                continue;

            // Morphological variants are lower-confidence than real vocabulary.
            result.Add(new SuggestionCandidate(
                variant,
                GlobalPrior(baseWord) - 30,
                0,
                SuggestionKind.Completion));
        }
    }

    private List<SuggestionCandidate> NextWordCandidates(
        IReadOnlyList<string> history)
    {
        // Bounded candidate pool: contextual candidates + common cold-start
        // candidates + a small learned-hot set. Never scan all 124k words here.
        Dictionary<string, int> scores =
            new(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string word, int bonus)
        {
            if (!IsUsableWord(word))
                return;

            scores[word] = scores.GetValueOrDefault(word) + bonus;
        }

        for (int length = Math.Min(4, history.Count); length >= 1; length--)
        {
            string key = BuildContextKey(history.TakeLast(length));

            if (SeedContexts.TryGetValue(key, out Dictionary<string, int>? seed))
            {
                foreach (KeyValuePair<string, int> item in seed)
                    AddCandidate(item.Key, item.Value * (length + 1));
            }

            if (ContextNext.TryGetValue(key, out Dictionary<string, int>? learned))
            {
                foreach (KeyValuePair<string, int> item in learned)
                    AddCandidate(item.Key, Math.Min(item.Value * 55, 700));
            }
        }

        foreach (string word in CommonWordsOrdered.Take(360))
            AddCandidate(word, 0);

        EnsureVocabularyIndex();

        if (_learnedHotWords != null)
        {
            foreach (string word in _learnedHotWords.Take(120))
                AddCandidate(word, 0);
        }

        List<SuggestionCandidate> ranked = scores
            .Select(x => new SuggestionCandidate(
                x.Key,
                ScoreWord(history, x.Key, "", false) + x.Value,
                0,
                SuggestionKind.Prediction))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Word.Length)
            .ThenBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (ranked.Count < 3)
        {
            foreach (string word in CommonWordsOrdered.Take(12))
            {
                if (!IsUsableWord(word) ||
                    ranked.Any(x => x.Word.Equals(
                        word,
                        StringComparison.OrdinalIgnoreCase)))
                    continue;

                ranked.Add(new SuggestionCandidate(
                    word,
                    1,
                    0,
                    SuggestionKind.Prediction));

                if (ranked.Count >= 3)
                    break;
            }
        }

        return ranked.Take(3).ToList();
    }

    public List<string> Candidates(
        string previous,
        string prefix,
        string recentContext = "") =>
        CandidateModels(previous, prefix, recentContext)
            .Select(x => x.Word)
            .ToList();

    public string? BestCorrection(string previous, string typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return null;

        typed = typed.Trim();

        string? explicitCorrection = TryGetAutocorrection(typed);
        if (!string.IsNullOrWhiteSpace(explicitCorrection))
            return explicitCorrection;

        int limit = typed.Length <= 4 ? 1 : 2;

        IEnumerable<string> vocabulary =
            GetCandidatesForPrefix(
                typed[..Math.Min(2, typed.Length)]);

        return vocabulary
            .Where(word =>
                !word.Equals(typed, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(word.Length - typed.Length) <= limit)
            .Select(word => new
            {
                Word = word,
                Distance = DamerauLevenshtein(typed, word),
                Score = ScoreWord(
                    string.IsNullOrWhiteSpace(previous)
                        ? Array.Empty<string>()
                        : new[] { previous },
                    word,
                    typed,
                    true)
            })
            .Where(x => x.Distance <= limit)
            .OrderBy(x => x.Distance)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Word)
            .FirstOrDefault();
    }

    private static List<string> ParseContext(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return new List<string>();

        return context
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsUsableWord)
            .Select(x => x.ToLowerInvariant())
            .TakeLast(4)
            .ToList();
    }

    private static string BuildContextKey(IEnumerable<string> words) =>
        string.Join('\u001F', words.Select(x => x.ToLowerInvariant()));

    private static Dictionary<string, int> Map(params string[] entries)
    {
        Dictionary<string, int> result =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (string entry in entries)
        {
            string[] parts = entry.Split(':', 2);

            if (parts.Length != 2 ||
                !int.TryParse(parts[1], out int score))
                continue;

            result[parts[0]] = score;
        }

        return result;
    }

    public void Save()
    {
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                Path,
                JsonSerializer.Serialize(this));

            _dirty = false;
        }
        catch
        {
        }
    }

    private static int DamerauLevenshtein(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        int[,] d = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++)
            d[i, 0] = i;

        for (int j = 0; j <= b.Length; j++)
            d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);

                if (i > 1 &&
                    j > 1 &&
                    a[i - 1] == b[j - 2] &&
                    a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(
                        d[i, j],
                        d[i - 2, j - 2] + cost);
                }
            }
        }

        return d[a.Length, b.Length];
    }
}
