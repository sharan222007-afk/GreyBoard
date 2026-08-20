using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace TypeSenseOverlay;

internal static class Native
{
    private static TextPatternRange? _capturedSelection;
    private static TextPatternRange? _capturedPredictionWordRange;
    private static AutomationElement? _lastTextElement;
    private static ActiveTextContext _lastTextContext;
    private static bool _hasLastTextContext;
    private static bool _lastActiveWordHasTrailingSpace;
    private static bool _capturedPredictionHasTrailingSpace;

    public readonly struct ActiveTextContext
    {
        public ActiveTextContext(
            string word,
            string previousWord,
            string prefix,
            bool caretInsideWord,
            string recentContext)
        {
            Word = word;
            PreviousWord = previousWord;
            Prefix = prefix;
            CaretInsideWord = caretInsideWord;
            RecentContext = recentContext;
        }

        public string Word { get; }
        public string PreviousWord { get; }
        public string Prefix { get; }
        public bool CaretInsideWord { get; }
        public string RecentContext { get; }
    }

    public static bool TryGetActiveTextContext(out ActiveTextContext context)
    {
        context = default;
        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null ||
                !focused.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject))
                return false;

            TextPattern pattern = (TextPattern)patternObject;
            TextPatternRange[] selection = pattern.GetSelection();
            if (selection == null || selection.Length == 0)
                return false;

            TextPatternRange document = pattern.DocumentRange.Clone();
            TextPatternRange before = document.Clone();
            before.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                selection[0],
                TextPatternRangeEndpoint.Start);

            string allText = document.GetText(-1) ?? string.Empty;
            string beforeText = before.GetText(-1) ?? string.Empty;

            // UI Automation can report a collapsed selection with an endpoint
            // that is one character off while an editor is updating. Use the
            // document prefix as the stable caret position and clamp it.
            int caret = Math.Clamp(beforeText.Length, 0, allText.Length);

            int start = caret;
            int end = caret;

            if (start < allText.Length && IsWordCharacter(allText[start]))
            {
                while (start > 0 && IsWordCharacter(allText[start - 1])) start--;
                while (end < allText.Length && IsWordCharacter(allText[end])) end++;
            }
            else if (caret > 0 && IsWordCharacter(allText[caret - 1]))
            {
                start = caret - 1;
                while (start > 0 && IsWordCharacter(allText[start - 1])) start--;
                end = caret;
                while (end < allText.Length && IsWordCharacter(allText[end])) end++;
            }
            else
            {
                // Caret is immediately after a separator (for example after
                // pressing Space). Keep the preceding word as context so the
                // prediction engine can offer the next three words.
                // The preceding word is computed below from the same caret
                // position. No second context endpoint is needed here.

                // If there is no previous word, the caller will naturally
                // suppress predictions because both context fields are empty.
                // Keep the context valid here so a space after a real word
                // can produce next-word predictions.
                start = caret;
                end = caret;
            }

            if (end < start)
                return false;

            string word = allText.Substring(start, end - start).Trim();

            int previousEnd = start - 1;
            while (previousEnd >= 0 && !IsWordCharacter(allText[previousEnd])) previousEnd--;
            int previousStart = previousEnd;
            while (previousStart >= 0 && IsWordCharacter(allText[previousStart])) previousStart--;

            string previousWord = previousEnd >= previousStart + 1
                ? allText.Substring(previousStart + 1, previousEnd - previousStart)
                : string.Empty;

            bool inside = caret > start && caret < end;
            string prefix = inside
                ? allText.Substring(start, caret - start)
                : string.Empty;

            // The prediction engine needs more than the immediately previous
            // word. We already have the document text in memory here, so build
            // a tiny local context window from the words before the active
            // word/caret. This is used only for local prediction ranking.
            int contextEnd = Math.Clamp(start, 0, allText.Length);
            string recentContext = ExtractRecentContext(allText, contextEnd, 8);

            context = new ActiveTextContext(
                word,
                previousWord,
                prefix,
                inside,
                recentContext);

            _lastTextElement = focused;
            _lastTextContext = context;
            _hasLastTextContext = true;

            if (!string.IsNullOrWhiteSpace(word))
            {
                _lastActiveWordHasTrailingSpace =
                    end < allText.Length &&
                    allText[end] == ' ';

                _capturedPredictionWordRange =
                    CreateRange(document, start, end);
            }
            else
            {
                _lastActiveWordHasTrailingSpace = false;
                _capturedPredictionWordRange = null;
            }

            return true;
        }
        catch
        {
            context = default;
            return false;
        }
    }

    public static bool TryGetCapturedTextContext(out ActiveTextContext context)
    {
        if (_hasLastTextContext)
        {
            context = _lastTextContext;
            return true;
        }

        context = default;
        return false;
    }

    public static bool CapturePredictionTarget(string expectedWord)
    {
        if (string.IsNullOrWhiteSpace(expectedWord))
            return false;

        try
        {
            if (!TryGetActiveTextContext(out ActiveTextContext context))
                return false;

            if (!context.Word.Equals(
                    expectedWord,
                    StringComparison.Ordinal))
                return false;

            if (_capturedPredictionWordRange == null)
                return false;

            _capturedPredictionWordRange =
                _capturedPredictionWordRange.Clone();

            _capturedPredictionHasTrailingSpace =
                _lastActiveWordHasTrailingSpace;

            DiagnosticLogNative(
                $"PREDICTION_TARGET_CAPTURED word=\"{expectedWord}\" " +
                $"trailingSpace={_capturedPredictionHasTrailingSpace}");

            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogNative(
                $"PREDICTION_TARGET_CAPTURE_FAIL {ex.GetType().Name}: {ex.Message}");
            _capturedPredictionWordRange = null;
            return false;
        }
    }

    public static bool ReplaceCapturedPredictionWord(
        string expectedWord,
        string replacement)
    {
        if (string.IsNullOrWhiteSpace(expectedWord) ||
            string.IsNullOrWhiteSpace(replacement) ||
            _capturedPredictionWordRange == null)
            return false;

        try
        {
            if (!RestoreLastTextTargetFocus())
            {
                DiagnosticLogNative("PREDICTION_REPLACE_FOCUS_FAIL");
                return false;
            }

            TextPatternRange target =
                _capturedPredictionWordRange.Clone();

            target.Select();
            Thread.Sleep(15);

            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null ||
                !focused.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out object patternObject))
                return false;

            TextPattern pattern = (TextPattern)patternObject;
            TextPatternRange[] selectedRanges = pattern.GetSelection();

            if (selectedRanges == null || selectedRanges.Length == 0)
                return false;

            string selected =
                selectedRanges[0].GetText(-1) ?? string.Empty;

            if (!selected.Equals(
                    expectedWord,
                    StringComparison.Ordinal))
            {
                DiagnosticLogNative(
                    $"PREDICTION_REPLACE_SELECT_FAIL " +
                    $"expected=\"{expectedWord}\" actual=\"{selected}\"");
                return false;
            }

            // IMPORTANT: do NOT delete the selection first.
            // Sending Unicode text while the exact word is selected makes the
            // editor perform an atomic replacement. The previous implementation
            // pressed Backspace first; if Unicode injection then failed, the
            // original word was left erased with nothing inserted.
            bool hasSpace = _capturedPredictionHasTrailingSpace;
            string textToInsert = hasSpace
                ? replacement
                : replacement + " ";

            if (!SendUnicodeText(textToInsert))
            {
                DiagnosticLogNative(
                    $"PREDICTION_REPLACE_INPUT_FAIL text=\"{textToInsert}\"");
                return false;
            }

            Thread.Sleep(20);

            // If a separator already existed, the replacement leaves the caret
            // immediately before it. Move past that existing separator.
            if (hasSpace &&
                !SendVirtualKeyChecked(VK_RIGHT))
            {
                DiagnosticLogNative(
                    "PREDICTION_REPLACE_SPACE_ADVANCE_FAIL");
                return false;
            }

            DiagnosticLogNative(
                $"PREDICTION_REPLACE_OK old=\"{expectedWord}\" " +
                $"new=\"{replacement}\" trailingSpace={hasSpace}");

            _capturedPredictionWordRange = null;
            _capturedPredictionHasTrailingSpace = false;
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogNative(
                $"PREDICTION_REPLACE_EXCEPTION {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool RestoreLastTextTargetFocus()
    {
        try
        {
            if (_lastTextElement == null)
                return false;

            _lastTextElement.SetFocus();
            Thread.Sleep(25);

            AutomationElement focused = AutomationElement.FocusedElement;
            return focused != null &&
                focused.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out _);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetSelectedText(out string selectedText)
    {
        selectedText = string.Empty;
        _capturedSelection = null;
        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null ||
                !focused.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject))
                return false;

            TextPattern pattern = (TextPattern)patternObject;
            TextPatternRange[] selection = pattern.GetSelection();
            if (selection == null || selection.Length == 0)
                return false;

            string text = selection[0].GetText(-1) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            selectedText = text.TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(selectedText))
                return false;

            _capturedSelection = selection[0].Clone();
            return true;
        }
        catch
        {
            _capturedSelection = null;
            selectedText = string.Empty;
            return false;
        }
    }

    public static bool ReplaceCapturedSelection(string replacement)
    {
        if (string.IsNullOrEmpty(replacement) || _capturedSelection == null)
            return false;

        try
        {
            _capturedSelection.Select();
            SendUnicodeText(replacement);
            _capturedSelection = null;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ReplaceSelectedText(string replacement)
    {
        if (string.IsNullOrEmpty(replacement))
            return false;

        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null ||
                !focused.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject))
                return false;

            TextPattern pattern = (TextPattern)patternObject;
            TextPatternRange[] selection = pattern.GetSelection();
            if (selection == null || selection.Length == 0)
                return false;

            string selected = selection[0].GetText(-1) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selected))
                return false;

            selection[0].Select();
            SendUnicodeText(replacement);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ReplaceActivePrefix(string replacement)
    {
        if (string.IsNullOrEmpty(replacement))
            return false;

        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null ||
                !focused.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject))
                return false;

            TextPattern pattern = (TextPattern)patternObject;
            TextPatternRange[] selection = pattern.GetSelection();
            if (selection == null || selection.Length == 0)
                return false;

            TextPatternRange document = pattern.DocumentRange.Clone();
            TextPatternRange before = document.Clone();
            before.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                selection[0],
                TextPatternRangeEndpoint.Start);

            string allText = document.GetText(-1) ?? string.Empty;
            string beforeText = before.GetText(-1) ?? string.Empty;
            int caret = Math.Min(beforeText.Length, allText.Length);

            int start = caret;
            while (start > 0 && IsWordCharacter(allText[start - 1]))
                start--;

            if (caret <= start)
                return false;

            TextPatternRange target = CreateRange(document, start, caret);
            target.Select();
            SendUnicodeText(replacement);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool InsertAfterActiveWord(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null ||
                !focused.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject))
                return false;

            TextPattern pattern = (TextPattern)patternObject;
            TextPatternRange[] selection = pattern.GetSelection();
            if (selection == null || selection.Length == 0)
                return false;

            TextPatternRange document = pattern.DocumentRange.Clone();
            TextPatternRange before = document.Clone();
            before.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                selection[0],
                TextPatternRangeEndpoint.Start);

            string allText = document.GetText(-1) ?? string.Empty;
            string beforeText = before.GetText(-1) ?? string.Empty;
            int caret = Math.Min(beforeText.Length, allText.Length);

            int end = caret;
            if (caret > 0 && caret <= allText.Length && IsWordCharacter(allText[caret - 1]))
            {
                while (end < allText.Length && IsWordCharacter(allText[end]))
                    end++;
            }

            TextPatternRange insertion = CreateRange(document, end, end);
            insertion.Select();
            SendUnicodeText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TextPatternRange CreateRange(
        TextPatternRange document,
        int start,
        int end)
    {
        TextPatternRange target = document.Clone();

        TextPatternRange targetStart = document.Clone();
        targetStart.MoveEndpointByRange(
            TextPatternRangeEndpoint.Start,
            document,
            TextPatternRangeEndpoint.Start);
        targetStart.MoveEndpointByRange(
            TextPatternRangeEndpoint.End,
            document,
            TextPatternRangeEndpoint.Start);
        targetStart.Move(TextUnit.Character, start);

        TextPatternRange targetEnd = document.Clone();
        targetEnd.MoveEndpointByRange(
            TextPatternRangeEndpoint.Start,
            document,
            TextPatternRangeEndpoint.Start);
        targetEnd.MoveEndpointByRange(
            TextPatternRangeEndpoint.End,
            document,
            TextPatternRangeEndpoint.Start);
        targetEnd.Move(TextUnit.Character, end);

        target.MoveEndpointByRange(
            TextPatternRangeEndpoint.Start,
            targetStart,
            TextPatternRangeEndpoint.Start);
        target.MoveEndpointByRange(
            TextPatternRangeEndpoint.End,
            targetEnd,
            TextPatternRangeEndpoint.Start);

        return target;
    }

    public static bool ReplaceActiveWord(string replacement)
    {
        if (string.IsNullOrEmpty(replacement))
            return false;

        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null ||
                !focused.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject))
                return false;

            TextPattern pattern = (TextPattern)patternObject;
            TextPatternRange[] selection = pattern.GetSelection();
            if (selection == null || selection.Length == 0)
                return false;

            TextPatternRange document = pattern.DocumentRange.Clone();
            TextPatternRange before = document.Clone();
            before.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                selection[0],
                TextPatternRangeEndpoint.Start);

            string allText = document.GetText(-1) ?? string.Empty;
            string beforeText = before.GetText(-1) ?? string.Empty;
            int caret = Math.Min(beforeText.Length, allText.Length);

            int start = caret;
            int end = caret;

            if (start < allText.Length && IsWordCharacter(allText[start]))
            {
                while (start > 0 && IsWordCharacter(allText[start - 1])) start--;
                while (end < allText.Length && IsWordCharacter(allText[end])) end++;
            }
            else if (caret > 0 && IsWordCharacter(allText[caret - 1]))
            {
                start = caret - 1;
                while (start > 0 && IsWordCharacter(allText[start - 1])) start--;
                end = caret;
                while (end < allText.Length && IsWordCharacter(allText[end])) end++;
            }
            else
            {
                return false;
            }

            // If the caret is inside the word, replace only the prefix before it.
            // If it is at the word end, replace the whole word.
            int replaceEnd = (caret > start && caret < end) ? caret : end;

            TextPatternRange target = document.Clone();
            TextPatternRange targetStart = document.Clone();
            TextPatternRange targetEnd = document.Clone();

            MoveRangeEndpointToTextOffset(
                targetStart,
                document,
                start);
            MoveRangeEndpointToTextOffset(
                targetEnd,
                document,
                replaceEnd);

            target.MoveEndpointByRange(
                TextPatternRangeEndpoint.Start,
                targetStart,
                TextPatternRangeEndpoint.Start);
            target.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                targetEnd,
                TextPatternRangeEndpoint.Start);

            target.Select();
            SendUnicodeText(replacement);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void MoveRangeEndpointToTextOffset(
        TextPatternRange target,
        TextPatternRange document,
        int offset)
    {
        target.MoveEndpointByRange(
            TextPatternRangeEndpoint.Start,
            document,
            TextPatternRangeEndpoint.Start);
        target.MoveEndpointByRange(
            TextPatternRangeEndpoint.End,
            document,
            TextPatternRangeEndpoint.Start);

        target.Move(
            TextUnit.Character,
            offset);
    }

    private static string ExtractRecentContext(string text, int end, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(text) || end <= 0 || maxWords <= 0)
            return string.Empty;

        end = Math.Clamp(end, 0, text.Length);
        List<string> words = new List<string>(maxWords);
        int cursor = end - 1;

        while (cursor >= 0 && words.Count < maxWords)
        {
            while (cursor >= 0 && !IsWordCharacter(text[cursor]))
                cursor--;

            if (cursor < 0)
                break;

            int wordEnd = cursor + 1;
            while (cursor >= 0 && IsWordCharacter(text[cursor]))
                cursor--;

            string word = text.Substring(cursor + 1, wordEnd - cursor - 1);
            if (word.Length >= 2)
                words.Add(word);
        }

        words.Reverse();
        return string.Join(" ", words);
    }

    private static bool IsWordCharacter(char c)
    {
        return char.IsLetterOrDigit(c) || c == '\'' || c == '_';
    }


    // Prediction selection uses the prefix at the caret, but acceptance
    // replaces the COMPLETE active word. This is intentionally separate from
    // ReplaceTypedCharacters(), which is prefix-oriented.
    // Prediction uses the prefix at the caret, but acceptance replaces the
    // COMPLETE active word. We deliberately perform the edit with real
    // keyboard navigation instead of relying on TextPatternRange.Select()
    // becoming the editor's keyboard selection.
    public static bool ReplaceEntireActiveWord(
        string expectedWord,
        string replacement)
    {
        if (string.IsNullOrWhiteSpace(expectedWord) ||
            string.IsNullOrWhiteSpace(replacement))
            return false;

        try
        {
            if (!RestoreLastTextTargetFocus())
            {
                DiagnosticLogNative("REPLACE_WORD_FOCUS_FAIL");
                return false;
            }

            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null ||
                !focused.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out object patternObject))
                return false;

            TextPattern pattern = (TextPattern)patternObject;
            TextPatternRange[] selection = pattern.GetSelection();

            if (selection == null || selection.Length == 0)
                return false;

            string selectedAtStart = selection[0].GetText(-1) ?? string.Empty;
            if (!string.IsNullOrEmpty(selectedAtStart))
            {
                DiagnosticLogNative(
                    $"REPLACE_WORD_FAIL selectionNotCollapsed=\"{selectedAtStart}\"");
                return false;
            }

            TextPatternRange document = pattern.DocumentRange.Clone();
            TextPatternRange before = document.Clone();

            before.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                selection[0],
                TextPatternRangeEndpoint.Start);

            string allText = document.GetText(-1) ?? string.Empty;
            string beforeText = before.GetText(-1) ?? string.Empty;

            int caret = Math.Clamp(
                beforeText.Length,
                0,
                allText.Length);

            int start = caret;
            int endWord = caret;

            if (caret < allText.Length &&
                IsWordCharacter(allText[caret]))
            {
                while (start > 0 &&
                       IsWordCharacter(allText[start - 1]))
                    start--;

                while (endWord < allText.Length &&
                       IsWordCharacter(allText[endWord]))
                    endWord++;
            }
            else if (caret > 0 &&
                     IsWordCharacter(allText[caret - 1]))
            {
                start = caret - 1;

                while (start > 0 &&
                       IsWordCharacter(allText[start - 1]))
                    start--;

                endWord = caret;

                while (endWord < allText.Length &&
                       IsWordCharacter(allText[endWord]))
                    endWord++;
            }
            else
            {
                DiagnosticLogNative(
                    "REPLACE_WORD_FAIL no active word");
                return false;
            }

            string actualWord =
                allText.Substring(start, endWord - start);

            if (!actualWord.Equals(
                    expectedWord,
                    StringComparison.Ordinal))
            {
                DiagnosticLogNative(
                    $"REPLACE_WORD_VERIFY_FAIL expected=\"{expectedWord}\" " +
                    $"actual=\"{actualWord}\"");
                return false;
            }

            int prefixLength = caret - start;
            int wordLength = endWord - start;

            DiagnosticLogNative(
                $"REPLACE_WORD_TARGET word=\"{expectedWord}\" " +
                $"caret={caret} start={start} end={endWord} " +
                $"prefixLength={prefixLength} wordLength={wordLength}");

            // Put the real keyboard caret at the beginning of the active word.
            if (!MoveCaretLeftChecked(prefixLength))
            {
                DiagnosticLogNative(
                    $"REPLACE_WORD_MOVE_START_FAIL count={prefixLength}");
                return false;
            }

            // Select the COMPLETE active word.
            if (!SelectForwardChecked(wordLength))
            {
                DiagnosticLogNative(
                    $"REPLACE_WORD_SELECT_INPUT_FAIL count={wordLength}");
                SendVirtualKey(VK_ESCAPE);
                return false;
            }

            Thread.Sleep(20);

            // Verify that the keyboard selection really is the expected word.
            TextPatternRange[] selectedRanges = pattern.GetSelection();
            if (selectedRanges == null || selectedRanges.Length == 0)
            {
                SendVirtualKey(VK_ESCAPE);
                return false;
            }

            string selected =
                selectedRanges[0].GetText(-1) ?? string.Empty;

            if (!selected.Equals(
                    expectedWord,
                    StringComparison.Ordinal))
            {
                DiagnosticLogNative(
                    $"REPLACE_WORD_SELECT_VERIFY_FAIL " +
                    $"expected=\"{expectedWord}\" actual=\"{selected}\"");
                SendVirtualKey(VK_ESCAPE);
                return false;
            }

            // Preserve one normal separator after the accepted prediction.
            // If the original word already had whitespace after it, reuse that
            // whitespace instead of creating a double space.
            bool hasExistingSeparator =
                endWord < allText.Length &&
                char.IsWhiteSpace(allText[endWord]);

            string textToInsert = hasExistingSeparator
                ? replacement
                : replacement + " ";

            if (!SendVirtualKeyChecked(VK_BACK))
            {
                DiagnosticLogNative(
                    "REPLACE_WORD_DELETE_INPUT_FAIL");
                SendVirtualKey(VK_ESCAPE);
                return false;
            }

            Thread.Sleep(20);

            if (!SendUnicodeText(textToInsert))
            {
                DiagnosticLogNative(
                    $"REPLACE_WORD_INPUT_FAIL text=\"{textToInsert}\"");
                return false;
            }

            // When the separator already existed, the insertion leaves the
            // caret immediately before that separator. Move over it so the
            // next typed character continues after the space.
            if (hasExistingSeparator &&
                !SendVirtualKeyChecked(VK_RIGHT))
            {
                DiagnosticLogNative(
                    "REPLACE_WORD_SEPARATOR_ADVANCE_FAIL");
                return false;
            }

            DiagnosticLogNative(
                $"REPLACE_WORD_OK old=\"{expectedWord}\" " +
                $"new=\"{replacement}\" separatorInserted={!hasExistingSeparator}");

            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogNative(
                $"REPLACE_WORD_EXCEPTION {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }


    private static bool MoveCaretLeftChecked(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (!SendVirtualKeyChecked(VK_LEFT))
                return false;
        }

        return true;
    }

    private static bool SelectForwardChecked(int count)
    {
        if (!SendKeyDownChecked(VK_SHIFT))
            return false;

        try
        {
            for (int i = 0; i < count; i++)
            {
                if (!SendVirtualKeyChecked(VK_RIGHT))
                    return false;
            }

            return true;
        }
        finally
        {
            SendKeyUpChecked(VK_SHIFT);
        }
    }

    private static bool SendVirtualKeyChecked(ushort vk)
    {
        INPUT down = new INPUT
        {
            type = 1u,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = 0u
                }
            }
        };

        INPUT up = new INPUT
        {
            type = 1u,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = 2u
                }
            }
        };

        INPUT[] input = { down, up };
        uint sent = SendInput(
            (uint)input.Length,
            input,
            Marshal.SizeOf<INPUT>());

        return sent == (uint)input.Length;
    }

    private static bool SendKeyDownChecked(ushort vk)
    {
        INPUT input = new INPUT
        {
            type = 1u,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = 0u
                }
            }
        };

        return SendInput(
            1u,
            new[] { input },
            Marshal.SizeOf<INPUT>()) == 1u;
    }

    private static bool SendKeyUpChecked(ushort vk)
    {
        INPUT input = new INPUT
        {
            type = 1u,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = 2u
                }
            }
        };

        return SendInput(
            1u,
            new[] { input },
            Marshal.SizeOf<INPUT>()) == 1u;
    }

    // Robust completion replacement used by prediction acceptance.
    // Do not rely on UI Automation selecting a TextPatternRange: some editors
    // visually show the range as selected but still insert at the caret.
    // The caret is already at the end of the active prefix, so deleting the
    // exact number of typed characters is deterministic.
    public static bool ReplaceTypedCharacters(string typed, string replacement)
    {
        if (string.IsNullOrEmpty(typed) || string.IsNullOrEmpty(replacement))
            return false;

        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null ||
                !focused.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out object patternObject))
                return false;

            TextPattern pattern = (TextPattern)patternObject;
            TextPatternRange[] selection = pattern.GetSelection();
            if (selection == null || selection.Length == 0)
                return false;

            // Start from the actual caret and move the start endpoint backward
            // by exactly the number of characters the user typed. This avoids
            // the previous absolute-document-offset calculation.
            TextPatternRange target = selection[0].Clone();
            int moved = target.MoveEndpointByUnit(
                TextPatternRangeEndpoint.Start,
                TextUnit.Character,
                -typed.Length);

            if (moved != -typed.Length)
            {
                DiagnosticLogNative(
                    $"REPLACE_RANGE_FAIL requested={typed.Length} moved={moved}");
                return false;
            }

            string selectedBefore = target.GetText(-1) ?? string.Empty;
            if (!selectedBefore.Equals(typed, StringComparison.Ordinal))
            {
                DiagnosticLogNative(
                    $"REPLACE_VERIFY_FAIL expected=\"{typed}\" actual=\"{selectedBefore}\"");
                return false;
            }

            target.Select();
            Thread.Sleep(15);

            // Do not type unless the target editor reports that the exact
            // intended characters are selected.
            TextPatternRange[] selectedRanges = pattern.GetSelection();
            if (selectedRanges == null || selectedRanges.Length == 0)
                return false;

            string selectedAfter = selectedRanges[0].GetText(-1) ?? string.Empty;
            if (!selectedAfter.Equals(typed, StringComparison.Ordinal))
            {
                DiagnosticLogNative(
                    $"REPLACE_SELECT_FAIL expected=\"{typed}\" actual=\"{selectedAfter}\"");
                return false;
            }

            if (!SendUnicodeText(replacement))
            {
                DiagnosticLogNative(
                    $"REPLACE_INPUT_FAIL text=\"{replacement}\"");
                return false;
            }

            DiagnosticLogNative(
                $"REPLACE_OK typed=\"{typed}\" replacement=\"{replacement}\"");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogNative(
                $"REPLACE_EXCEPTION {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void DiagnosticLogNative(string message)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Deckboard");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "GreyBoard_diagnostic.log");
            File.AppendAllText(
                path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public static bool ReplacePrefixByKeyboard(int prefixLength, string replacement)
    {
        if (prefixLength <= 0 || string.IsNullOrEmpty(replacement))
            return false;

        try
        {
            // The caret is the source of truth. Select exactly prefixLength
            // characters immediately before it, then delete that selection
            // explicitly before inserting the completion. This avoids relying
            // on Unicode input to implicitly replace a selection.
            SendKeyDown(0x10); // Shift
            for (int i = 0; i < prefixLength; i++)
                SendVirtualKey(0x25); // Left
            SendKeyUp(0x10);

            System.Threading.Thread.Sleep(15);
            SendVirtualKey(0x08); // Backspace deletes the selected prefix.
            System.Threading.Thread.Sleep(10);
            SendUnicodeText(replacement);
            return true;
        }
        catch
        {
            SendKeyUp(0x10);
            return false;
        }
    }

    public static bool InsertTextAtCaret(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            if (!RestoreLastTextTargetFocus())
                return false;

            return SendUnicodeText(text);
        }
        catch
        {
            return false;
        }
    }

    private static void SendVirtualKey(ushort vk)
    {
        INPUT down = new INPUT
        {
            type = 1u,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = 0u
                }
            }
        };
        INPUT up = new INPUT
        {
            type = 1u,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = 2u
                }
            }
        };
        INPUT[] input = { down, up };
        SendInput((uint)input.Length, input, Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyDown(ushort vk)
    {
        INPUT input = new INPUT
        {
            type = 1u,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk, dwFlags = 0u }
            }
        };
        SendInput(1u, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyUp(ushort vk)
    {
        INPUT input = new INPUT
        {
            type = 1u,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk, dwFlags = 2u }
            }
        };
        SendInput(1u, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static bool SendUnicodeText(string text)
    {
        List<INPUT> input = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            input.Add(new INPUT
            {
                type = 1u,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = c,
                        dwFlags = 4u
                    }
                }
            });
            input.Add(new INPUT
            {
                type = 1u,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = c,
                        dwFlags = 6u
                    }
                }
            });
        }

        if (input.Count == 0)
            return false;

        uint sent = SendInput(
            (uint)input.Count,
            input.ToArray(),
            Marshal.SizeOf<INPUT>());

        return sent == (uint)input.Count;
    }

    public delegate nint HookProc(int nCode, nint wParam, nint lParam);

    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    private struct POINT { public int X; public int Y; }

    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    private struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fEnable;
        public nint hRgnBlur;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fTransitionOnMaximized;
    }

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 256;
    public const int WM_SYSKEYDOWN = 260;
    public const int VK_BACK = 8;
    public const int VK_ESCAPE = 27;
    public const int VK_SHIFT = 16;
    public const int VK_LEFT = 37;
    public const int VK_RIGHT = 39;
    public const int VK_TAB = 9;
    public const int VK_RETURN = 13;
    public const int VK_SPACE = 32;
    public const int LLKHF_INJECTED = 16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 128;
    public const int WS_EX_NOACTIVATE = 134217728;

    public static bool ControlDown => (GetAsyncKeyState(17) & 0x8000) != 0;
    public static bool AltDown => (GetAsyncKeyState(18) & 0x8000) != 0;
    public static bool ShiftDown => (GetAsyncKeyState(16) & 0x8000) != 0;

    [DllImport("user32.dll")]
    public static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    public static extern nint GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll")]
    public static extern nint SetWindowLong(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, nint processId);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint hWnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref DWM_BLURBEHIND blur);

    public static void SetGlass(nint hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            DWM_BLURBEHIND blur = new DWM_BLURBEHIND
            {
                dwFlags = 1u,
                fEnable = enabled
            };
            DwmEnableBlurBehindWindow(hwnd, ref blur);
        }
        catch { }
    }

    public static string? KeyToLetter(int key)
    {
        if (key < 65 || key > 90) return null;
        return ((char)key).ToString().ToLowerInvariant();
    }

    public static bool TryGetCaretScreenPosition(out Point point)
    {
        nint foreground = GetForegroundWindow();
        GUITHREADINFO info = new GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<GUITHREADINFO>()
        };

        if (foreground != IntPtr.Zero &&
            GetGUIThreadInfo(
                GetWindowThreadProcessId(foreground, IntPtr.Zero),
                ref info) &&
            info.hwndCaret != IntPtr.Zero)
        {
            POINT p = new POINT
            {
                X = info.rcCaret.Left,
                Y = info.rcCaret.Bottom
            };

            if (ClientToScreen(info.hwndCaret, ref p))
            {
                point = new Point(p.X, p.Y);
                return true;
            }
        }

        point = default;
        return false;
    }

    public static void SendBackspaces(int count)
    {
        for (int i = 0; i < count; i++)
            SendKey(8);
    }

    private static void SendKey(ushort vk)
    {
        SendInput(
            2u,
            new INPUT[2]
            {
                new INPUT
                {
                    type = 1u,
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT { wVk = vk }
                    }
                },
                new INPUT
                {
                    type = 1u,
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = vk,
                            dwFlags = 2u
                        }
                    }
                }
            },
            Marshal.SizeOf<INPUT>());
    }
}
