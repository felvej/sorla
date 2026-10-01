using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sorla.Platform;

public enum InputKind
{
    TriggerDown,
    TriggerUp,
    OtherKeyDown,
    Click,
    Escape,
}

// Low-level keyboard and mouse hooks on their own thread, so a busy UI thread can never slow down typing or make
// Windows drop the hook. Nothing is swallowed: the trigger key still reaches the app, so Ctrl+C with Right Ctrl keeps
// working (the gesture sees the C and calls the dictation off).
public sealed class InputHook : IDisposable
{
    // Tags the Ctrl+V Sorla sends, so its own keys are never read as the user's.
    public const nuint SyntheticMarker = 0x536F726C;

    private readonly Native.HookProc keyboardProc;
    private readonly Native.HookProc mouseProc;
    private readonly SynchronizationContext context;
    private readonly Thread thread;
    private readonly ManualResetEventSlim installed = new();
    private uint threadId;
    private nint keyboardHook;
    private nint mouseHook;
    private Exception? installError;
    private volatile int triggerVirtualKey;
    private volatile bool triggerIsDown;

    public int TriggerVirtualKey
    {
        get => triggerVirtualKey;
        set => triggerVirtualKey = value;
    }

    // Raised on the UI thread with the event and its time in seconds.
    public event Action<InputKind, double>? Input;

    public InputHook(int triggerVirtualKey)
    {
        this.triggerVirtualKey = triggerVirtualKey;
        context = SynchronizationContext.Current ?? throw new InvalidOperationException("InputHook needs a UI thread.");
        keyboardProc = OnKeyboard;
        mouseProc = OnMouse;
        thread = new Thread(Run) { IsBackground = true, Name = "Sorla input hook", Priority = ThreadPriority.AboveNormal };
    }

    public void Install()
    {
        thread.Start();
        installed.Wait();
        if (installError != null) throw installError;
    }

    private void Run()
    {
        threadId = Native.GetCurrentThreadId();
        var module = Native.GetModuleHandleW(null);
        keyboardHook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, keyboardProc, module, 0);
        if (keyboardHook == 0) installError = new InvalidOperationException($"SetWindowsHookEx (keyboard) failed: {Marshal.GetLastWin32Error()}");
        mouseHook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, mouseProc, module, 0);
        installed.Set();
        if (installError != null) return;

        // Low-level hooks are called through this thread's message loop.
        while (Native.GetMessageW(out var message, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(ref message);
            Native.DispatchMessageW(ref message);
        }
        Native.UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
    }

    // A release Windows never delivered (a UAC prompt, a locked screen) would leave the trigger stuck down.
    public bool IsTriggerPhysicallyDown => Native.IsKeyDown(TriggerVirtualKey);

    public void ForgetTriggerState() => triggerIsDown = false;

    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            var message = (int)wParam;
            var isDown = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            var isUp = message is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            var vk = (int)data.vkCode;

            // AltGr arrives as a fake Left Ctrl (scan code 0x21D) ahead of Right Alt; it is part of the trigger, not a shortcut.
            var isAltGrCtrl = vk == Native.VK_LCONTROL && (data.scanCode & 0x200) != 0;

            if (data.dwExtraInfo != SyntheticMarker && !isAltGrCtrl)
            {
                if (vk == triggerVirtualKey)
                {
                    if (isDown && !triggerIsDown)
                    {
                        triggerIsDown = true;
                        Post(InputKind.TriggerDown);
                    }
                    else if (isUp && triggerIsDown)
                    {
                        triggerIsDown = false;
                        Post(InputKind.TriggerUp);
                    }
                }
                else if (isDown)
                {
                    Post(vk == Native.VK_ESCAPE ? InputKind.Escape : InputKind.OtherKeyDown);
                }
            }
        }
        return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (int)wParam is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN)
        {
            Post(InputKind.Click);
        }
        return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private void Post(InputKind kind)
    {
        var at = Now;
        context.Post(_ => Input?.Invoke(kind, at), null);
    }

    public void Dispose()
    {
        if (threadId != 0) Native.PostThreadMessageW(threadId, Native.WM_QUIT, 0, 0);
        thread.Join(1000);
        installed.Dispose();
    }
}
