using System;
using System.Windows;

namespace TypeSenseOverlay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        UserSettings settings = UserSettings.Load();
        LanguageProfile profile = LanguageProfile.Load();
        SuggestionOverlay overlay = new SuggestionOverlay(settings);
        EnhanceOverlay enhanceOverlay = new EnhanceOverlay(settings);
        TypingEngine engine = new TypingEngine(profile, settings, overlay, enhanceOverlay);
        ControlCenter controlCenter = new ControlCenter(settings, engine);
        using (new TrayMenu(engine, overlay, settings, controlCenter))
        {
            controlCenter.Closed += delegate
            {
                engine.Dispose();
            };
            Application application = new Application();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            application.Exit += delegate
            {
                engine.Dispose();
            };
            application.Run(controlCenter);
        }
    }
}
