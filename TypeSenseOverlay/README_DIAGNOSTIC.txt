Grey Board implementation notes
================================

This build keeps the existing TypeSenseOverlay / Deckboard project structure.

Added in this build
--------------------
- Local-first candidate prediction remains the fast path.
- Optional asynchronous Ollama autocomplete fills gaps when local candidates are weak.
- AI Enhance is available with Ctrl+Alt+E by default after selecting text.
- Enhance modes: Enhance, Fix, Rewrite, Shorten, Expand, Formal, Casual.
- Enhance uses a review window so text is never silently replaced by the model.
- The original text selection is captured before the review window opens, so Apply can replace the correct editor selection.
- Ollama calls are asynchronous, cancellable and fail closed if Ollama/model is unavailable.
- New diagnostics are written to %LOCALAPPDATA%\Deckboard\GreyBoard_diagnostic.log.

Ollama
------
Default endpoint: http://127.0.0.1:11434
Default model: qwen2.5:3b

The model must be installed locally in Ollama. Grey Board does not require a cloud subscription for these local calls.
If a different local model is installed, change the model name in Grey Board Settings.

Main shortcut
-------------
Ctrl+Alt+E -> AI Enhance selected text

Important implementation choice
--------------------------------
The low-level keyboard hook remains the current input mechanism. AI work never runs directly on that hook thread. UI Automation remains the text-context/replacement mechanism already used by the project.

Build note
----------
The provided environment did not have the .NET SDK installed, so a local dotnet build could not be executed here. The source was inspected and patched in-place; brace/syntax-structure sanity checks passed. Build on Windows with the project's configured .NET 10 Windows SDK before deployment.
