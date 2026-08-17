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
    "your", "in", "of",

    // Common everyday words
    "hello", "help", "helpful", "good", "great", "going", "go", "come", "coming",
    "what", "when", "where", "why", "how", "who", "which",
    "can", "could", "will", "would", "should", "shall",
    "do", "does", "did", "done", "doing",
    "am", "are", "was", "were", "be", "been", "being",
    "he", "she", "they", "them", "his", "her", "their",
    "me", "us", "our", "your",
    "not", "no", "yes", "okay", "ok",
    "please", "thanks", "thank", "sorry",
    "today", "tomorrow", "yesterday", "now", "later",
    "here", "there", "home", "work", "time",
    "want", "need", "like", "love", "know", "think",
    "see", "look", "tell", "say", "said",
    "make", "made", "take", "give", "get", "got",
    "come", "came", "go", "went",
    "one", "two", "three", "first", "last",
    "new", "old", "more", "most", "some", "very",
    "really", "only", "also", "just", "even",
    "because", "before", "after", "about", "from",
    "into", "with", "without", "for",
    "computer", "phone", "message", "friend", "family"
};

    private static readonly string[] RomanTelugu = new string[40]
	{
		"em", "enti", "ela", "unnav", "chesthunnav", "thinnava", "bagunnava", "bagunna", "nenu", "nuvvu",
		"meeru", "avunu", "kadu", "kaadu", "ledu", "sare", "inka", "ippudu", "vellava", "vastava",
		"vachava", "emaina", "cheppu", "naaku", "neeku", "enduku", "ekkada", "eppudu", "aithe", "chesa",
		"chesanu", "chesav", "chesaru", "chestha", "chesthanu", "chestunnanu", "chestunnav", "chestunnaru", "thinnanu", "thinnav"
	};

	private static readonly Dictionary<string, int> SeedPairs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
	{
		["em|chesthunnav"] = 80,
		["em|enti"] = 64,
		["chesthunnav|thinnava"] = 72,
		["nuvvu|ela"] = 56,
		["ela|unnav"] = 68,
		["bagunnava|nuvvu"] = 52,
		["nenu|bagunna"] = 50
	};

	public Dictionary<string, int> Words { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public Dictionary<string, int> Pairs { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> DefaultSet =
        new HashSet<string>(Defaults, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> RomanTeluguSet =
        new HashSet<string>(RomanTelugu, StringComparer.OrdinalIgnoreCase);

    private List<string>? _vocabularyCache;
    private Dictionary<string, List<string>>? _prefixBuckets;
    private readonly Dictionary<string, List<SuggestionCandidate>> _nextWordCache =
        new Dictionary<string, List<SuggestionCandidate>>(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;
	private static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Deckboard", "profile.json");

    private IReadOnlyList<string> Vocabulary
    {
        get
        {
            EnsureVocabularyIndex();
            return _vocabularyCache!;
        }
    }

    internal bool IsDirty => _dirty;

	public static LanguageProfile Load()
	{
		try
		{
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

    private void InvalidateVocabularyIndex()
    {
        _vocabularyCache = null;
        _prefixBuckets = null;
        _nextWordCache.Clear();
    }

    private void EnsureVocabularyIndex()
    {
        if (_vocabularyCache != null && _prefixBuckets != null)
            return;

        _vocabularyCache =
            Words.Keys
                .Concat(Defaults)
                .Concat(RomanTelugu)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        _prefixBuckets =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string word in _vocabularyCache)
        {
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
    }

    private IReadOnlyList<string> GetCandidatesForPrefix(string prefix)
    {
        EnsureVocabularyIndex();

        if (string.IsNullOrWhiteSpace(prefix))
            return _vocabularyCache!;

        string normalized = prefix.Trim().ToLowerInvariant();
        string bucketKey = normalized.Substring(0, Math.Min(3, normalized.Length));

        return _prefixBuckets!.TryGetValue(bucketKey, out List<string>? bucket)
            ? bucket
            : Array.Empty<string>();
    }

	public void Learn(string word, string previous)
	{
		word = word.ToLowerInvariant();
		if (word.Length < 2)
		{
			return;
		}
        Words[word] = Words.GetValueOrDefault(word) + 1;
        if (!string.IsNullOrEmpty(previous))
        {
            string pair = previous + "|" + word;
            Pairs[pair] = Pairs.GetValueOrDefault(pair) + 1;
        }

        InvalidateVocabularyIndex();
        _dirty = true;
	}

	public bool IsKnownWord(string word)
	{
		if (string.IsNullOrWhiteSpace(word))
		{
			return false;
		}

		word = word.Trim();

        return Words.ContainsKey(word)
            || DefaultSet.Contains(word)
            || RomanTeluguSet.Contains(word);
	}

    public int Score(string previous, string word)
    {
        int personalFrequency = Words.GetValueOrDefault(word);
        int contextFrequency = Pairs.GetValueOrDefault(previous + "|" + word);
        int seedContext = SeedPairs.GetValueOrDefault(previous + "|" + word);

        int score = personalFrequency * 3
            + contextFrequency * 14
            + seedContext;

        // Keep the built-in vocabulary useful before the user has trained
        // the profile, while still allowing learned words to dominate.
        if (RomanTeluguSet.Contains(word))
            score += 18;

        if (DefaultSet.Contains(word))
            score += 2;

        return score;
    }

    // Candidate ranking combines:
    // 1. learned word frequency,
    // 2. previous-word context,
    // 3. built-in language priors,
    // 4. prefix relevance for completions,
    // 5. a small length penalty so shorter natural completions win ties.
    public List<SuggestionCandidate> CandidateModels(string previous, string prefix)
    {
        IReadOnlyList<string> vocabulary = GetCandidatesForPrefix(prefix);

        if (string.IsNullOrWhiteSpace(prefix))
        {
            string cacheKey = previous ?? string.Empty;
            if (_nextWordCache.TryGetValue(cacheKey, out List<SuggestionCandidate>? cached))
                return cached.ToList();

            List<SuggestionCandidate> ranked = vocabulary
                .Select(word => new SuggestionCandidate(
                    word,
                    Score(previous, word),
                    0,
                    SuggestionKind.Prediction))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Word)
                .Take(3)
                .ToList();

            _nextWordCache[cacheKey] = ranked;
            return ranked.ToList();
        }

        string normalizedPrefix = prefix.Trim();

        return vocabulary
            .Where(word =>
                word.StartsWith(
                    normalizedPrefix,
                    StringComparison.OrdinalIgnoreCase)
                && !word.Equals(
                    normalizedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            .Select(word =>
            {
                int baseScore = Score(previous, word);
                int remainingLength = Math.Max(
                    0,
                    word.Length - normalizedPrefix.Length);
                int prefixBonus = normalizedPrefix.Length * 8;
                int lengthPenalty = Math.Min(remainingLength * 2, 20);
                int rankedScore = baseScore + prefixBonus - lengthPenalty;

                return new SuggestionCandidate(
                    word,
                    rankedScore,
                    0,
                    SuggestionKind.Completion);
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Word.Length)
            .ThenBy(candidate => candidate.Word)
            .Take(3)
            .ToList();
    }

    public List<string> Candidates(string previous, string prefix)
    {
        return CandidateModels(previous, prefix)
            .Select(candidate => candidate.Word)
            .ToList();
    }

       

    public string? BestCorrection(string previous, string typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return null;

        typed = typed.Trim();

        int limit = typed.Length <= 4 ? 1 : 2;

        var candidates = from word in Vocabulary
                         where !word.Equals(typed, StringComparison.OrdinalIgnoreCase)
                            && Math.Abs(word.Length - typed.Length) <= limit
                         select new
                         {
                             Word = word,
                             Distance = DamerauLevenshtein(typed, word)
                         };

        return (from x in candidates
                where x.Distance <= limit
                orderby x.Distance,
                        Score(previous, x.Word) descending,
                        x.Word
                select x.Word)
            .FirstOrDefault();
    }

    public void Save()
    {
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(Path, JsonSerializer.Serialize(this));
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
		{
			d[i, 0] = i;
		}
		for (int j = 0; j <= b.Length; j++)
		{
			d[0, j] = j;
		}
		for (int k = 1; k <= a.Length; k++)
		{
			for (int l = 1; l <= b.Length; l++)
			{
				int cost = ((a[k - 1] != b[l - 1]) ? 1 : 0);
				d[k, l] = Math.Min(Math.Min(d[k - 1, l] + 1, d[k, l - 1] + 1), d[k - 1, l - 1] + cost);
				if (k > 1 && l > 1 && a[k - 1] == b[l - 2] && a[k - 2] == b[l - 1])
				{
					d[k, l] = Math.Min(d[k, l], d[k - 2, l - 2] + cost);
				}
			}
		}
		return d[a.Length, b.Length];
	}
}
