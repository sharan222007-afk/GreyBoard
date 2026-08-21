GreyBoard Input System v11 — dedicated low-latency hook

REPLACE:
- TypeSenseOverlay/TypingEngine.cs
- TypeSenseOverlay/Native.cs
- ADD TypeSenseOverlay/InputHookThread.cs
- AdvancedShortcutController.cs is included unchanged for consistency.

Architecture:
- WH_KEYBOARD_LL runs on a dedicated GreyBoard.InputHook thread.
- Hook callback only reads the event, updates tiny modifier/gesture state, makes consume/pass decisions, and posts a tiny command.
- UI Automation, prediction, overlay rendering, autocorrection, and replacement remain on the WPF thread.
- Normal Shift/Ctrl/Alt releases are passed through. GreyBoard consumes only events it owns.
- Advanced Shift+Alt cycle/commit state is isolated to the input thread.
- Classic prediction shortcuts queue key-down/up and wait for modifier release without blocking Windows input.
- No UI Automation or WPF prediction work exists in the low-level hook callback.

Verification performed here:
- source delimiter/balance checks passed
- old TypingEngine HookCallback removed
- no UI Automation/replacement calls in InputHookThread.cs
- message-loop APIs and dedicated-thread hook wiring present
- duplicate modifier update removed

Limitation: this environment has no Windows .NET SDK/runtime, so a real Windows build and WhatsApp runtime test could not be executed here. Rebuild in Visual Studio before use.
