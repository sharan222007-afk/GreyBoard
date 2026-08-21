using System;
using System.Collections.Generic;
using System.Linq;

namespace TypeSenseOverlay;

internal static class EmojiMap
{
    // Local, dependency-free emoji catalog. The picker shows this entire
    // catalog; the prediction engine uses only a small context-ranked subset.
    private static readonly (string Key, string Emoji)[] Catalog =
    {
        ("smile", "😊"), ("laugh", "😂"), ("joy", "🤣"), ("love", "😍"), ("love2", "🥰"),
        ("heart", "❤️"), ("kiss", "😘"), ("wink", "😉"), ("cool", "😎"),
        ("thinking", "🤔"), ("confused", "😕"), ("sad", "😢"), ("cry", "😭"),
        ("angry", "😡"), ("shocked", "😮"), ("surprised", "😲"), ("scared", "😨"),
        ("tired", "😴"), ("sleep", "😴"), ("blush", "😊"), ("party", "🥳"),
        ("celebrate", "🎉"), ("clap", "👏"), ("pray", "🙏"), ("thanks", "🙏"),
        ("please", "🙏"), ("sorry", "🙏"), ("wave", "👋"), ("ok", "👌"),
        ("thumbsup", "👍"), ("like", "👍"), ("thumbsdown", "👎"), ("lovehands", "🫶"),
        ("muscle", "💪"), ("highfive", "🙌"), ("shrug", "🤷"), ("facepalm", "🤦"),
        ("fire", "🔥"), ("rocket", "🚀"), ("star", "⭐"), ("sparkles", "✨"),
        ("sun", "☀️"), ("moon", "🌙"), ("rainbow", "🌈"), ("snow", "❄️"),
        ("lightning", "⚡"), ("earth", "🌍"), ("flower", "🌸"), ("rose", "🌹"),
        ("tree", "🌳"), ("leaf", "🍃"), ("coffee", "☕"), ("tea", "🍵"),
        ("pizza", "🍕"), ("burger", "🍔"), ("fries", "🍟"), ("cake", "🍰"),
        ("birthday", "🎂"), ("food", "🍽️"), ("tasty", "😋"), ("apple", "🍎"), ("banana", "🍌"),
        ("icecream", "🍦"), ("water", "💧"), ("beer", "🍺"), ("wine", "🍷"),
        ("football", "⚽"), ("cricket", "🏏"), ("basketball", "🏀"), ("tennis", "🎾"),
        ("medal", "🏅"), ("trophy", "🏆"), ("game", "🎮"), ("music", "🎵"),
        ("movie", "🎬"), ("camera", "📷"), ("phone", "📱"), ("computer", "💻"),
        ("bulb", "💡"), ("idea", "💡"), ("book", "📚"), ("study", "📖"),
        ("school", "🏫"), ("college", "🎓"), ("work", "💼"), ("home", "🏠"),
        ("car", "🚗"), ("bus", "🚌"), ("train", "🚆"), ("plane", "✈️"),
        ("travel", "✈️"), ("location", "📍"), ("map", "🗺️"), ("calendar", "📅"),
        ("clock", "⏰"), ("time", "⏰"), ("money", "💰"), ("gift", "🎁"),
        ("bell", "🔔"), ("mail", "✉️"), ("message", "💬"), ("chat", "💬"),
        ("check", "✅"), ("done", "✅"), ("yes", "✅"), ("cross", "❌"),
        ("no", "❌"), ("warning", "⚠️"), ("question", "❓"), ("info", "ℹ️"),
        ("plus", "➕"), ("minus", "➖"), ("redheart", "❤️"), ("blueheart", "💙"),
        ("greenheart", "💚"), ("yellowheart", "💛"), ("purpleheart", "💜"),
        ("brokenheart", "💔"), ("100", "💯"), ("okhand", "👌"), ("eyes", "👀"),
        ("look", "👀"), ("finger", "👉"), ("up", "☝️"), ("down", "👇"),
        ("left", "👈"), ("right", "👉"), ("point", "👉"), ("hand", "✋"),
        ("strong", "💪"), ("brain", "🧠"), ("health", "❤️‍🩹"), ("doctor", "🩺"),
        ("medicine", "💊"), ("hospital", "🏥"), ("dog", "🐶"), ("cat", "🐱"),
        ("loveanimal", "🐾"), ("sunflower", "🌻"), ("cherry", "🍒"), ("mango", "🥭"),
        ("fireworks", "🎆"), ("amazing", "🤩"), ("headphones", "🎧"), ("sos", "🆘"), ("yawn", "🥱"), ("confetti", "🎊"), ("balloon", "🎈"), ("musicnote", "🎶"),
        ("warning", "🚨"), ("lock", "🔒"), ("unlock", "🔓"), ("search", "🔎"),
        ("settings", "⚙️"), ("pin", "📌"), ("link", "🔗"), ("folder", "📁"),
        ("file", "📄"), ("email", "📧"), ("bellring", "🔔"), ("notification", "🔔")
    };

    private static readonly Dictionary<string, string> Data =
        Catalog
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Emoji, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> EmojiSet =
        new(Catalog.Select(x => x.Emoji), StringComparer.Ordinal);

    private static readonly string[] AllEmojiCache =
        Catalog.Select(x => x.Emoji).Distinct(StringComparer.Ordinal).ToArray();


    // Fitzpatrick skin-tone modifiers supported by Unicode emoji.
    private static readonly string[] SkinToneModifiers =
    {
        "", "🏻", "🏼", "🏽", "🏾", "🏿"
    };

    private static readonly HashSet<string> ToneableEmoji =
        new(StringComparer.Ordinal)
        {
            "👋", "👌", "👍", "👎", "👏", "🙏", "🙌", "🤝", "✋",
            "🤚", "🖐️", "🖐", "✌️", "✌", "🤞", "🤟", "🤘", "🤙",
            "👈", "👉", "👆", "👇", "☝️", "☝", "✍️", "✍",
            "💪", "🤳", "💅", "🤷", "🤦", "🙋", "🙆", "🙅",
            "🙇", "💁", "🙍", "🙎", "🧏", "🧑", "👩", "👨",
            "👦", "👧", "👶"
        };

    private static string _selectedSkinTone = "";

    private static readonly Dictionary<string, string[]> Context =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["love"] = new[] { "❤️", "😍", "🥰", "😘" },
            ["like"] = new[] { "👍", "❤️", "😊" },
            ["good"] = new[] { "👍", "😊", "✨" },
            ["great"] = new[] { "🔥", "🎉", "✨" },
            ["thanks"] = new[] { "🙏", "❤️", "😊" },
            ["thank"] = new[] { "🙏", "❤️", "😊" },
            ["sorry"] = new[] { "🙏", "😔", "❤️" },
            ["congrats"] = new[] { "🎉", "🥳", "🏆" },
            ["congratulations"] = new[] { "🎉", "🥳", "🏆" },
            ["happy"] = new[] { "😊", "🥳", "❤️" },
            ["birthday"] = new[] { "🎂", "🎉", "🥳" },
            ["party"] = new[] { "🥳", "🎉", "🎊" },
            ["study"] = new[] { "📚", "🎓", "💡" },
            ["college"] = new[] { "🎓", "📚", "🏫" },
            ["school"] = new[] { "📚", "🎓", "🏫" },
            ["work"] = new[] { "💼", "💻", "☕" },
            ["coffee"] = new[] { "☕", "😊" },
            ["food"] = new[] { "🍽️", "😋", "🍕" },
            ["pizza"] = new[] { "🍕", "😋" },
            ["sleep"] = new[] { "😴", "🌙" },
            ["tired"] = new[] { "😴", "🥱" },
            ["fire"] = new[] { "🔥", "💯" },
            ["awesome"] = new[] { "🔥", "🤩", "✨" },
            ["yes"] = new[] { "✅", "👍", "😊" },
            ["no"] = new[] { "❌", "😕" },
            ["question"] = new[] { "❓", "🤔" },
            ["help"] = new[] { "🙏", "🆘", "💡" },
            ["home"] = new[] { "🏠", "❤️" },
            ["travel"] = new[] { "✈️", "🌍", "🚗" },
            ["game"] = new[] { "🎮", "🏆", "🔥" },
            ["music"] = new[] { "🎵", "🎶", "🎧" },
            ["hello"] = new[] { "👋", "😊", "❤️" },
            ["hi"] = new[] { "👋", "😊" }
        };


    private static readonly string[] SkinTonesCache =
        SkinToneModifiers.ToArray();

    public static string SelectedSkinTone => _selectedSkinTone;

    public static void SetSkinTone(string modifier)
    {
        _selectedSkinTone = SkinToneModifiers.Contains(modifier, StringComparer.Ordinal)
            ? modifier
            : "";
    }

    public static string ApplySelectedSkinTone(string emoji)
    {
        if (string.IsNullOrEmpty(_selectedSkinTone) || string.IsNullOrEmpty(emoji))
            return emoji;

        // Do not double-apply a tone modifier.
        if (emoji.Contains("🏻", StringComparison.Ordinal) ||
            emoji.Contains("🏼", StringComparison.Ordinal) ||
            emoji.Contains("🏽", StringComparison.Ordinal) ||
            emoji.Contains("🏾", StringComparison.Ordinal) ||
            emoji.Contains("🏿", StringComparison.Ordinal))
            return emoji;

        if (!ToneableEmoji.Contains(emoji))
            return emoji;

        // Keep variation selectors intact by appending the modifier to the
        // base emoji sequence.
        return emoji + _selectedSkinTone;
    }

    public static IReadOnlyList<string> AllEmojis => AllEmojiCache;

    public static List<SuggestionCandidate> GetMatches(string search)
    {
        return Catalog
            .Where(x => x.Key.StartsWith(search, StringComparison.OrdinalIgnoreCase))
            .Select(x => new SuggestionCandidate(x.Emoji, 100, 0, SuggestionKind.Prediction))
            .GroupBy(x => x.Word)
            .Select(x => x.First())
            .Take(3)
            .ToList();
    }

    public static bool IsEmoji(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        EmojiSet.Contains(value);

    public static IEnumerable<string> GetContextual(IReadOnlyList<string> history)
    {
        if (history.Count == 0)
            return Array.Empty<string>();

        HashSet<string> result = new(StringComparer.Ordinal);

        foreach (string word in history.Reverse())
        {
            if (Context.TryGetValue(word, out string[]? emojis))
            {
                foreach (string emoji in emojis)
                    result.Add(emoji);
            }
        }

        return result;
    }
}
