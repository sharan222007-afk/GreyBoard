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

    private readonly AdvancedShortcutController _advancedShortcut;

    private readonly DispatcherTimer _caretTimer;
    private readonly DispatcherTimer _predictionRefreshTimer;
    private readonly DispatcherTimer _profileSaveTimer;

    private Native.HookProc? _hookCallback;

    private nint _hook;

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
    private int _consumedNavigationKey = -1;
    private string _selectionContextKey = "";
    private bool _controlDown;
    private bool _altDown;
    private bool _shiftDown;
    private bool _refreshAfterBoundary;
    private int _pendingBoundaryShortcutKey = -1;
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

        _advancedShortcut = new AdvancedShortcutController();

        _advancedShortcut.Configure(
            _settings.AdvancedKey1,
            _settings.AdvancedKey2);

        _advancedShortcut.Enabled =
            _settings.ShortcutMode.Equals(
                "Advanced",
                StringComparison.OrdinalIgnoreCase);
        _overlay.SetPredictionClickHandler(Accept);
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
            Interval = TimeSpan.FromMilliseconds(45L)
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

        _hookCallback = HookCallback;
    }

    public void Start()
    {
        if (IsRunning)
            return;

        using Process process = Process.GetCurrentProcess();
        using ProcessModule module = process.MainModule;

        _hook = Native.SetWindowsHookEx(
            13,
            _hookCallback,
            Native.GetModuleHandle(module.ModuleName),
            0u);

        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Windows could not start the keyboard listener.");
        }

        IsRunning = true;
        IsPaused = false;
        _overlay.ShowOverlay();
        _caretTimer.Start();
        _profileSaveTimer.Start();
        RefreshFromActiveCaret();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (IsRunning)
        {
            _caretTimer.Stop();
            _predictionRefreshTimer.Stop();
            _profileSaveTimer.Stop();
            if (_profile.IsDirty)
                _profile.Save();
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
            }
            _hook = IntPtr.Zero;
            _current = "";
            _controlDown = false;
            _altDown = false;
            _shiftDown = false;
            _advancedShortcut.Reset();
            ClearPendingPrediction();
            _pendingBoundaryShortcutKey = -1;
            _refreshAfterBoundary = false;
            _candidates = new List<SuggestionCandidate>();
            IsRunning = false;
            IsPaused = false;
            _overlay.HideOverlay();
            StateChanged?.Invoke();
        }
    }

    public void TogglePause()
    {
        if (IsRunning)
        {
            IsPaused = !IsPaused;
            _current = "";
            _predictionRefreshTimer.Stop();
            _pendingBoundaryShortcutKey = -1;
            _refreshAfterBoundary = false;
            _candidates = new List<SuggestionCandidate>();
            Refresh();
            StateChanged?.Invoke();
        }
    }

    public void ApplySettings()
    {
        _advancedShortcut.Configure(
            _settings.AdvancedKey1,
            _settings.AdvancedKey2);

        _advancedShortcut.Enabled =
            _settings.ShortcutMode.Equals(
                "Advanced",
                StringComparison.OrdinalIgnoreCase);

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

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return Native.CallNextHookEx(_hook, code, wParam, lParam);

        bool isKeyDown = wParam == 0x0100 || wParam == 0x0104;
        bool isKeyUp = wParam == 0x0101 || wParam == 0x0105;

        if (!isKeyDown && !isKeyUp)
            return Native.CallNextHookEx(_hook, code, wParam, lParam);

        Native.KBDLLHOOKSTRUCT data =
            Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);

        if ((data.flags & Native.LLKHF_INJECTED) != 0)
            return Native.CallNextHookEx(_hook, code, wParam, lParam);

        int key = (int)data.vkCode;

        // ------------------------------------------------------------
        // ADVANCED PREDICTION GESTURE
        // ------------------------------------------------------------
        //
        // IMPORTANT:
        // Advanced gesture handling happens BEFORE UpdateModifierState.
        //
        // This is intentional:
        //
        // Shift DOWN
        //     -> let Windows receive Shift DOWN normally
        //
        // Alt DOWN
        //     -> GreyBoard consumes it
        //
        // Alt UP
        //     -> GreyBoard consumes it
        //
        // Shift UP
        //     -> GreyBoard commits prediction,
        //        BUT Windows MUST still receive Shift UP.
        //
        // Otherwise Windows thinks Shift is still physically held.
        //

        if (_settings.ShortcutMode.Equals(
                "Advanced",
                StringComparison.OrdinalIgnoreCase))
        {
            bool gestureWasActive =
                _advancedShortcut.IsGestureActive;

            if (isKeyDown)
            {
                AdvancedShortcutController.Action action =
                    _advancedShortcut.HandleKeyDown(key);

                if (action ==
                    AdvancedShortcutController.Action.CycleNext)
                {
                    CycleAdvancedPrediction();

                    // Consume Key 2 DOWN.
                    // Windows must not see Alt DOWN.
                    return 1;
                }
            }
            else if (isKeyUp)
            {
                bool wasKey2 =
                    _advancedShortcut.IsKey2(key);
                bool key2WasDown =
                    _advancedShortcut.IsKey2Down;

                bool wasKey1 =
                    _advancedShortcut.IsKey1(key);

                AdvancedShortcutController.Action action =
                    _advancedShortcut.HandleKeyUp(key);

                if (wasKey2 && key2WasDown)
                {
                    // Consume Key 2 UP.
                    //
                    // We consumed Alt DOWN, so Windows must NOT receive
                    // an unmatched Alt UP.
                    return 1;
                }

                if (wasKey1 &&
                    action ==
                    AdvancedShortcutController.Action.Commit)
                {
                    CommitAdvancedPrediction();

                    // CRITICAL:
                    //
                    // DO NOT return 1 here.
                    //B
                    // Windows needs to receive the real Shift UP event.
                    // Otherwise Shift remains logically pressed and every
                    // following character becomes uppercase.
                }
            }
        }

        // Only update our normal modifier state after Advanced gesture
        // interception has had the chance to consume the gesture keys.
        UpdateModifierState(key, isKeyDown, isKeyUp);

        if (isKeyUp && key == _pendingBoundaryShortcutKey)
        {
            int boundaryShortcutKey = _pendingBoundaryShortcutKey;
            _pendingBoundaryShortcutKey = -1;

            _caretTimer.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    if (IsPaused)
                        return;

                    RefreshFromActiveCaret();
                    int? boundaryChoice = PredictionChoice(boundaryShortcutKey);

                    if (!boundaryChoice.HasValue ||
                        boundaryChoice.Value < 0 ||
                        boundaryChoice.Value >= _candidates.Count)
                        return;

                    SnapshotPredictionForAcceptance(boundaryShortcutKey, boundaryChoice.Value);
                    CompletePendingPredictionAcceptance();
                }));

            return 1;
        }

        if (isKeyUp &&
            key == _pendingShortcutKey &&
            _pendingPredictionIndex >= 0)
        {
            _pendingShortcutKeyUpReceived = true;

            if (!_controlDown && !_altDown && !_shiftDown)
                CompletePendingPredictionAcceptance();

            return 1;
        }

        if (isKeyUp &&
            _pendingPredictionIndex >= 0 &&
            _pendingShortcutKeyUpReceived &&
            (key == 16 || key == 160 || key == 161 ||
             key == 17 || key == 162 || key == 163 ||
             key == 18 || key == 164 || key == 165))
        {
            if (!_controlDown && !_altDown && !_shiftDown)
                CompletePendingPredictionAcceptance();
        }

        if (isKeyUp && key == _consumedNavigationKey)
        {
            _consumedNavigationKey = -1;
            return 1;
        }

        if (!isKeyDown)
            return Native.CallNextHookEx(_hook, code, wParam, lParam);

        if (Shortcut.Matches(
                _settings.PauseShortcut,
                key,
                _controlDown,
                _altDown,
                _shiftDown))
        {
            TogglePause();
            return 1;
        }

        if (IsPaused)
            return Native.CallNextHookEx(_hook, code, wParam, lParam);

        // Space is handled before the target application receives it. If the
        // user immediately presses a Classic prediction shortcut, defer that
        // shortcut until its key-up, then reacquire the post-Space caret.
        if (_refreshAfterBoundary && IsPredictionShortcut(key))
        {
            _pendingBoundaryShortcutKey = key;
            _refreshAfterBoundary = false;
            return 1;
        }

        int? choice = PredictionChoice(key);

        if (choice.HasValue &&
            choice.Value >= 0 &&
            choice.Value < _candidates.Count)
        {
            SnapshotPredictionForAcceptance(key, choice.Value);

            return 1;
        }

        if (key == Native.VK_BACK)
        {
            SchedulePredictionRefresh();
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        bool boundary =
            key == Native.VK_SPACE ||
            key == Native.VK_RETURN ||
            key == 186 ||
            key == 188 ||
            key == 190 ||
            key == 191;

        if (boundary)
        {
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
                            StringComparison.OrdinalIgnoreCase) &&
                        Native.ReplaceActiveWord(correction))
                    {
                        _settings.RecordAutocorrection(
                            typed,
                            correction);

                        _current = correction;
                        Commit(correction);

                        return Native.CallNextHookEx(
                            _hook,
                            code,
                            wParam,
                            lParam);
                    }
                }
            }

            Commit(_current);
            _refreshAfterBoundary = true;
            SchedulePredictionRefresh();

            return Native.CallNextHookEx(
                _hook,
                code,
                wParam,
                lParam);
        }

        if (isKeyDown && !IsModifierKey(key))
            SchedulePredictionRefresh();

        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool IsModifierKey(int key)
    {
        return key == 16 || key == 17 || key == 18 ||
               key == 160 || key == 161 || key == 162 || key == 163 ||
               key == 164 || key == 165;
    }

    private bool IsPredictionShortcut(int key)
    {
        return Shortcut.Matches(_settings.AcceptFirst, key, _controlDown, _altDown, _shiftDown) ||
               Shortcut.Matches(_settings.AcceptSecond, key, _controlDown, _altDown, _shiftDown) ||
               Shortcut.Matches(_settings.AcceptThird, key, _controlDown, _altDown, _shiftDown);
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

        // Capture the freshest context at acceptance time. This is especially
        // important for Advanced Shift+Alt, where selection and Shift release
        // are separated by a real physical key interval.
        if (Native.TryGetActiveTextContext(
                out Native.ActiveTextContext context))
        {
            _pendingPredictionActiveWord = context.Word;
            _pendingPredictionPrevious =
                string.IsNullOrWhiteSpace(context.PreviousWord)
                    ? _pendingPredictionPrevious
                    : context.PreviousWord;
            _pendingPredictionRecentContext = context.RecentContext;
            _pendingPredictionTyped =
                string.IsNullOrWhiteSpace(context.Word)
                    ? string.Empty
                    : context.CaretInsideWord &&
                      !string.IsNullOrWhiteSpace(context.Prefix)
                        ? context.Prefix
                        : context.Word;
            _pendingPredictionReplaceLength = context.Word.Length;

            if (!string.IsNullOrWhiteSpace(context.Word))
                Native.CapturePredictionTarget(context.Word);
        }

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

        if (!string.IsNullOrWhiteSpace(typedPrefix) &&
            !word.StartsWith(
                typedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            DiagnosticLog(
                $"ACCEPT_REJECT staleCandidate prefix=\"{typedPrefix}\" " +
                $"word=\"{word}\"");
            return;
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

        string output = char.IsUpper(context.Word[0])
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
            Math.Min(3, _candidates.Count);

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
        string word = _candidates[index].Word;

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

        string word = _candidates[index].Word;
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

        // Clean up prefixes for Emoji and Clipboard injections
        if (word.StartsWith("📋 "))
        {
            word = word.Substring(3);
            typedPrefix = "";
        }
        else if (typedPrefix.StartsWith(":"))
        {
            typedPrefix = "";
        }

        if (!string.IsNullOrWhiteSpace(typedPrefix) &&
            !word.StartsWith(
                typedPrefix,
                StringComparison.OrdinalIgnoreCase))
            return;

        bool accepted;

        if (replaceLength > 0 &&
            !string.IsNullOrWhiteSpace(activeWord))
        {
            bool upper = char.IsUpper(activeWord[0]);
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
        List<SuggestionCandidate> normalized = _candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Word))
            .GroupBy(
                candidate => candidate.Word,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => candidate.Score)
                .First())
            .OrderByDescending(candidate => candidate.Score)
            .Take(3)
            .ToList();

        // The prediction bar is a three-slot control. Never present a partial
        // prediction set during normal typing. The prediction engine is given
        // a chance to produce three candidates; if it cannot, keep the current
        // UI hidden rather than showing an empty/partial bar.
        if (!IsPaused && normalized.Count < 3)
        {
            DiagnosticLog(
                $"RENDER_INCOMPLETE_PREDICTIONS count={normalized.Count}; hiding-partial-bar");
            _candidates = normalized;
            _selectedPredictionIndex = -1;
            _overlay.Render(
                Array.Empty<string>(),
                false,
                -1);
            return;
        }

        _candidates = normalized;

        if (_selectedPredictionIndex >= _candidates.Count)
            _selectedPredictionIndex = -1;

        List<string> words = _candidates
            .Select(candidate => candidate.Word)
            .ToList();

        _overlay.Render(
            words,
            IsPaused,
            _selectedPredictionIndex);
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

        // Emoji Search Feature
        if (_predictionPrefix.StartsWith(":"))
        {
            string emojiSearch = _predictionPrefix.Substring(1);
            if (emojiSearch.Length > 0)
            {
                _candidates = EmojiMap.GetMatches(emojiSearch);
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

        _candidates = _profile
            .CandidateModels(_previous, predictionPrefix, _recentContext);

        if (!string.IsNullOrWhiteSpace(predictionPrefix))
        {
            // LanguageProfile.CandidateModels already performs the controlled
            // prefix/context ranking. Do not apply a second hard prefix filter
            // here: doing so discards the contextual fallback candidates that
            // are intentionally used when a prefix has fewer than three strong
            // completions. Only suppress the exact word already typed.
            _candidates = _candidates
                .Where(candidate =>
                    string.IsNullOrWhiteSpace(context.Word) ||
                    !candidate.Word.Equals(
                        context.Word,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

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

            _candidates = _candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Word)
                .Take(3)
                .ToList();
        }

        List<string> words = _candidates
            .Select(candidate => candidate.Word)
            .ToList();

        Debug.WriteLine(
            "[RANK] " + string.Join(
                " | ",
                _candidates.Select(candidate =>
                    $"{candidate.Word}:{candidate.Score}:{candidate.Kind}")));

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
