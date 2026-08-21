namespace TypeSenseOverlay;

internal enum SuggestionKind
{
    Typed,
    Correction,
    Completion,
    Prediction,
    Personal,
    Emoji
}

internal sealed class SuggestionCandidate
{
    public string Word { get; }
    public int Score { get; }
    public int EditDistance { get; }
    public SuggestionKind Kind { get; }

    public SuggestionCandidate(
        string word,
        int score,
        int editDistance,
        SuggestionKind kind)
    {
        Word = word;
        Score = score;
        EditDistance = editDistance;
        Kind = kind;
    }
}