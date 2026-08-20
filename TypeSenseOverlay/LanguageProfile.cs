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
        "the", "to", "and", "I", "you", "a", "is", "it", "that", "for",
        "with", "this", "my", "on", "we", "have", "just", "really", "would", "love",
        "your", "in", "of", "hello", "help", "helpful", "good", "great", "going", "go",
        "come", "coming", "what", "when", "where", "why", "how", "who", "which",
        "can", "could", "will", "should", "shall", "do", "does", "did", "done", "doing",
        "am", "are", "was", "were", "be", "been", "being", "he", "she", "they", "them",
        "his", "her", "their", "me", "us", "our", "not", "no", "yes", "okay", "ok",
        "please", "thanks", "thank", "sorry", "today", "tomorrow", "yesterday", "now", "later",
        "here", "there", "home", "work", "time", "want", "need", "like", "know", "think",
        "see", "look", "tell", "say", "said", "make", "made", "take", "give", "get", "got",
        "came", "went", "one", "two", "three", "first", "last", "new", "old", "more", "most",
        "some", "very", "only", "also", "even", "because", "before", "after", "about", "from",
        "into", "without", "computer", "phone", "message", "friend", "family", "college", "school",
        "student", "learn", "learning", "programming", "project", "meeting"
    };

    private static readonly string[] RomanTelugu =
    {
        "em", "enti", "ela", "unnav", "chesthunnav", "thinnava", "bagunnava", "bagunna", "nenu", "nuvvu",
        "meeru", "avunu", "kadu", "kaadu", "ledu", "sare", "inka", "ippudu", "vellava", "vastava",
        "vachava", "emaina", "cheppu", "naaku", "neeku", "enduku", "ekkada", "eppudu", "aithe", "chesa",
        "chesanu", "chesav", "chesaru", "chestha", "chesthanu", "chestunnanu", "chestunnav", "chestunnaru", "thinnanu", "thinnav"
    };

    // A compact local frequency prior. The language pack supplies coverage;
    // this list supplies a useful cold-start ordering without a cloud model.
    private static readonly string[] CommonWordsOrdered =
    {
        "the","of","and","to","in","a","is","that","for","it","as","was","with","be","by",
        "on","not","he","I","this","are","or","his","from","at","which","but","have","an","had",
        "you","were","their","they","one","all","we","can","her","has","there","been","if","more",
        "when","will","would","who","so","what","up","about","out","do","like","just","time","into",
        "than","could","our","my","your","me","good","new","some","very","them","these","then","now",
        "people","also","only","other","how","want","because","well","make","over","think","see","know",
        "take","come","go","look","use","find","give","need","tell","work","should","try","ask","feel",
        "really","right","back","much","where","help","before","great","through","life","first","last",
        "long","little","same","another","while","day","still","here","something","anything","everything",
        "nothing","please","thanks","thank","sorry","hello","today","tomorrow","later","home","school",
        "college","student","learn","learning","computer","phone","message","friend","family","project",
        "meeting","programming","different","important","possible","problem","question","answer","sure","maybe",
        "always","never","already","again","together","around","after","before","under","without","between",
        "during","every","each","both","many","most","another","better","best","next","last","first",
        "suitable","suitability","suitably","computer","complete","completion","suggest","suggestion","suggestions",
        "because","different","definitely","receive","separate","something","someone","anything","everyone"
    };

    private static readonly Dictionary<string, int> CommonFrequency =
        BuildFrequencyMap(CommonWordsOrdered);

    // Cold-start contextual priors. These are deliberately small and local;
    // learned ContextNext entries take precedence as the user types.
    private static readonly Dictionary<string, Dictionary<string, int>> SeedContexts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["i"] = Map("am:120", "want:90", "will:70", "can:65", "have:60", "need:55", "like:50", "think:45", "was:35"),
            ["i\u001fwant"] = Map("to:150"),
            ["i\u001fam"] = Map("going:120", "a:65", "not:55", "really:45", "looking:40"),
            ["i\u001fneed"] = Map("to:140", "a:70", "some:50", "help:45"),
            ["i\u001fwould"] = Map("like:130", "love:90", "be:70"),
            ["i\u001fwant\u001fto"] = Map("go:110", "learn:105", "know:80", "make:70", "get:65", "see:55"),
            ["i\u001fam\u001fgoing"] = Map("to:150"),
            ["going\u001fto"] = Map("the:100", "college:80", "school:70", "work:65", "go:45", "be:40"),
            ["want\u001fto"] = Map("learn:100", "go:95", "know:70", "make:60", "get:60", "see:50"),
            ["need\u001fto"] = Map("go:85", "get:85", "find:75", "know:60", "make:55"),
            ["looking\u001ffor"] = Map("a:120", "the:90", "some:65", "my:50"),
            ["how\u001fare"] = Map("you:160", "things:60"),
            ["thank\u001fyou"] = Map("for:150", "so:80"),
            ["see\u001fyou"] = Map("soon:100", "tomorrow:85", "there:60", "again:55"),
            ["can\u001fyou"] = Map("help:125", "please:105", "tell:90", "give:80", "send:70"),
            ["could\u001fyou"] = Map("please:130", "help:115", "tell:90", "send:75"),
            ["please\u001fhelp"] = Map("me:140", "with:95"),
            ["what\u001fis"] = Map("the:120", "this:80", "that:70"),
            ["where\u001fis"] = Map("the:120", "it:85", "my:65"),
            ["there\u001fis"] = Map("a:125", "no:70", "nothing:45"),
            ["it\u001fis"] = Map("a:105", "not:80", "the:70", "really:55"),
            ["i\u001flike"] = Map("to:115", "the:80", "this:70", "it:60"),
            ["i\u001fthink"] = Map("that:120", "it:75", "the:60", "we:55"),
            ["i\u001fknow"] = Map("that:110", "the:70", "it:65", "you:55"),
            ["nenu"] = Map("bagunna:120", "chesthunnanu:90", "velthunnanu:80"),
            ["nuvvu"] = Map("ela:120", "em:85", "ekkada:65", "unnav:60"),
            ["ela\u001funnav"] = Map("ippudu:60", "ivala:50")
        };

    private static readonly Dictionary<string, int> SeedPairs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["em|chesthunnav"] = 80,
            ["em|enti"] = 64,
            ["chesthunnav|thinnava"] = 72,
            ["nuvvu|ela"] = 56,
            ["ela|unnav"] = 68,
            ["bagunnava|nuvvu"] = 52,
            ["nenu|bagunna"] = 50
        };

    private static readonly HashSet<string> CommonVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "go", "get", "give", "make", "take", "learn", "know", "see", "come", "tell",
        "help", "find", "use", "try", "ask", "send", "call", "check", "start", "finish",
        "do", "be", "have", "want", "need", "like", "love", "look", "work", "study"
    };

    private static readonly HashSet<string> CommonNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "college", "school", "work", "home", "house", "store", "market", "computer",
        "phone", "message", "project", "meeting", "problem", "question", "answer", "time",
        "day", "place", "person", "student", "friend", "family", "world", "thing", "things"
    };

    private static readonly HashSet<string> CommonAdjectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "good", "great", "bad", "new", "old", "different", "important", "possible", "sure",
        "ready", "happy", "sorry", "better", "best", "right", "wrong", "easy", "hard"
    };

    public Dictionary<string, int> Words { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> Pairs { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, int>> ContextNext { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> DefaultSet =
        new(Defaults, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> RomanTeluguSet =
        new(RomanTelugu, StringComparer.OrdinalIgnoreCase);

    private List<string> _packVocabulary = new();
    private List<string>? _vocabularyCache;
    private HashSet<string>? _vocabularySet;
    private Dictionary<string, List<string>>? _prefixBuckets;
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
        string nextName = pack?.Name ?? "English";

        _languageName = nextName;
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
    }

    private void EnsureVocabularyIndex()
    {
        if (_vocabularyCache != null &&
            _vocabularySet != null &&
            _prefixBuckets != null)
            return;

        // The language pack is the main vocabulary, but it must not be the
        // only source used by the completion index.  CommonWordsOrdered and
        // the built-in defaults are deliberately indexed too; otherwise a
        // valid word can exist in the local frequency model yet be invisible
        // to prefix completion.
        IEnumerable<string> extraWords =
            _languageName.Contains("Telugu", StringComparison.OrdinalIgnoreCase) ||
            _languageName.Contains("Tenglish", StringComparison.OrdinalIgnoreCase)
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

        _vocabularySet = new HashSet<string>(
            _vocabularyCache,
            StringComparer.OrdinalIgnoreCase);

        _prefixBuckets = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (string word in _vocabularyCache)
            AddWordToPrefixIndex(word);
    }

    private void AddWordToPrefixIndex(string word)
    {
        if (_prefixBuckets == null || string.IsNullOrWhiteSpace(word))
            return;

        string normalized = word.ToLowerInvariant();
        int maxPrefix = Math.Min(3, normalized.Length);

        for (int length = 1; length <= maxPrefix; length++)
        {
            string prefix = normalized.Substring(0, length);
            if (!_prefixBuckets.TryGetValue(prefix, out List<string>? bucket))
            {
                bucket = new List<string>();
                _prefixBuckets[prefix] = bucket;
            }

            bucket.Add(word);
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

    private IReadOnlyList<string> GetCandidatesForPrefix(string prefix)
    {
        EnsureVocabularyIndex();

        if (string.IsNullOrWhiteSpace(prefix))
            return _vocabularyCache!;

        string normalized = prefix.Trim().ToLowerInvariant();
        string bucketKey = normalized.Substring(0, Math.Min(3, normalized.Length));

        if (_prefixBuckets!.TryGetValue(bucketKey, out List<string>? bucket))
            return bucket;

        // Rare fallback: protects against a stale/malformed index and keeps
        // completion functional even when the prefix bucket was not built.
        return _vocabularyCache!
            .Where(word => word.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
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

        string normalizedWord = word.Trim().ToLowerInvariant();
        Words[normalizedWord] = Words.GetValueOrDefault(normalizedWord) + 1;

        List<string> history = recentContext
            .Where(IsUsableWord)
            .Select(x => x.Trim().ToLowerInvariant())
            .TakeLast(4)
            .ToList();

        if (history.Count > 0)
        {
            string previous = history[^1];
            string pair = previous + "|" + normalizedWord;
            Pairs[pair] = Pairs.GetValueOrDefault(pair) + 1;
        }

        for (int length = 1; length <= Math.Min(4, history.Count); length++)
        {
            string contextKey = BuildContextKey(history.TakeLast(length));
            if (!ContextNext.TryGetValue(
                    contextKey,
                    out Dictionary<string, int>? nextWords))
            {
                nextWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ContextNext[contextKey] = nextWords;
            }

            nextWords[normalizedWord] =
                nextWords.GetValueOrDefault(normalizedWord) + 1;
        }

        // Do not rebuild the 124k-word index on every learned word.
        if (_vocabularyCache != null &&
            _vocabularySet != null &&
            _prefixBuckets != null &&
            _vocabularySet.Add(normalizedWord))
        {
            _vocabularyCache.Add(word.Trim());
            AddWordToPrefixIndex(word.Trim());
        }

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

        return _autocorrect.TryGetValue(
            typed.Trim(),
            out string? correction)
                ? correction
                : null;
    }

    private static Dictionary<string, int> BuildFrequencyMap(IEnumerable<string> words)
    {
        List<string> list = words
            .Where(IsUsableWord)
            .Select(x => x.ToLowerInvariant())
            .ToList();

        Dictionary<string, int> map =
            new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < list.Count; i++)
        {
            int score = Math.Max(18, 240 - (i * 2));
            map[list[i]] = Math.Max(
                map.GetValueOrDefault(list[i]),
                score);
        }

        // Explicitly keep several high-value completion words strong even
        // though they occur late in the compact cold-start list.
        string[] importantWords =
        {
            "suitable", "suggestion", "suggestions", "because", "different",
            "computer", "college", "school", "helpful", "something", "someone"
        };

        foreach (string word in importantWords)
            map[word] = Math.Max(map.GetValueOrDefault(word), 120);

        return map;
    }

    private int GlobalPrior(string word)
    {
        int score = CommonFrequency.GetValueOrDefault(word);

        if (score == 0)
        {
            score = word.Length switch
            {
                <= 2 => 28,
                3 => 24,
                4 => 20,
                5 => 16,
                6 => 12,
                _ => 8
            };
        }

        if (DefaultSet.Contains(word))
            score += 30;

        if (RomanTeluguSet.Contains(word))
            score += 30;

        score += Math.Min(Words.GetValueOrDefault(word) * 10, 240);
        return score;
    }

    private int HeuristicContextScore(IReadOnlyList<string> history, string word)
    {
        if (history.Count == 0)
            return 0;

        int score = 0;
        string last = history[^1];
        string previous = history.Count >= 2 ? history[^2] : string.Empty;

        if (last.Equals("to", StringComparison.OrdinalIgnoreCase))
        {
            if (previous.Equals("going", StringComparison.OrdinalIgnoreCase) ||
                previous.Equals("go", StringComparison.OrdinalIgnoreCase))
            {
                if (CommonNouns.Contains(word))
                    score += 70;
            }
            else if (CommonVerbs.Contains(word))
            {
                score += 55;
            }
        }

        if (last.Equals("the", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("my", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("your", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("our", StringComparison.OrdinalIgnoreCase))
        {
            if (CommonNouns.Contains(word))
                score += 48;
        }

        if (last.Equals("is", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("are", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("was", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("were", StringComparison.OrdinalIgnoreCase))
        {
            if (CommonAdjectives.Contains(word))
                score += 52;
        }

        if (last.Equals("can", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("will", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("should", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("could", StringComparison.OrdinalIgnoreCase))
        {
            if (CommonVerbs.Contains(word))
                score += 58;
        }

        if (last.Equals("a", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("an", StringComparison.OrdinalIgnoreCase))
        {
            if (CommonNouns.Contains(word) || CommonAdjectives.Contains(word))
                score += 35;
        }

        return score;
    }

    private int ContextScore(IReadOnlyList<string> history, string word)
    {
        int score = 0;

        for (int length = Math.Min(4, history.Count); length >= 1; length--)
        {
            string contextKey = BuildContextKey(history.TakeLast(length));

            if (SeedContexts.TryGetValue(
                    contextKey,
                    out Dictionary<string, int>? seedNext))
            {
                score += seedNext.GetValueOrDefault(word);
            }

            if (!ContextNext.TryGetValue(
                    contextKey,
                    out Dictionary<string, int>? nextWords))
                continue;

            int count = nextWords.GetValueOrDefault(word);
            if (count <= 0)
                continue;

            int weight = length switch
            {
                4 => 110,
                3 => 90,
                2 => 65,
                _ => 38
            };

            score += Math.Min(count * weight, 600);
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

        string previous = history.Count > 0 ? history[^1] : string.Empty;
        score += Math.Min(
            Pairs.GetValueOrDefault(previous + "|" + word) * 28,
            280);
        score += SeedPairs.GetValueOrDefault(previous + "|" + word);

        if (completion && !string.IsNullOrWhiteSpace(prefix))
        {
            int prefixLength = prefix.Length;
            score += Math.Min(prefixLength * 22, 180);

            int remaining = Math.Max(0, word.Length - prefixLength);
            score -= Math.Min(remaining * 3, 42);

            if (word.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                score -= 2000;
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

        // The completed word must be the last context item after Space.
        // Avoid duplicate insertion when Native already included it.
        if (!string.IsNullOrWhiteSpace(normalizedPrevious) &&
            (history.Count == 0 ||
             !history[^1].Equals(
                 normalizedPrevious,
                 StringComparison.OrdinalIgnoreCase)))
        {
            history.Add(normalizedPrevious);
        }

        if (history.Count > 4)
            history = history.TakeLast(4).ToList();

        string normalizedPrefix = prefix.Trim();

        if (normalizedPrefix.Length == 0)
            return NextWordCandidates(history);

        IReadOnlyList<string> vocabulary =
            GetCandidatesForPrefix(normalizedPrefix);

        // Prefix completion is intentionally generated from the cached
        // vocabulary index. Because the index contains the language pack,
        // common cold-start vocabulary, Roman-Telugu vocabulary where
        // applicable, and learned words, short prefixes such as "sui" are
        // not limited to whatever happened to be present in one pack file.
        List<SuggestionCandidate> completions = vocabulary
            .Where(word =>
                word.StartsWith(
                    normalizedPrefix,
                    StringComparison.OrdinalIgnoreCase) &&
                !word.Equals(
                    normalizedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            .Select(word => new SuggestionCandidate(
                word,
                ScoreWord(history, word, normalizedPrefix, true),
                0,
                SuggestionKind.Completion))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Word.Length)
            .ThenBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        // A partially typed word can legitimately have only one or two exact
        // completions. In that case, use strong sentence-context candidates
        // only as a controlled fallback. This is deliberately conservative:
        // contextual candidates are admitted only when they have real local
        // context evidence, and never replace a valid exact completion.
        if (completions.Count < 3 && history.Count > 0)
        {
            HashSet<string> existing = new(
                completions.Select(x => x.Word),
                StringComparer.OrdinalIgnoreCase);

            foreach (SuggestionCandidate candidate in
                NextWordCandidates(history)
                    .Where(x => x.Score >= 75)
                    .OrderByDescending(x => x.Score))
            {
                if (existing.Contains(candidate.Word) ||
                    candidate.Word.Equals(
                        normalizedPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                completions.Add(
                    new SuggestionCandidate(
                        candidate.Word,
                        candidate.Score - 25,
                        candidate.EditDistance,
                        SuggestionKind.Prediction));

                existing.Add(candidate.Word);
                if (completions.Count >= 3)
                    break;
            }
        }

        return completions
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Word.Length)
            .ThenBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private List<SuggestionCandidate> NextWordCandidates(
        IReadOnlyList<string> history)
    {
        HashSet<string> pool = new(StringComparer.OrdinalIgnoreCase);

        for (int length = Math.Min(4, history.Count); length >= 1; length--)
        {
            string key = BuildContextKey(history.TakeLast(length));

            if (SeedContexts.TryGetValue(
                    key,
                    out Dictionary<string, int>? seedNext))
            {
                foreach (string word in seedNext.Keys)
                    pool.Add(word);
            }

            if (ContextNext.TryGetValue(
                    key,
                    out Dictionary<string, int>? learnedNext))
            {
                foreach (string word in learnedNext.Keys)
                    pool.Add(word);
            }
        }

        // Always retain a strong cold-start pool. Only a few hundred words are
        // considered here, so next-word prediction stays cheap even with a
        // 124k-word language pack. Personal words are added separately.
        foreach (string word in CommonWordsOrdered.Take(320))
            pool.Add(word);

        foreach (string word in Words.Keys)
            pool.Add(word);

        // Learned/contextual candidates may be absent on a fresh profile.
        // The cold-start pool above guarantees a meaningful local fallback.

        return pool
            .Where(IsUsableWord)
            .Select(word => new SuggestionCandidate(
                word,
                ScoreWord(history, word, "", false),
                0,
                SuggestionKind.Prediction))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Word.Length)
            .ThenBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
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
                typed.Length > 0
                    ? typed[..Math.Min(2, typed.Length)]
                    : "");

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
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(IsUsableWord)
            .Select(x => x.ToLowerInvariant())
            .TakeLast(4)
            .ToList();
    }

    private static string BuildContextKey(IEnumerable<string> words) =>
        string.Join(
            '\u001F',
            words.Select(x => x.ToLowerInvariant()));

    private static Dictionary<string, int> Map(params string[] entries)
    {
        Dictionary<string, int> map =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (string entry in entries)
        {
            string[] parts = entry.Split(':', 2);
            if (parts.Length != 2 ||
                !int.TryParse(parts[1], out int score))
                continue;

            map[parts[0]] = score;
        }

        return map;
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
