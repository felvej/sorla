using System.Runtime.InteropServices;
using System.Text;

namespace Sorla.Platform;

// Delivers text by way of the clipboard and a synthetic Ctrl+V, then puts back what the user had copied.
public static class ClipboardPaster
{
    // Formats that keep dictated text out of Win+V history and cloud clipboard sync.
    private const string ExcludeFromMonitoring = "ExcludeClipboardContentFromMonitorProcessing";
    private const string CanIncludeInHistory = "CanIncludeInClipboardHistory";
    private const string CanUploadToCloud = "CanUploadToCloudClipboard";

    public static uint Sequence => Native.GetClipboardSequenceNumber();

    public static IDataObject? Snapshot()
    {
        try
        {
            var current = Clipboard.GetDataObject();
            if (current == null) return null;
            var copy = new DataObject();
            foreach (var format in current.GetFormats(false))
            {
                try
                {
                    var data = current.GetData(format, false);
                    if (data != null) copy.SetData(format, false, data);
                }
                catch (Exception e) when (e is ExternalException or OutOfMemoryException or InvalidOperationException or NotSupportedException)
                {
                    // Some formats (delayed-rendered or private ones) cannot be read back; the rest is still kept.
                }
            }
            return copy.GetFormats().Length == 0 ? null : copy;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    // Returns the clipboard sequence number after the write.
    public static uint Write(string text, bool privateToSorla = true)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        if (privateToSorla)
        {
            data.SetData(ExcludeFromMonitoring, false, new MemoryStream(Encoding.ASCII.GetBytes("1")));
            data.SetData(CanIncludeInHistory, false, new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData(CanUploadToCloud, false, new MemoryStream(BitConverter.GetBytes(0)));
        }
        Retry(() => Clipboard.SetDataObject(data, copy: true, retryTimes: 10, retryDelay: 50));
        return Sequence;
    }

    public static void Restore(IDataObject? snapshot)
    {
        Retry(() =>
        {
            if (snapshot == null) Clipboard.Clear();
            else Clipboard.SetDataObject(snapshot, copy: true, retryTimes: 10, retryDelay: 50);
        });
    }

    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (ExternalException) when (attempt < 3)
            {
                Thread.Sleep(50);
            }
        }
    }

    // Alt, Shift or Win still held would turn Ctrl+V into another shortcut, so wait (up to a second) for them to go.
    public static async Task WaitForModifiersReleasedAsync(int ignoredVirtualKey = 0)
    {
        int[] keys = [Native.VK_MENU, Native.VK_SHIFT, Native.VK_LWIN, Native.VK_RWIN, 0xA3, 0xA2];
        for (var waited = 0; waited < 1000; waited += 20)
        {
            if (!keys.Any(k => k != ignoredVirtualKey && Native.IsKeyDown(k))) return;
            await Task.Delay(20);
        }
    }

    // Sends Ctrl+V to the window that has focus. Returns false if Windows refused the input (UIPI: an elevated window).
    public static bool SendPaste()
    {
        Native.INPUT Key(int vk, bool up) => new()
        {
            type = Native.INPUT_KEYBOARD,
            u = new Native.InputUnion
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    dwFlags = up ? Native.KEYEVENTF_KEYUP : 0,
                    dwExtraInfo = InputHook.SyntheticMarker,
                },
            },
        };
        Native.INPUT[] inputs =
        [
            Key(Native.VK_CONTROL, false),
            Key(Native.VK_V, false),
            Key(Native.VK_V, true),
            Key(Native.VK_CONTROL, true),
        ];
        var sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
        return sent == inputs.Length;
    }
}
