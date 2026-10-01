using System.Diagnostics;
using Microsoft.Win32;

namespace Sorla.Platform;

public static class LoginItem
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Sorla";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    // HKCU only, so no administrator rights are needed.
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

public static class SettingsPages
{
    public static void Microphone() => Open("ms-settings:privacy-microphone");
    public static void Sound() => Open("ms-settings:sound");

    public static void Folder(string path)
    {
        Directory.CreateDirectory(path);
        Open(path);
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Error($"could not open {target}", e);
        }
    }
}

// Ctrl+Alt+V for Paste Last, through RegisterHotKey so no other key is ever touched.
public sealed class HotKeyWindow : NativeWindow, IDisposable
{
    private const int PasteLastId = 1;
    public event Action? PasteLast;
    public bool IsRegistered { get; }

    public HotKeyWindow()
    {
        CreateHandle(new CreateParams());
        IsRegistered = Native.RegisterHotKey(Handle, PasteLastId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_V);
        if (!IsRegistered) Log.Info("Ctrl+Alt+V is taken by another app; Paste Last is only in the menu");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && (int)m.WParam == PasteLastId) PasteLast?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Native.UnregisterHotKey(Handle, PasteLastId);
        DestroyHandle();
    }
}
