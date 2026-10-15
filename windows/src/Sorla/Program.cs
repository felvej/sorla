using System.Diagnostics;
using Sorla.Core;
using Sorla.Core.Audio;
using Sorla.Core.Transcription;
using Sorla.Platform;

namespace Sorla;

internal static class Program
{
    // A separate data folder (SORLA_DATA_DIR, used by tests) is a separate instance, so a test never reaches the
    // Sorla the user is running.
    private static readonly string InstanceSuffix = Environment.GetEnvironmentVariable("SORLA_DATA_DIR") is { Length: > 0 } dir
        ? "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dir.ToLowerInvariant())))[..12]
        : "";
    private static readonly string QuitEventName = $@"Local\Sorla-Windows-quit{InstanceSuffix}";
    private static readonly string ShowEventName = $@"Local\Sorla-Windows-show{InstanceSuffix}";

    [STAThread]
    private static int Main(string[] args)
    {
        Privacy.Apply();
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

        using var instance = new Mutex(true, $@"Local\Sorla-Windows-2f8c1d{InstanceSuffix}", out var isFirst);
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
        Func<string?, IAudioSource> createSource = fakeMic != null ? _ => new FileAudioSource(fakeMic) : id => new MicrophoneSource(id);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Log.Error("unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception x) Log.Error("unhandled exception", x); };

        Log.Info($"Sorla {AppVersion.Current} starting{(fakeMic != null ? " with fake microphone" : "")}");
        var app = new TrayApp(Settings.Load(), createSource, pasteGuard);
        if (args.Contains("--settings")) app.ShowSettings();
        if (fakeMic != null && Option(args, "--auto-dictate") is { } seconds) app.AutoDictate(TimeSpan.FromSeconds(double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture)));
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

    // Opens each microphone and reports how long until audio arrives, as Sorla would open it
    // cold and then warm. The audio is never kept; only counts and levels are printed.
    private static int CheckMicrophone()
    {
        Native.AttachConsole(-1);
        var settings = Settings.Load();
        var devices = MicrophoneSource.Devices();
        Console.WriteLine($"default: {MicrophoneSource.DefaultDeviceName ?? "none"}; chosen in settings: {(settings.MicrophoneId.Length == 0 ? "Windows default" : MicrophoneSource.Resolve(settings.MicrophoneId)?.Name ?? "missing")}");
        var result = 4;
        foreach (var device in devices)
        {
            using var microphone = new MicrophoneService(() => new MicrophoneSource(device.Id)) { KeepWarm = Timeout.InfiniteTimeSpan };
            foreach (var attempt in new[] { "cold", "warm" })
            {
                var clock = Stopwatch.StartNew();
                var capture = microphone.Begin();
                using var ready = new ManualResetEventSlim(capture.IsReady);
                capture.Ready += _ => ready.Set();
                capture.Failed += _ => ready.Set();
                ready.Wait(TimeSpan.FromSeconds(5));
                var readyAfter = clock.ElapsedMilliseconds;
                Thread.Sleep(1000);
                var (samples, peak) = microphone.End(capture);
                var status = capture.Failure != null ? $"failed: {capture.Failure.Message}" : capture.IsReady ? $"ready after {readyAfter} ms" : "no audio within 5 s";
                Console.WriteLine($"{device.Name}, {attempt}: {status}, {samples.Length} samples, peak {peak:F3}");
                if (capture.IsReady) result = 0;
            }
        }
        return result;
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
