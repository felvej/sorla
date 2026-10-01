using System.Diagnostics;
using Sorla.Core;
using Sorla.Core.Audio;
using Sorla.Core.Transcription;
using Sorla.Platform;

namespace Sorla;

internal static class Program
{
    private const string QuitEventName = @"Local\Sorla-Windows-quit";
    private const string ShowEventName = @"Local\Sorla-Windows-show";

    [STAThread]
    private static int Main(string[] args)
    {
        // Command-line transcription of WAV files, for scripts and for checking the model without the tray app.
        if (args.Length >= 2 && args[0] == "--transcribe") return TranscribeFiles(args[1..]);
        if (args.Length == 1 && args[0] == "--mic-check") return CheckMicrophone();

        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (args.Length == 1 && args[0] == "--quit")
        {
            quit.Set();
            return 0;
        }

        using var instance = new Mutex(true, @"Local\Sorla-Windows-2f8c1d", out var isFirst);
        if (!isFirst)
        {
            // Starting Sorla again while it runs opens its window instead of a second copy.
            show.Set();
            return 0;
        }

        // --fake-mic plays a WAV file instead of the microphone, so the whole flow can be tested unattended.
        var fakeMic = Option(args, "--fake-mic");
        // Test runs only ever paste into the test window; a fake microphone without a guard gets the default one.
        var pasteGuard = Option(args, "--paste-only-into") ?? (fakeMic != null ? "TestTarget" : null);
        Func<IAudioSource> createSource = fakeMic != null ? () => new FileAudioSource(fakeMic) : () => new MicrophoneSource();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Log.Error("unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception x) Log.Error("unhandled exception", x); };

        Log.Info($"Sorla {AppVersion.Current} starting{(fakeMic != null ? " with fake microphone" : "")}");
        var app = new TrayApp(Settings.Load(), createSource, pasteGuard);
        if (args.Contains("--settings")) app.ShowSettings();
        var context = SynchronizationContext.Current!;
        var quitWait = ThreadPool.RegisterWaitForSingleObject(quit, (_, _) => context.Post(_ => app.ExitThread(), null), null, -1, executeOnlyOnce: true);
        var showWait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => context.Post(_ => app.ShowWelcome(), null), null, -1, executeOnlyOnce: false);
        Application.Run(app);
        quitWait.Unregister(null);
        showWait.Unregister(null);
        Log.Info("Sorla quit");
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    // Opens the default microphone for two seconds and reports what arrived; the audio is never kept.
    private static int CheckMicrophone()
    {
        Native.AttachConsole(-1);
        Console.WriteLine($"devices: {NAudio.Wave.WaveIn.DeviceCount}, first: {MicrophoneSource.DefaultDeviceName ?? "none"}");
        long count = 0;
        float peak = 0;
        using var source = new MicrophoneSource();
        source.Frames += frames =>
        {
            Interlocked.Add(ref count, frames.Length);
            foreach (var s in frames) peak = Math.Max(peak, Math.Abs(s));
        };
        var clock = Stopwatch.StartNew();
        try
        {
            source.Start();
        }
        catch (Exception e)
        {
            Console.WriteLine($"microphone failed to start: {e.Message}");
            return 3;
        }
        var opened = clock.ElapsedMilliseconds;
        Thread.Sleep(2000);
        source.Stop();
        Console.WriteLine($"opened in {opened} ms, {count} samples in 2 s ({count / 2.0:F0}/s), peak {peak:F4}");
        return count > 0 ? 0 : 4;
    }

    private static int TranscribeFiles(string[] files)
    {
        Native.AttachConsole(-1);
        var settings = Settings.Load();
        var directory = settings.EffectiveModelDirectory;
        if (!ModelFiles.IsInstalled(directory, settings.ModelVariant))
        {
            Console.Error.WriteLine($"Model not installed in {directory}. Start Sorla once to download it.");
            return 2;
        }
        var clock = Stopwatch.StartNew();
        using var transcriber = new PianissimoTranscriber(directory, settings.ModelVariant);
        Console.Error.WriteLine($"model loaded in {clock.ElapsedMilliseconds} ms");
        foreach (var file in files)
        {
            var audio = Wav.Read(file);
            var samples = Resampler.Resample(audio.Samples, audio.SampleRate, PianissimoTranscriber.SampleRate);
            clock.Restart();
            var text = transcriber.Transcribe(samples);
            Console.Error.WriteLine($"{Path.GetFileName(file)}: {samples.Length / 16000.0:F1} s audio in {clock.ElapsedMilliseconds} ms");
            Console.WriteLine(text);
        }
        return 0;
    }
}
