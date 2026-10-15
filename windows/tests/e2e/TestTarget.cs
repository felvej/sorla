#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property OutputType=WinExe
#:property AllowUnsafeBlocks=true
#:property PublishAot=false

// End-to-end test target: a window with a text box that drives Sorla with real (injected) key presses and records
// what lands in the box. Run by run-e2e.ps1 while Sorla runs with --fake-mic.
using System.Runtime.InteropServices;
using System.Text.Json;

// The clipboard needs an STA thread, which top-level statements don't get.
var staThread = new Thread(() => Run(args[0]));
staThread.SetApartmentState(ApartmentState.STA);
staThread.Start();
staThread.Join();

static void Run(string resultPath)
{
var results = new Dictionary<string, object?>();
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
var form = new Form { Text = "Sorla E2E target", Width = 700, Height = 300, StartPosition = FormStartPosition.CenterScreen, TopMost = true };
var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12) };
form.Controls.Add(box);

form.Shown += async (_, _) =>
{
    try
    {
        await Focus();
        results["focused"] = Native.GetForegroundWindow() == form.Handle;

        // 1. Hold Right Ctrl while the fake microphone "speaks"; the text should be pasted here and the clipboard put back.
        Clipboard.SetText("SORLA-ORIGINAL-CLIPBOARD");
        Key(0xA3, down: true);
        var focusLog = new List<string>();
        for (var t = 0; t < 30; t++)
        {
            await Task.Delay(250);
            var name = Native.ProcessOf(Native.GetForegroundWindow());
            if (focusLog.Count == 0 || !focusLog[^1].EndsWith(name)) focusLog.Add($"{t * 250}ms {name}");
        }
        Key(0xA3, down: false);
        results["focusDuringHold"] = focusLog;
        results["dictation"] = await WaitForText(TimeSpan.FromSeconds(40));
        await Task.Delay(1200);
        results["clipboardAfter"] = Clipboard.ContainsText() ? Clipboard.GetText() : null;

        // 2. Paste Last with Ctrl+Alt+V.
        box.Clear();
        await Focus();
        Key(0xA2, true); Key(0xA4, true); Key(0x56, true); Key(0x56, false); Key(0xA4, false); Key(0xA2, false);
        results["pasteLast"] = await WaitForText(TimeSpan.FromSeconds(5));
        await Task.Delay(1200);

        // 3. Esc while holding calls the dictation off: nothing may be pasted.
        box.Clear();
        await Focus();
        Key(0xA3, true);
        await Task.Delay(2000);
        Key(0x1B, true); Key(0x1B, false);
        await Task.Delay(300);
        Key(0xA3, false);
        await Task.Delay(8000);
        results["escape"] = box.Text;

        // 4. Right Ctrl + A is a shortcut, not a dictation.
        box.Text = "";
        await Focus();
        Key(0xA3, true);
        await Task.Delay(100);
        Key(0x41, true); Key(0x41, false);
        await Task.Delay(1500);
        Key(0xA3, false);
        await Task.Delay(8000);
        results["shortcut"] = box.Text;
    }
    catch (Exception e)
    {
        results["error"] = e.ToString();
        // Never leave a modifier held down after a stopped run.
        foreach (var vk in new[] { 0xA3, 0xA2, 0xA4 }) Key(vk, down: false);
    }
    File.WriteAllText(resultPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    form.Close();
};
Application.Run(form);

async Task Focus()
{
    // Attaching to the foreground thread lets this window take focus without injecting any key.
    for (var attempt = 0; attempt < 10 && Native.GetForegroundWindow() != form.Handle; attempt++)
    {
        var foreground = Native.GetForegroundWindow();
        var foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
        var ownThread = Native.GetCurrentThreadId();
        Native.AttachThreadInput(ownThread, foregroundThread, true);
        Native.BringWindowToTop(form.Handle);
        Native.SetForegroundWindow(form.Handle);
        Native.AttachThreadInput(ownThread, foregroundThread, false);
        await Task.Delay(200);
    }
    box.Focus();
    await Task.Delay(300);
    EnsureFocus();
}

// Every injected key first checks that it goes to this window; otherwise the run stops before touching another app.
void EnsureFocus()
{
    if (Native.GetForegroundWindow() != form.Handle) throw new InvalidOperationException("Test window lost focus; stopped before sending keys elsewhere.");
}

async Task<string> WaitForText(TimeSpan timeout)
{
    var until = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < until && box.Text.Length == 0) await Task.Delay(100);
    // Let a paste in progress finish.
    await Task.Delay(300);
    return box.Text;
}

void Key(int vk, bool down)
{
    if (down) EnsureFocus();
    var extended = vk is 0xA3 or 0xA5;
    var input = new Native.INPUT
    {
        type = 1,
        ki = new Native.KEYBDINPUT
        {
            wVk = (ushort)vk,
            wScan = (ushort)Native.MapVirtualKeyW((uint)vk, 0),
            dwFlags = (down ? 0u : 2u) | (extended ? 1u : 0u),
        },
    };
    Native.SendInput(1, [input], Marshal.SizeOf<Native.INPUT>());
}
}

static partial class Native
{
    public static string ProcessOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        try { return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { return "?"; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [LibraryImport("user32.dll")]
    public static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    public static partial uint MapVirtualKeyW(uint uCode, uint uMapType);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint hWnd);
}
