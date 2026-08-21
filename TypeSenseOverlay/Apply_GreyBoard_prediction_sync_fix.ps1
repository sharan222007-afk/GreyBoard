$ErrorActionPreference = "Stop"

# GreyBoard prediction synchronization fix
# Run this script from the TypeSenseOverlay directory.
#
# It:
#   1. Backs up the current TypingEngine.cs.
#   2. Makes the prediction shortcut refresh authoritative at shortcut-down.
#   3. Removes the redundant Native context read from SnapshotPredictionForAcceptance.
#   4. Verifies both target changes are present.
#
# It intentionally does NOT change the prediction model, UI, Native.cs,
# InputHookThread.cs, or the 30 ms normal typing debounce.

$typing = Join-Path $PSScriptRoot "TypingEngine.cs"

if (-not (Test-Path $typing)) {
    throw "TypingEngine.cs was not found at: $typing"
}

$t = Get-Content -Raw -Encoding UTF8 $typing

$oldShortcut = @'
        if (input.Kind == InputHookEventKind.PredictionShortcutDown)
        {
            if (IsPaused)
                return;

            int? choice = PredictionChoice(input.Key);
'@

$newShortcut = @'
        if (input.Kind == InputHookEventKind.PredictionShortcutDown)
        {
            if (IsPaused)
                return;

            # Normal typing is intentionally debounced by the 30 ms
            # prediction refresh timer. A prediction shortcut is an explicit
            # request for the state at the caret NOW, so make this path
            # authoritative without slowing the normal typing hot path.
            _predictionRefreshTimer.Stop();
            RefreshFromActiveCaret();

            int? choice = PredictionChoice(input.Key);
'@

if (-not $t.Contains($oldShortcut)) {
    throw "PredictionShortcutDown anchor not found. Refusing to modify the file."
}

if ($t.Contains("_predictionRefreshTimer.Stop();`r`n            RefreshFromActiveCaret();") -or
    $t.Contains("_predictionRefreshTimer.Stop();`n            RefreshFromActiveCaret();")) {
    Write-Host "Shortcut refresh fix is already present." -ForegroundColor Yellow
} else {
    $t = $t.Replace($oldShortcut, $newShortcut)
}

$oldSnapshot = @'
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

'@

if ($t.Contains($oldSnapshot)) {
    $t = $t.Replace($oldSnapshot, "")
} elseif ($t.Contains("Capture the freshest context at acceptance time")) {
    throw "The snapshot block exists but does not match the expected current source. Refusing to guess."
}

# Backup
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backup = Join-Path $PSScriptRoot "TypingEngine.cs.backup_$stamp"
Copy-Item $typing $backup -Force

# Write UTF-8 without BOM on PowerShell 7; UTF-8 on Windows PowerShell is
# acceptable for this source file as well.
Set-Content -Path $typing -Value $t -Encoding UTF8

# Basic structural checks.
$final = Get-Content -Raw -Encoding UTF8 $typing

if (-not $final.Contains("_predictionRefreshTimer.Stop();")) {
    throw "Verification failed: refresh timer stop was not written."
}

if (-not $final.Contains("RefreshFromActiveCaret();")) {
    throw "Verification failed: refresh call was not written."
}

if ($final.Contains("Capture the freshest context at acceptance time")) {
    throw "Verification failed: redundant snapshot context block remains."
}

$open = ([regex]::Matches($final, '\{')).Count
$close = ([regex]::Matches($final, '\}')).Count

if ($open -ne $close) {
    throw "Brace-count check failed: {$open} opening vs {$close} closing braces."
}

Write-Host ""
Write-Host "GreyBoard prediction synchronization fix applied." -ForegroundColor Green
Write-Host "Backup: $backup"
Write-Host ""
Write-Host "Next: Clean Solution -> Rebuild Solution in Visual Studio."
Write-Host "Then test continuous typing followed immediately by Ctrl+Alt+1/2/3."
