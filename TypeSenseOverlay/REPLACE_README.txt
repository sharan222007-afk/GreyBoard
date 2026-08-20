GreyBoard Local Prediction - FIXED replacement files

Replace:
- TypingEngine.cs
- LanguageProfile.cs
- Native.cs
- UserSettings.cs
- Program.cs
- SettingsWindow.cs

Important compile fix:
TypingEngine.cs now exposes the public Start() method expected by ControlCenter.
The existing constructor-start behavior is preserved, while Start() is also idempotent
and supports restarting after Stop().

Do NOT restore OllamaSuggestionService.cs, AIEnhanceService.cs, or EnhanceOverlay.cs.
