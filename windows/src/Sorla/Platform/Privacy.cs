using System.Runtime.InteropServices;
using Sorla.Core.Transcription;

namespace Sorla.Platform;

// Everything Sorla does at launch so that nothing about a dictation can leave the PC by way of Windows.
public static partial class Privacy
{
    public static void Apply()
    {
        // A crash dump holds whatever was in memory (audio, text), and Windows Error Reporting may upload it to
        // Microsoft. This keeps Sorla's own crashes out of it, for this user only; no administrator rights needed.
        try
        {
            if (Environment.ProcessPath is { } path) WerAddExcludedApplication(path, false);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        PianissimoTranscriber.DisableTelemetry();
    }

    [LibraryImport("wer.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WerAddExcludedApplication(string pwzExeName, [MarshalAs(UnmanagedType.Bool)] bool bAllUsers);
}
