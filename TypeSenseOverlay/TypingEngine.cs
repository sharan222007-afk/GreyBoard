using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace TypeSenseOverlay;

internal sealed class TypingEngine : IDisposable
{
    private readonly LanguageProfile _profile;

    private readonly UserSettings _settings;

    private readonly SuggestionOverlay _overlay;

    private readonly InputHookThread _inputThread;

    private readonly DispatcherTimer _caretTimer;
    private readonly DispatcherTimer _predictionRefreshTimer;
    private readonly DispatcherTimer _profileSaveTimer;

    private string _current = "";

    private string _previous = "";

    private List<SuggestionCandidate> _candidates = new List<SuggestionCandidate>();

    // Prediction shortcut keydown is consumed first; the replacement is
    // started only after that same key's keyup has also been consumed.
    private int _pendingShortcutKey = -1;
    private int _pendingPredictionIndex = -1;
    private string _pendingPredictionWord = "";
    private string _pendingPredictionTyped = "";
    private string _pendingPredictionActiveWord = "";
    private string _pendingPredictionPrevious = "";
    private string _pendingPredictionRecentContext = "";
    private int _pendingPredictionReplaceLength = 0;
    private bool _pendingShortcutKeyUpReceived;
    private string _predictionPrefix = "";
    private int _selectedPredictionIndex = -1;
    private string _selectionContextKey = "";
    private bool _controlDown;
    private bool _altDown;
    private bool _shiftDown;
    private bool _refreshAfterBoundary;
    private string _recentContext = "";

    private static readonly bool DiagnosticsEnabled =
        string.Equals(
            Environment.GetEnvironmentVariable("GREYBOARD_DIAGNOSTICS"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static readonly object DiagnosticLogLock = new object();

    private static void DiagnosticLog(string message)
    {
        if (!DiagnosticsEnabled)
            return;

        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Deckboard");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "GreyBoard_diagnostic.log");
            lock (DiagnosticLogLock)
            {
                File.AppendAllText(
                    path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public bool IsPaused { get; private set; }

    public bool IsRunning { get; private set; }

    public event Action? StateChanged;

    public TypingEngine(LanguageProfile profile, UserSettings settings, SuggestionOverlay overlay)
    {
        _profile = profile;
        _settings = settings;
        _overlay = overlay;

        _profile.SetLanguagePack(
            LanguagePackManager.Load(_settings.SelectedLanguagePack));

        _overlay.SetPredictionClickHandler(Accept);
        _overlay.SetEmojiInsertHandler(emoji => Native.InsertTextAtCaret(emoji));
        _caretTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180L)
        };
        _caretTimer.Tick += delegate
        {
            _overlay.FollowCaret();
        };

        _predictionRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30L)
        };
        _predictionRefreshTimer.Tick += delegate
        {
            _predictionRefreshTimer.Stop();
            if (!IsPaused)
                RefreshFromActiveCaret();
        };

        _profileSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _profileSaveTimer.Tick += delegate
        {
            if (_profile.IsDirty)
                _profile.Save();
        };

        _inputThread = new InputHookThread(
            _settings,
            _caretTimer.Dispatcher,
            HandleInputEvent);
    }

    public void Start()
    {
        if (IsRunning)
            return;

        _inputThread.Start();
        IsRunning = true;
        IsPaused = false;
        _inputThread.SetPaused(false);
        _inputThread.Configure(
            _settings.AdvancedKey1,
            _settings.AdvancedKey2,
            _settings.ShortcutMode);

        _overlay.ShowOverlay();
        _caretTimer.Start();
        _profileSaveTimer.Start();
        RefreshFromActiveCaret();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        _caretTimer.Stop();
        _predictionRefreshTimer.Stop();
        _profileSaveTimer.Stop();

        if (_profile.IsDirty)
            _profile.Save();

        _inputThread.Stop();

        _current = "";
        _controlDown = false;
        _altDown = false;
        _shiftDown = false;
        ClearPendingPrediction();
        _refreshAfterBoundary = false;
        _candidates = new List<SuggestionCandidate>();
        IsRunning = false;
        IsPaused = false;
        _overlay.HideOverlay();
        StateChanged?.Invoke();
    }

    public void TogglePause()
    {
        if (IsRunning)
        {
            IsPaused = !IsPaused;
            _inputThread.SetPaused(IsPaused);
            _current = "";
            _controlDown = false;
            _altDown = false;
            _shiftDown = false;
            _predictionRefreshTimer.Stop();
            _refreshAfterBoundary = false;
            _candidates = new List<SuggestionCandidate>();
            Refresh();
            StateChanged?.Invoke();
        }
    }

    public void ApplySettings()
    {
        _inputThread.Configure(
            _settings.AdvancedKey1,
            _settings.AdvancedKey2,
            _settings.ShortcutMode);

        _profile.SetLanguagePack(
            LanguagePackManager.Load(_settings.SelectedLanguagePack));

        _overlay.ApplyAppearance();
        if (IsRunning && _settings.Placement == "Fixed")
        {
            _overlay.ShowOverlay();
        }
        SchedulePredictionRefresh();
        StateChanged?.Invoke();
    }

    private void HandleInputEvent(InputHookEvent input)
    {
        if (!IsRunning)
            return;

        _controlDown = input.ControlDown;
        _altDown = input.AltDown;
        _shiftDown = input.ShiftDown;

        if (input.Kind == InputHookEventKind.TogglePause)
        {
            TogglePause();
            return;
        }

        if (input.Kind == InputHookEventKind.AdvancedCycle)
        {
            CycleAdvancedPrediction();
            return;
        }

        if (input.Kind == InputHookEventKind.AdvancedCommit)
        {
            CommitAdvancedPrediction();
            return;
        }

        if (input.Kind == InputHookEventKind.PredictionShortcutDown)
        {
            if (IsPaused)
                return;

            // Normal typing is intentionally debounced. A prediction shortcut
            // is an explicit request for the state at the caret NOW, so make
            // this path authoritative without adding work to every keypress.
            _predictionRefreshTimer.Stop();
            RefreshFromActiveCaret();

            int? choice = PredictionChoice(input.Key);
            if (choice.HasValue &&
                choice.Value >= 0 &&
                choice.Value < _candidates.Count)
            {
                SnapshotPredictionForAcceptance(
                    input.Key,
                    choice.Value);
            }

            return;
        }

        if (input.Kind == InputHookEventKind.PredictionShortcutUp)
        {
            if (_pendingPredictionIndex < 0)
                return;

            _pendingShortcutKeyUpReceived = true;

            if (!input.ControlDown &&
                !input.AltDown &&
                !input.ShiftDown)
            {
                CompletePendingPredictionAcceptance();
            }

            return;
        }

        if (input.Kind == InputHookEventKind.PredictionShortcutCommitReady)
        {
            if (_pendingPredictionIndex >= 0)
                CompletePendingPredictionAcceptance();

            return;
        }

        if (input.Kind == InputHookEventKind.Boundary)
        {
            HandleBoundaryAfterInput(input.Key);
            return;
        }

        if (input.Kind == InputHookEventKind.Backspace)
        {
            SchedulePredictionRefresh();
            return;
        }

        if (input.Kind == InputHookEventKind.NormalKeyDown)
        {
            if (!IsPaused && !IsModifierKey(input.Key))
                SchedulePredictionRefresh();
        }
    }

    private void HandleBoundaryAfterInput(int key)
    {
        if (IsPaused)
            return;

        if (key == Native.VK_SPACE &&
            Native.TryGetActiveTextContext(
                out Native.ActiveTextContext context))
        {
            string typed = context.Word;
            _current = typed;
            _previous = context.PreviousWord;
            _recentContext = context.RecentContext;

            if (!string.IsNullOrWhiteSpace(typed) &&
                !_settings.IsProtectedWord(typed) &&
                !_profile.IsKnownWord(typed))
            {
                string? correction = FindAutocorrect();

                if (correction != null &&
                    !correction.Equals(
                        typed,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (Native.ReplaceActiveWord(correction))
                    {
                        _settings.RecordAutocorrection(
                            typed,
                            correction);

                        _current = correction;
                        Commit(correction);
                    }
                }
            }
        }

        Commit(_current);
        _refreshAfterBoundary = true;
        SchedulePredictionRefresh();
    }

    private bool IsModifierKey(int key)
    {
        return key == 16 || key == 17 || key == 18 ||
               key == 160 || key == 161 || key == 162 || key == 163 ||
               key == 164 || key == 165;
    }

    private void SnapshotPredictionForAcceptance(int key, int index)
    {
        if (index < 0 || index >= _candidates.Count)
            return;

        _pendingShortcutKey = key;
        _pendingPredictionIndex = index;
        _pendingPredictionWord = _candidates[index].Word;
        _pendingPredictionTyped = string.IsNullOrWhiteSpace(_predictionPrefix)
            ? _current
            : _predictionPrefix;
        _pendingPredictionActiveWord = _current;
        _pendingPredictionPrevious = _previous;
        _pendingPredictionRecentContext = _recentContext;
        _pendingPredictionReplaceLength = _current.Length;
        _pendingShortcutKeyUpReceived = false;

        DiagnosticLog(
            $"SHORTCUT_SNAPSHOT word=\"{_pendingPredictionWord}\" " +
            $"typedPrefix=\"{_pendingPredictionTyped}\" " +
            $"activeWord=\"{_pendingPredictionActiveWord}\" " +
            $"replaceLength={_pendingPredictionReplaceLength} " +
            $"previous=\"{_pendingPredictionPrevious}\"");
    }

    private void SchedulePredictionRefresh()
    {
        if (!IsRunning || IsPaused)
            return;

        _predictionRefreshTimer.Stop();
        _predictionRefreshTimer.Start();
    }

    private void CompletePendingPredictionAcceptance()
    {
        if (_pendingPredictionIndex < 0 ||
            string.IsNullOrWhiteSpace(_pendingPredictionWord))
            return;

        string word = _pendingPredictionWord;
        string typedPrefix = _pendingPredictionTyped;
        string activeWord = _pendingPredictionActiveWord;
        string previous = _pendingPredictionPrevious;
        string recentContext = _pendingPredictionRecentContext;

        ClearPendingPrediction();

        _caretTimer.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                AcceptPendingPredictionOnUiThread(
                    word,
                    typedPrefix,
                    activeWord,
                    previous,
                    recentContext,
                    0);
            }));
    }

    private void AcceptPendingPredictionOnUiThread(
        string word,
        string typedPrefix,
        string expectedActiveWord,
        string previous,
        string pendingRecentContext,
        int attempt)
    {
        if (IsPaused || string.IsNullOrWhiteSpace(word))
            return;

        // Do not require the accepted prediction to start with the typed
        // prefix. A valid prediction can replace characters inside the active
        // word, e.g. "goo" -> "going" or "goob" -> "good".
        //
        // Stale-candidate protection is handled by the authoritative active
        // word check below (context.Word == expectedActiveWord). Requiring
        // StartsWith here incorrectly rejects legitimate predictions whose
        // spelling diverges after the typed prefix.
        if (!string.IsNullOrWhiteSpace(typedPrefix) &&
            !string.IsNullOrWhiteSpace(expectedActiveWord) &&
            !typedPrefix.StartsWith(
                expectedActiveWord,
                StringComparison.OrdinalIgnoreCase) &&
            !expectedActiveWord.StartsWith(
                typedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            DiagnosticLog(
                $"ACCEPT_PREFIX_MISMATCH allowed activeWord=\"{expectedActiveWord}\" " +
                $"typedPrefix=\"{typedPrefix}\" prediction=\"{word}\"");
        }

        if (!Native.TryGetActiveTextContext(
                out Native.ActiveTextContext context))
        {
            if (attempt < 2)
            {
                _caretTimer.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() =>
                    {
                        AcceptPendingPredictionOnUiThread(
                            word,
                            typedPrefix,
                            expectedActiveWord,
                            previous,
                            pendingRecentContext,
                            attempt + 1);
                    }));
                return;
            }

            if (string.IsNullOrWhiteSpace(expectedActiveWord))
            {
                bool inserted = Native.InsertTextAtCaret(word + " ");
                DiagnosticLog(
                    $"ACCEPT_NEXT_WORD_FALLBACK inserted={inserted} " +
                    $"word=\"{word}\"");

                if (inserted)
                {
                    _recentContext = pendingRecentContext;
                    FinalizeAcceptedPrediction(word, previous);
                }
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(context.Word))
        {
            bool inserted = Native.InsertTextAtCaret(word + " ");

            DiagnosticLog(
                $"ACCEPT_NEXT_WORD inserted={inserted} " +
                $"word=\"{word}\" previous=\"{context.PreviousWord}\"");

            if (!inserted)
                return;

            _recentContext = string.IsNullOrWhiteSpace(context.RecentContext)
                ? pendingRecentContext
                : context.RecentContext;

            FinalizeAcceptedPrediction(
                word,
                string.IsNullOrWhiteSpace(context.PreviousWord)
                    ? previous
                    : context.PreviousWord);
            return;
        }

        // If Space has not propagated through the target editor yet, allow a
        // couple of non-blocking UI retries. Genuine typing changes still fail
        // the active-word equality check below.
        if (string.IsNullOrWhiteSpace(expectedActiveWord) && attempt < 2)
        {
            _caretTimer.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    AcceptPendingPredictionOnUiThread(
                        word,
                        typedPrefix,
                        expectedActiveWord,
                        previous,
                        pendingRecentContext,
                        attempt + 1);
                }));
            return;
        }

        if (!context.Word.Equals(
                expectedActiveWord,
                StringComparison.Ordinal))
        {
            DiagnosticLog(
                $"ACCEPT_REJECT active-word-changed expected=\"{expectedActiveWord}\" " +
                $"actual=\"{context.Word}\"");
            return;
        }

        bool isEmoji = EmojiMap.IsEmoji(word);
        string output = !isEmoji && char.IsUpper(context.Word[0])
            ? char.ToUpperInvariant(word[0]) + word.Substring(1)
            : word;

        if (!Native.CapturePredictionTarget(context.Word))
        {
            DiagnosticLog(
                $"ACCEPT_TARGET_CAPTURE_FAIL word=\"{context.Word}\"");
            return;
        }

        bool accepted = Native.ReplaceCapturedPredictionWord(
            context.Word,
            output);

        DiagnosticLog(
            $"ACCEPT_RESULT accepted={accepted} " +
            $"activeWord=\"{context.Word}\" output=\"{output}\"");

        if (!accepted)
            return;

        _recentContext = string.IsNullOrWhiteSpace(context.RecentContext)
            ? pendingRecentContext
            : context.RecentContext;

        FinalizeAcceptedPrediction(
            output,
            string.IsNullOrWhiteSpace(context.PreviousWord)
                ? previous
                : context.PreviousWord);
    }

    private void FinalizeAcceptedPrediction(
        string word,
        string previous)
    {
        _profile.Learn(word, ParseRecentContext(_recentContext));
        _previous = word.ToLowerInvariant();
        _current = "";
        _predictionPrefix = "";
        _candidates = new List<SuggestionCandidate>();
        _selectedPredictionIndex = -1;
        _selectionContextKey = "";
        SchedulePredictionRefresh();
    }

    private void ClearPendingPrediction()
    {
        _pendingShortcutKey = -1;
        _pendingPredictionIndex = -1;
        _pendingPredictionWord = "";
        _pendingPredictionTyped = "";
        _pendingPredictionActiveWord = "";
        _pendingPredictionPrevious = "";
        _pendingPredictionRecentContext = "";
        _pendingPredictionReplaceLength = 0;
        _pendingShortcutKeyUpReceived = false;
    }


    private void UpdateModifierState(int key, bool isKeyDown, bool isKeyUp)
    {
        bool? state = isKeyDown ? true : isKeyUp ? false : null;
        if (!state.HasValue)
            return;

        switch (key)
        {
            case 17:
            case 162:
            case 163:
                _controlDown = state.Value;
                break;
            case 18:
            case 164:
            case 165:
                _altDown = state.Value;
                break;
            case 16:
            case 160:
            case 161:
                _shiftDown = state.Value;
                break;
        }
    }

    private void CycleAdvancedPrediction()
    {
        if (IsPaused)
            return;

        if (_candidates == null ||
            _candidates.Count == 0)
        {
            DiagnosticLog(
                "ADVANCED_CYCLE ignored - no candidates");

            return;
        }

        int count =
            Math.Min(5, _candidates.Count);

        if (count <= 0)
            return;

        if (_selectedPredictionIndex < 0 ||
            _selectedPredictionIndex >= count)
        {
            _selectedPredictionIndex = 0;
        }
        else
        {
            _selectedPredictionIndex =
                (_selectedPredictionIndex + 1) % count;
        }

        DiagnosticLog(
            "ADVANCED_CYCLE index=" +
            _selectedPredictionIndex +
            " word=\"" +
            _candidates[_selectedPredictionIndex].Word +
            "\"");

        RenderCandidates();
    }

    private void CommitAdvancedPrediction()
    {
        if (IsPaused)
            return;

        if (_selectedPredictionIndex < 0 ||
            _selectedPredictionIndex >= _candidates.Count)
        {
            DiagnosticLog(
                "ADVANCED_COMMIT ignored - no selected prediction");
            return;
        }

        int index = _selectedPredictionIndex;
        if (index < 0 || index >= Math.Min(5, _candidates.Count))
        {
            _selectedPredictionIndex = -1;
            return;
        }

        string word = EmojiMap.IsEmoji(_candidates[index].Word)
            ? EmojiMap.ApplySelectedSkinTone(_candidates[index].Word)
            : _candidates[index].Word;

        DiagnosticLog(
            "ADVANCED_COMMIT index=" +
            index +
            " word=\"" +
            word +
            "\"");

        // Never perform UI Automation replacement directly inside the low-level
        // keyboard hook. Queue the same acceptance path used by Classic mode.
        SnapshotPredictionForAcceptance(0, index);
        CompletePendingPredictionAcceptance();
    }

    private int? PredictionChoice(int key)
    {
        if (Shortcut.Matches(_settings.AcceptFirst, key, _controlDown, _altDown, _shiftDown))
        {
            return 0;
        }

        if (Shortcut.Matches(_settings.AcceptSecond, key, _controlDown, _altDown, _shiftDown))
        {
            return 1;
        }

        if (Shortcut.Matches(_settings.AcceptThird, key, _controlDown, _altDown, _shiftDown))
        {
            return 2;
        }

        return null;
    }

    private void Accept(int index)
    {
        if (index < 0 || index >= _candidates.Count)
            return;

        string word = EmojiMap.IsEmoji(_candidates[index].Word)
            ? EmojiMap.ApplySelectedSkinTone(_candidates[index].Word)
            : _candidates[index].Word;
        string typedPrefix = _predictionPrefix;
        string activeWord = _current;
        string previous = _previous;
        int replaceLength = activeWord.Length;

        if (Native.TryGetActiveTextContext(
                out Native.ActiveTextContext context))
        {
            activeWord = context.Word;

            if (!string.IsNullOrWhiteSpace(context.Word))
            {
                typedPrefix =
                    context.CaretInsideWord &&
                    !string.IsNullOrWhiteSpace(context.Prefix)
                        ? context.Prefix
                        : context.Word;

                replaceLength = context.Word.Length;

                Native.CapturePredictionTarget(context.Word);
            }

            if (!string.IsNullOrWhiteSpace(context.PreviousWord))
                previous = context.PreviousWord;

            _recentContext = context.RecentContext;
        }

        // Emoji candidates are first-class candidates and do not need to
        // preserve the typed text prefix. Clipboard-style candidates retain
        // their existing special handling.
        bool isEmoji = EmojiMap.IsEmoji(word);
        if (word.StartsWith("📋 "))
        {
            word = word.Substring(3);
            typedPrefix = "";
        }
        else if (typedPrefix.StartsWith(":"))
        {
            typedPrefix = "";
        }

        if (!isEmoji &&
            !string.IsNullOrWhiteSpace(typedPrefix) &&
            !word.StartsWith(typedPrefix, StringComparison.OrdinalIgnoreCase))
            return;

        bool accepted;

        if (replaceLength > 0 &&
            !string.IsNullOrWhiteSpace(activeWord))
        {
            bool upper = !isEmoji && char.IsUpper(activeWord[0]);
            string output = upper
                ? char.ToUpperInvariant(word[0]) + word.Substring(1)
                : word;

            accepted = Native.ReplaceCapturedPredictionWord(
                activeWord,
                output);
        }
        else
        {
            accepted = Native.InsertTextAtCaret(word + " ");
        }

        if (!accepted)
            return;

        FinalizeAcceptedPrediction(word, previous);
    }

    private static List<string> ParseRecentContext(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return new List<string>();

        return context
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(4)
            .ToList();
    }

    private void Commit(string word)
    {
        if (!string.IsNullOrWhiteSpace(word))
        {
            _profile.Learn(word, ParseRecentContext(_recentContext));
            _previous = word.ToLowerInvariant();
        }

        _current = "";
        _candidates = new List<SuggestionCandidate>();
        _selectedPredictionIndex = -1;
        _selectionContextKey = "";
        _pendingShortcutKey = -1;
        _pendingPredictionIndex = -1;
        SchedulePredictionRefresh();
    }

    private string? FindAutocorrect()
    {
        if (_current.Length < 3)
        {
            return null;
        }

        string typed = _current.ToLowerInvariant();

        if (_settings.IsProtectedWord(typed))
        {
            return null;
        }

        if (_profile.IsKnownWord(typed))
        {
            return null;
        }

        string? packCorrection = _profile.TryGetAutocorrection(typed);
        if (!string.IsNullOrWhiteSpace(packCorrection))
            return packCorrection;

        var corrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["helo"] = "hello",
            ["teh"] = "the",
            ["adn"] = "and",
            ["dont"] = "don't",
            ["cant"] = "can't",
            ["wont"] = "won't",
            ["isnt"] = "isn't",
            ["im"] = "I'm",
            ["ive"] = "I've",
            ["youre"] = "you're",
            ["thats"] = "that's",
            ["becuase"] = "because",
            ["recieve"] = "receive",
            ["seperate"] = "separate",
            ["definately"] = "definitely",
            ["occured"] = "occurred",
            ["untill"] = "until",
            ["wich"] = "which",
            ["waht"] = "what",
            ["whay"] = "why",
            ["hte"] = "the"
        };

        if (corrections.TryGetValue(typed, out string? directCorrection))
        {
            Debug.WriteLine(
                $"[AUTOCORRECT] Direct: {_current} -> {directCorrection}");

            return directCorrection;
        }

        string? correction = _settings.PreferredFor(_current);

        if (correction != null)
        {
            return correction;
        }

        return null;
    }

    private void Refresh()
    {
        RefreshFromActiveCaret();
    }

    private void RenderCandidates()
    {
        List<SuggestionCandidate> deduped = _candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Word))
            .GroupBy(candidate => candidate.Word, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .ToList();

        List<SuggestionCandidate> emojis = deduped
            .Where(candidate => candidate.Kind == SuggestionKind.Emoji || EmojiMap.IsEmoji(candidate.Word))
            .Take(3)
            .ToList();

        List<SuggestionCandidate> text = deduped
            .Where(candidate => candidate.Kind != SuggestionKind.Emoji && !EmojiMap.IsEmoji(candidate.Word))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Word.Length)
            .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Five internal candidates: two text + up to three emoji.
        // The overlay compresses the emoji candidates into visual slot #3.
        _candidates = emojis.Count > 0
            ? text.Take(2).Concat(emojis.Take(3)).ToList()
            : text.Take(3).ToList();

        if (_selectedPredictionIndex >= _candidates.Count)
            _selectedPredictionIndex = -1;

        _overlay.Render(
            _candidates
                .Select(candidate =>
                    EmojiMap.IsEmoji(candidate.Word)
                        ? EmojiMap.ApplySelectedSkinTone(candidate.Word)
                        : candidate.Word)
                .ToList(),
            IsPaused,
            _selectedPredictionIndex);
    }

    private static List<SuggestionCandidate> BuildEmojiCandidates(
        string currentWord,
        string previousWord,
        string recentContext)
    {
        // Emoji suggestions should describe the most recent completed word.
        // The active word is often only a prefix, so using it as emoji context
        // produces unstable/unrelated emojis while the user is typing.
        _ = currentWord;

        List<string> history = ParseRecentContext(recentContext)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .ToList();

        if (!string.IsNullOrWhiteSpace(previousWord))
            history.Add(previousWord.Trim().ToLowerInvariant());

        // First choice: exact context of the immediately preceding word.
        // This makes emoji suggestions deterministic instead of mixing emojis
        // from several unrelated words in the previous sentence.
        if (history.Count > 0)
        {
            string newestWord = history[^1];
            List<string> direct = EmojiMap.GetContextual(new[] { newestWord })
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToList();

            if (direct.Count > 0)
            {
                return direct
                    .Select((emoji, index) => new SuggestionCandidate(
                        emoji,
                        320 - (index * 10),
                        0,
                        SuggestionKind.Emoji))
                    .ToList();
            }

            // Fallback: find the newest earlier word with a known emoji mapping.
            // We deliberately stop at the first match rather than combining
            // multiple topics.
            foreach (string word in history.AsEnumerable().Reverse().Skip(1))
            {
                List<string> fallback = EmojiMap.GetContextual(new[] { word })
                    .Distinct(StringComparer.Ordinal)
                    .Take(3)
                    .ToList();

                if (fallback.Count == 0)
                    continue;

                return fallback
                    .Select((emoji, index) => new SuggestionCandidate(
                        emoji,
                        250 - (index * 10),
                        0,
                        SuggestionKind.Emoji))
                    .ToList();
            }
        }

        return new List<SuggestionCandidate>();
    }

    private void RefreshFromActiveCaret()
    {
        if (IsPaused)
        {
            _current = "";
            _candidates = new List<SuggestionCandidate>();
            _overlay.Render(new List<string>(), true);
            return;
        }

        if (!Native.TryGetActiveTextContext(out Native.ActiveTextContext context))
        {
            // UI Automation can temporarily lose TextPattern while an editor
            // updates its caret. Never erase a valid prediction set because of
            // one transient read failure.
            if (_candidates.Count > 0)
            {
                DiagnosticLog(
                    "REFRESH_CONTEXT_UNAVAILABLE preserving-current-candidates");
                return;
            }

            if (!string.IsNullOrWhiteSpace(_previous))
            {
                _candidates = _profile.CandidateModels(
                    _previous,
                    string.Empty,
                    _recentContext);
                RenderCandidates();
                return;
            }

            _current = "";
            return;
        }

        if (string.IsNullOrWhiteSpace(context.Word) &&
            string.IsNullOrWhiteSpace(context.PreviousWord) &&
            string.IsNullOrWhiteSpace(_previous))
        {
            _current = "";
            return;
        }

        _current = context.Word;
        _previous = context.PreviousWord;
        _recentContext = context.RecentContext;
        _refreshAfterBoundary = false;

        string predictionPrefix = !string.IsNullOrWhiteSpace(context.Word)
            ? (context.CaretInsideWord ? context.Prefix : context.Word)
            : string.Empty;

        _predictionPrefix = predictionPrefix;

        // Colon search is a local, bounded emoji mode.
        if (_predictionPrefix.StartsWith(":"))
        {
            string emojiSearch = _predictionPrefix.Substring(1);
            if (emojiSearch.Length > 0)
            {
                _candidates = EmojiMap.GetMatches(emojiSearch)
                    .Take(5)
                    .Select(candidate => new SuggestionCandidate(
                        candidate.Word, candidate.Score, candidate.EditDistance, SuggestionKind.Emoji))
                    .ToList();
                RenderCandidates();
                return;
            }
        }

        string selectionContextKey =
            _recentContext + "|" +
            _previous + "|" +
            context.Word + "|" +
            predictionPrefix;

        if (!string.Equals(
                selectionContextKey,
                _selectionContextKey,
                StringComparison.Ordinal))
        {
            _selectedPredictionIndex = -1;
            _selectionContextKey = selectionContextKey;
        }

        List<SuggestionCandidate> textCandidates = _profile
            .CandidateModels(_previous, predictionPrefix, _recentContext)
            .Where(candidate =>
                string.IsNullOrWhiteSpace(context.Word) ||
                !candidate.Word.Equals(context.Word, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.Kind != SuggestionKind.Emoji && !EmojiMap.IsEmoji(candidate.Word))
            .ToList();

        List<SuggestionCandidate> emojiCandidates = BuildEmojiCandidates(
            context.Word, _previous, _recentContext);

        _candidates = emojiCandidates.Count > 0
            ? textCandidates.Take(2).Concat(emojiCandidates).ToList()
            : textCandidates.Take(3).ToList();

        DiagnosticLog(
            $"REFRESH word=\"{context.Word}\" prefix=\"{context.Prefix}\" " +
            $"previous=\"{context.PreviousWord}\" inside={context.CaretInsideWord} " +
            $"prefixUsed=\"{predictionPrefix}\" count={_candidates.Count} " +
            $"list=[{string.Join(" | ", _candidates.Select(c => c.Word + ":" + c.Score + ":" + c.Kind))}]");

        string? correction = !string.IsNullOrWhiteSpace(context.Word) &&
            !context.CaretInsideWord
            ? FindAutocorrect()
            : null;

        if (correction != null)
        {
            _candidates.RemoveAll(candidate =>
                candidate.Word.Equals(
                    correction,
                    StringComparison.OrdinalIgnoreCase));

            int correctionScore = _candidates.Count > 0
                ? _candidates[0].Score + 1000
                : 1000;

            _candidates.Insert(
                0,
                new SuggestionCandidate(
                    correction,
                    correctionScore,
                    0,
                    SuggestionKind.Correction));

            List<SuggestionCandidate> correctionText = _candidates
                .Where(candidate => candidate.Kind != SuggestionKind.Emoji && !EmojiMap.IsEmoji(candidate.Word))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            List<SuggestionCandidate> correctionEmoji = _candidates
                .Where(candidate => candidate.Kind == SuggestionKind.Emoji || EmojiMap.IsEmoji(candidate.Word))
                .Take(3)
                .ToList();

            _candidates = correctionText.Concat(correctionEmoji).ToList();
        }

        List<string> words = _candidates
            .Select(candidate => candidate.Word)
            .ToList();

        RenderCandidates();
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

    public void Dispose()
    {
        Stop();
    }
}
