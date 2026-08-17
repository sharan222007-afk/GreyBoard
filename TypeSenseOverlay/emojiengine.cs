using System;
using System.Collections.Generic;
using System.Linq;
using TypeSenseOverlay;

internal static class EmojiMap
{
    public static readonly Dictionary<string, string> Data = new(StringComparer.OrdinalIgnoreCase)
    {
        ["smile"] = "😊",
        ["laugh"] = "😂",
        ["sad"] = "😢",
        ["fire"] = "🔥",
        ["heart"] = "❤️",
        ["check"] = "✅",
        ["rocket"] = "🚀",
        ["thumbsup"] = "👍"
    };

    public static List<SuggestionCandidate> GetMatches(string search)
    {
        return Data.Where(kvp => kvp.Key.StartsWith(search, StringComparison.OrdinalIgnoreCase))
                   .Select(kvp => new SuggestionCandidate(kvp.Value, 100, 0, SuggestionKind.Prediction))
                   .Take(3).ToList();
    }
}