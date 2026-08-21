using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace TypeSenseOverlay;

internal enum InputHookEventKind
{
    NormalKeyDown,
    Backspace,
    Boundary,
    PredictionShortcutDown,
    PredictionShortcutUp,
    PredictionShortcutCommitReady,
    AdvancedCycle,
    AdvancedCommit,
    TogglePause
}

internal readonly struct InputHookEvent
{
    public readonly InputHookEventKind Kind;
    public readonly int Key;
    public readonly bool ControlDown;
    public readonly bool AltDown;
    public readonly bool ShiftDown;

    public InputHookEvent(
        InputHookEventKind kind,
        int key,
        bool controlDown,
        bool altDown,
        bool shiftDown)
    {
        Kind = kind;
        Key = key;
        ControlDown = controlDown;
        AltDown = altDown;
        ShiftDown = shiftDown;
    }
}

/// <summary>
/// Owns the global low-level keyboard hook on a dedicated thread.
/// The hook callback performs only tiny state transitions and interception.
/// WPF/prediction/UI Automation work is queued away from the hook.
/// </summary>
internal sealed class InputHookThread : IDisposable
{
    private const int WM_QUIT = 0x0012;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly UserSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly Action<InputHookEvent> _sink;
    private readonly object _lifecycleLock = new object();
    private readonly object _stateLock = new object();

    private readonly AdvancedShortcutController _advancedShortcut =
        new AdvancedShortcutController();

    // Coalesce dispatcher work. Rapid typing must not create one WPF callback
    // per physical key.
    private readonly ConcurrentQueue<InputHookEvent> _eventQueue =
        new ConcurrentQueue<InputHookEvent>();

    private int _drainScheduled;

    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private Native.HookProc? _hookCallback;

    private readonly ManualResetEventSlim _started =
        new ManualResetEventSlim(false);

    private volatile bool _stopRequested;
    private volatile bool _paused;
    private volatile bool _advancedEnabled;
    private volatile bool _startFailed;

    private bool _controlDown;
    private bool _altDown;
    private bool _shiftDown;

    private int _consumedPredictionKey = -1;
    private bool _predictionShortcutAwaitingModifiers;

    public InputHookThread(
        UserSettings settings,
        Dispatcher dispatcher,
        Action<InputHookEvent> sink)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _sink = sink;

        Configure(
            settings.AdvancedKey1,
            settings.AdvancedKey2,
            settings.ShortcutMode);
    }

    public void Configure(
        string key1,
        string key2,
        string shortcutMode)
    {
        lock (_stateLock)
        {
            _advancedShortcut.Configure(key1, key2);

            _advancedEnabled =
                string.Equals(
                    shortcutMode,
                    "Advanced",
                    StringComparison.OrdinalIgnoreCase);

            _advancedShortcut.Enabled = _advancedEnabled;
            ResetPhysicalStateUnsafe();
        }

        // Configuration changes invalidate queued old control events.
        while (_eventQueue.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _drainScheduled, 0);
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;

        if (paused)
        {
            lock (_stateLock)
            {
                ResetPhysicalStateUnsafe();
            }

            while (_eventQueue.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _drainScheduled, 0);
        }
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_thread != null)
                return;

            _stopRequested = false;
            _startFailed = false;
            _started.Reset();

            _thread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "GreyBoard.KeyboardHook"
            };

            _thread.Start();
        }

        if (!_started.Wait(TimeSpan.FromSeconds(2)) || _startFailed)
            throw new InvalidOperationException(
                "GreyBoard keyboard input thread could not start.");
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;

        lock (_lifecycleLock)
        {
            thread = _thread;
            threadId = _threadId;
            _stopRequested = true;
        }

        if (thread == null)
            return;

        if (threadId != 0)
        {
            Native.PostThreadMessage(
                threadId,
                WM_QUIT,
                IntPtr.Zero,
                IntPtr.Zero);
        }

        if (Thread.CurrentThread != thread)
            thread.Join(1500);

        lock (_lifecycleLock)
        {
            _thread = null;
            _threadId = 0;
            _hook = IntPtr.Zero;
        }

        while (_eventQueue.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _drainScheduled, 0);
        ResetPhysicalState();
    }

    private void HookThreadMain()
    {
        _threadId = Native.GetCurrentThreadId();
        _hookCallback = HookCallback;

        try
        {
            nint module = Native.GetModuleHandle(null);

            _hook = Native.SetWindowsHookEx(
                Native.WH_KEYBOARD_LL,
                _hookCallback,
                module,
                0u);

            if (_hook == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Windows could not install the GreyBoard keyboard hook.");

            // Force creation of the thread message queue.
            Native.PeekMessage(
                out _,
                IntPtr.Zero,
                0u,
                0u,
                0u);

            _started.Set();

            while (!_stopRequested)
            {
                int result = Native.GetMessage(
                    out Native.MSG message,
                    IntPtr.Zero,
                    0u,
                    0u);

                if (result <= 0)
                    break;

                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            _startFailed = true;
            _started.Set();
            Debug.WriteLine(
                "GreyBoard input thread failed: " + ex);
        }
        finally
        {
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            ResetPhysicalState();
        }
    }

    private nint HookCallback(
        int code,
        nint wParam,
        nint lParam)
    {
        if (code < 0)
            return Native.CallNextHookEx(
                _hook,
                code,
                wParam,
                lParam);

        bool isKeyDown =
            wParam == WM_KEYDOWN ||
            wParam == WM_SYSKEYDOWN;

        bool isKeyUp =
            wParam == WM_KEYUP ||
            wParam == WM_SYSKEYUP;

        if (!isKeyDown && !isKeyUp)
            return Native.CallNextHookEx(
                _hook,
                code,
                wParam,
                lParam);

        Native.KBDLLHOOKSTRUCT data =
            Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);

        if ((data.flags & Native.LLKHF_INJECTED) != 0)
            return Native.CallNextHookEx(
                _hook,
                code,
                wParam,
                lParam);

        int key = (int)data.vkCode;

        // This is intentionally the only state work done for modifiers.
        UpdateModifierState(
            key,
            isKeyDown,
            isKeyUp);

        bool control = _controlDown;
        bool alt = _altDown;
        bool shift = _shiftDown;

        // Advanced gesture handling stays entirely on this dedicated hook
        // thread. Only the gesture state machine runs here.
        if (_advancedEnabled)
        {
            lock (_stateLock)
            {
                if (isKeyDown)
                {
                    AdvancedShortcutController.Action action =
                        _advancedShortcut.HandleKeyDown(key);

                    if (action ==
                        AdvancedShortcutController.Action.CycleNext)
                    {
                        Post(
                            new InputHookEvent(
                                InputHookEventKind.AdvancedCycle,
                                key,
                                control,
                                alt,
                                shift));

                        // Key 2 (normally Alt) was consumed on down.
                        return 1;
                    }
                }
                else
                {
                    bool wasKey2 =
                        _advancedShortcut.IsKey2(key);

                    bool key2WasDown =
                        _advancedShortcut.IsKey2Down;

                    AdvancedShortcutController.Action action =
                        _advancedShortcut.HandleKeyUp(key);

                    if (wasKey2 && key2WasDown)
                    {
                        // Match the consumed Key 2 down.
                        return 1;
                    }

                    if (_advancedShortcut.IsKey1(key) &&
                        action ==
                        AdvancedShortcutController.Action.Commit)
                    {
                        Post(
                            new InputHookEvent(
                                InputHookEventKind.AdvancedCommit,
                                key,
                                control,
                                alt,
                                shift));

                        // IMPORTANT:
                        // Do not consume Key 1 / Shift UP.
                        // Windows must receive the physical release.
                    }
                }
            }
        }

        // Pause is handled without waiting for the WPF thread.
        if (isKeyDown &&
            Shortcut.Matches(
                _settings.PauseShortcut,
                key,
                control,
                alt,
                shift))
        {
            Post(
                new InputHookEvent(
                    InputHookEventKind.TogglePause,
                    key,
                    control,
                    alt,
                    shift));

            return 1;
        }

        if (_paused)
        {
            return Native.CallNextHookEx(
                _hook,
                code,
                wParam,
                lParam);
        }

        if (isKeyDown &&
            (Shortcut.Matches(
                 _settings.AcceptFirst,
                 key,
                 control,
                 alt,
                 shift) ||
             Shortcut.Matches(
                 _settings.AcceptSecond,
                 key,
                 control,
                 alt,
                 shift) ||
             Shortcut.Matches(
                 _settings.AcceptThird,
                 key,
                 control,
                 alt,
                 shift)))
        {
            _consumedPredictionKey = key;

            Post(
                new InputHookEvent(
                    InputHookEventKind.PredictionShortcutDown,
                    key,
                    control,
                    alt,
                    shift));

            return 1;
        }

        if (isKeyUp &&
            key == _consumedPredictionKey)
        {
            _consumedPredictionKey = -1;

            _predictionShortcutAwaitingModifiers =
                control || alt || shift;

            Post(
                new InputHookEvent(
                    InputHookEventKind.PredictionShortcutUp,
                    key,
                    control,
                    alt,
                    shift));

            return 1;
        }

        if (isKeyUp &&
            _predictionShortcutAwaitingModifiers &&
            IsModifierKey(key))
        {
            if (!control && !alt && !shift)
            {
                _predictionShortcutAwaitingModifiers = false;

                Post(
                    new InputHookEvent(
                        InputHookEventKind.PredictionShortcutCommitReady,
                        key,
                        control,
                        alt,
                        shift));
            }
        }

        if (!isKeyDown)
        {
            // Normal modifier releases always reach Windows.
            return Native.CallNextHookEx(
                _hook,
                code,
                wParam,
                lParam);
        }

        if (key == Native.VK_BACK)
        {
            Post(
                new InputHookEvent(
                    InputHookEventKind.Backspace,
                    key,
                    control,
                    alt,
                    shift));

            return Native.CallNextHookEx(
                _hook,
                code,
                wParam,
                lParam);
        }

        if (IsBoundary(key))
        {
            Post(
                new InputHookEvent(
                    InputHookEventKind.Boundary,
                    key,
                    control,
                    alt,
                    shift));

            return Native.CallNextHookEx(
                _hook,
                code,
                wParam,
                lParam);
        }

        if (!IsModifierKey(key))
        {
            Post(
                new InputHookEvent(
                    InputHookEventKind.NormalKeyDown,
                    key,
                    control,
                    alt,
                    shift));
        }

        return Native.CallNextHookEx(
            _hook,
            code,
            wParam,
            lParam);
    }

    private void Post(InputHookEvent input)
    {
        _eventQueue.Enqueue(input);

        // One WPF dispatcher item per burst instead of one item per key.
        if (Interlocked.Exchange(
                ref _drainScheduled,
                1) != 0)
        {
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(DrainQueuedEvents));
        }
        catch
        {
            Interlocked.Exchange(
                ref _drainScheduled,
                0);

            while (_eventQueue.TryDequeue(out _))
            {
            }
        }
    }

    private void DrainQueuedEvents()
    {
        try
        {
            while (_eventQueue.TryDequeue(
                out InputHookEvent input))
            {
                _sink(input);
            }
        }
        finally
        {
            Interlocked.Exchange(
                ref _drainScheduled,
                0);

            // If input arrived while the drain was finishing, schedule one
            // more drain. Still only one dispatcher item is outstanding.
            if (!_eventQueue.IsEmpty &&
                Interlocked.Exchange(
                    ref _drainScheduled,
                    1) == 0)
            {
                try
                {
                    _dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        new Action(DrainQueuedEvents));
                }
                catch
                {
                    Interlocked.Exchange(
                        ref _drainScheduled,
                        0);

                    while (_eventQueue.TryDequeue(out _))
                    {
                    }
                }
            }
        }
    }

    private void UpdateModifierState(
        int key,
        bool down,
        bool up)
    {
        if (!down && !up)
            return;

        bool state = down;

        switch (key)
        {
            case 17:
            case 162:
            case 163:
                _controlDown = state;
                break;

            case 18:
            case 164:
            case 165:
                _altDown = state;
                break;

            case 16:
            case 160:
            case 161:
                _shiftDown = state;
                break;
        }
    }

    private void ResetPhysicalState()
    {
        lock (_stateLock)
        {
            ResetPhysicalStateUnsafe();
        }
    }

    private void ResetPhysicalStateUnsafe()
    {
        _controlDown = false;
        _altDown = false;
        _shiftDown = false;
        _consumedPredictionKey = -1;
        _predictionShortcutAwaitingModifiers = false;
        _advancedShortcut.Reset();
    }

    private static bool IsBoundary(int key)
    {
        return key == Native.VK_SPACE ||
               key == Native.VK_RETURN ||
               key == 186 ||
               key == 188 ||
               key == 190 ||
               key == 191;
    }

    private static bool IsModifierKey(int key)
    {
        return key == 16 ||
               key == 17 ||
               key == 18 ||
               key == 160 ||
               key == 161 ||
               key == 162 ||
               key == 163 ||
               key == 164 ||
               key == 165;
    }

    public void Dispose()
    {
        Stop();
    }
}
