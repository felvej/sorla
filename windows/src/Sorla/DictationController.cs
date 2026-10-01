using System.Diagnostics;
using Sorla.Core;
using Sorla.Core.Audio;
using Sorla.Core.Dictation;
using Sorla.Core.Transcription;
using Sorla.Platform;
using Sorla.UI;

namespace Sorla;

public enum Issue
{
    NoMicrophone,
    Silent,
    ModelMissing,
    ModelLoading,
    ModelFailed,
    TranscriptionFailed,
}

// Record while the key is held, transcribe on release, paste where the cursor is. Runs on the UI thread;
// audio arrives on a background thread and is handed over under a lock.
public sealed class DictationController : IDisposable
{
    private static readonly TimeSpan TailDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan PasteSettleTime = TimeSpan.FromMilliseconds(500);

    private readonly Settings settings;
    private readonly ModelHost model;
    private readonly Func<IAudioSource> createSource;
    private readonly RecordingIndicator indicator;
    private readonly Feedback feedback;
    private readonly SynchronizationContext context;
    private readonly RecentTranscript recent = new();
    private readonly System.Windows.Forms.Timer limitTimer = new() { Interval = 250 };
    private readonly SpectrumAnalyzer analyzer = new(PianissimoTranscriber.SampleRate);

    private Capture? capture;
    private Stopwatch? recordingClock;
    private bool limitWarned;
    private Issue? pendingIssue;
    private Task deliveries = Task.CompletedTask;
    private int pendingJobs;
    private bool pasteLastInFlight;

    public bool IsRecording { get; private set; }
    public bool IsBusy => IsRecording || pendingJobs > 0;
    public event Action? StateChanged;
    // Raised after the key is released, so a dialog never steals focus mid-dictation.
    public event Action<Issue>? IssueRaised;

    public DictationController(Settings settings, ModelHost model, Func<IAudioSource> createSource, RecordingIndicator indicator, Feedback feedback)
    {
        this.settings = settings;
        this.model = model;
        this.createSource = createSource;
        this.indicator = indicator;
        this.feedback = feedback;
        context = SynchronizationContext.Current ?? throw new InvalidOperationException("DictationController needs a UI thread.");
        limitTimer.Tick += (_, _) => CheckLimit();
    }

    public void Start()
    {
        if (IsRecording) return;
        pendingIssue = null;
        if (model.State is ModelState.Missing)
        {
            pendingIssue = Issue.ModelMissing;
            return;
        }
        if (model.State is ModelState.Failed)
        {
            pendingIssue = Issue.ModelFailed;
            return;
        }

        var recording = new Capture(createSource());
        recording.SpectrumReady += OnSpectrumReady;
        capture = recording;
        _ = WatchMicrophoneStartAsync(recording);

        IsRecording = true;
        recordingClock = Stopwatch.StartNew();
        limitWarned = false;
        limitTimer.Start();
        feedback.Start();
        indicator.ShowRecording();
        Log.Info("recording started");
        StateChanged?.Invoke();
    }

    public async void Finish()
    {
        if (!IsRecording || capture is not { } recording)
        {
            RaisePendingIssue();
            return;
        }
        var windowAtRelease = Native.GetForegroundWindow();
        StopTimers();
        IsRecording = false;
        capture = null;
        pendingJobs++;
        StateChanged?.Invoke();
        // The last syllable is still on its way through the audio stack when the key comes up.
        await Task.Delay(TailDuration);
        var audio = await recording.StopAsync();
        if (recording.Started.IsFaulted)
        {
            pendingJobs--;
            indicator.HideIndicator();
            StateChanged?.Invoke();
            IssueRaised?.Invoke(Issue.NoMicrophone);
            return;
        }
        feedback.Stop();
        indicator.ShowTranscribing();

        var seconds = audio.Samples.Length / (double)PianissimoTranscriber.SampleRate;
        Log.Info($"recording finished: {seconds:F1} s, peak {audio.Peak:F4}");
        var generation = recent.Generation;
        var job = TranscribeAsync(audio);
        var previous = deliveries;
        deliveries = DeliverAsync(previous, job, windowAtRelease, generation);
    }

    public void Cancel(bool audible)
    {
        if (!IsRecording) return;
        StopTimers();
        IsRecording = false;
        capture?.Abandon();
        capture = null;
        if (audible) feedback.Cancel();
        if (pendingJobs == 0) indicator.HideIndicator();
        Log.Info("recording cancelled");
        StateChanged?.Invoke();
    }

    // A dictation that stopped before it began (no model, no microphone) says why once the key is up.
    public void RaisePendingIssue()
    {
        if (pendingIssue is { } issue)
        {
            pendingIssue = null;
            IssueRaised?.Invoke(issue);
        }
    }

    // On the audio thread: hop to the UI thread, where the spectrum is read and drawn.
    private void OnSpectrumReady(Capture recording) => context.Post(_ =>
    {
        var spectrum = recording.ReadSpectrum(analyzer);
        if (recording == capture) indicator.UpdateSpectrum(spectrum);
    }, null);

    // A microphone that fails to open while the key is still held says so once it is let go (TrayApp raises it);
    // if nothing is held (toggle mode, or started from the menu) it says so at once.
    private async Task WatchMicrophoneStartAsync(Capture recording)
    {
        try
        {
            await recording.Started;
        }
        catch (Exception e)
        {
            Log.Error("microphone start failed", e);
            // Finish() reports it itself if the recording already ended.
            if (recording != capture) return;
            Cancel(audible: false);
            pendingIssue = Issue.NoMicrophone;
            if (!Native.IsKeyDown(settings.TriggerKey.VirtualKey())) RaisePendingIssue();
        }
    }

    private async Task<(string? Text, Issue? Issue, TimeSpan Elapsed)> TranscribeAsync((float[] Samples, float Peak) audio)
    {
        if (audio.Samples.Length < DeliveryRules.MinimumSpeechSeconds * PianissimoTranscriber.SampleRate) return (null, null, TimeSpan.Zero);
        if (audio.Peak < DeliveryRules.SilencePeak) return (null, Issue.Silent, TimeSpan.Zero);
        var transcriber = await model.GetAsync();
        if (transcriber == null) return (null, model.State == ModelState.Missing ? Issue.ModelMissing : Issue.ModelFailed, TimeSpan.Zero);
        var clock = Stopwatch.StartNew();
        try
        {
            var text = await Task.Run(() => transcriber.Transcribe(audio.Samples));
            return (text, null, clock.Elapsed);
        }
        catch (Exception e)
        {
            Log.Error("transcription failed", e);
            return (null, Issue.TranscriptionFailed, clock.Elapsed);
        }
    }

    // Deliveries go out in the order the dictations were made, even if a later one transcribes faster.
    private async Task DeliverAsync(Task previous, Task<(string? Text, Issue? Issue, TimeSpan Elapsed)> job, nint windowAtRelease, int generation)
    {
        try
        {
            await previous;
            var (text, issue, elapsed) = await job;
            if (issue is { } problem)
            {
                IssueRaised?.Invoke(problem);
                return;
            }
            if (string.IsNullOrWhiteSpace(text) || !recent.Accepts(generation)) return;
            Log.Info($"transcribed {text.Length} chars in {elapsed.TotalMilliseconds:F0} ms");

            if (settings.KeepLastTranscription) recent.Store(text, DateTime.UtcNow);
            var windowNow = Native.GetForegroundWindow();
            if (DeliveryRules.ShouldAutoPaste(windowAtRelease, windowNow))
            {
                await PasteAsync(text);
            }
            else
            {
                ClipboardPaster.Write(text);
                indicator.ShowMessage(Strings.Get("note.windowChanged"), TimeSpan.FromSeconds(4));
                Log.Info($"window changed ({NativeDiagnostics.Describe(windowAtRelease)} -> {NativeDiagnostics.Describe(windowNow)}): text left on clipboard");
            }
        }
        catch (Exception e)
        {
            Log.Error("delivery failed", e);
        }
        finally
        {
            pendingJobs--;
            if (pendingJobs == 0 && !IsRecording && indicator.State == IndicatorState.Transcribing) indicator.HideIndicator();
            StateChanged?.Invoke();
        }
    }

    // Set only by the automated test (--paste-only-into), so injected test keys can never paste into a real app.
    public string? PasteOnlyIntoProcess { get; set; }

    private async Task PasteAsync(string text)
    {
        if (PasteOnlyIntoProcess != null && NativeDiagnostics.ProcessName(Native.GetForegroundWindow()) != PasteOnlyIntoProcess)
        {
            Log.Info($"test guard: foreground is {NativeDiagnostics.Describe(Native.GetForegroundWindow())}, not {PasteOnlyIntoProcess}; nothing pasted");
            return;
        }
        var original = settings.KeepClipboardContent ? ClipboardPaster.Snapshot() : null;
        var sequenceAfterWrite = ClipboardPaster.Write(text);
        await ClipboardPaster.WaitForModifiersReleasedAsync();
        var delivered = ClipboardPaster.SendPaste();
        if (!delivered) Log.Info("SendInput was blocked (the target window may run as administrator)");
        // The target app reads the clipboard some time after Ctrl+V; restoring too soon would paste the old content.
        await Task.Delay(PasteSettleTime);
        if (DeliveryRules.ShouldRestoreClipboard(settings.KeepClipboardContent, delivered, sequenceAfterWrite, ClipboardPaster.Sequence))
        {
            ClipboardPaster.Restore(original);
        }
    }

    public bool HasRecentTranscript => recent.Text(DateTime.UtcNow) != null;

    public async void PasteLast()
    {
        if (pasteLastInFlight || IsBusy) return;
        var text = recent.Text(DateTime.UtcNow);
        if (text == null) return;
        pasteLastInFlight = true;
        try
        {
            await PasteAsync(text);
        }
        finally
        {
            pasteLastInFlight = false;
        }
    }

    // Locking the PC or putting it to sleep forgets the last text and calls off a recording in progress.
    public void Forget()
    {
        recent.Forget();
        Cancel(audible: false);
    }

    public void SettingsChanged()
    {
        feedback.Enabled = settings.PlaySounds;
        if (!settings.KeepLastTranscription) recent.Clear();
    }

    private void CheckLimit()
    {
        if (!IsRecording || recordingClock == null) return;
        var elapsed = recordingClock.Elapsed;
        if (!limitWarned && RecordingLimit.Standard.ShouldWarn(elapsed))
        {
            limitWarned = true;
            indicator.ShowWarning();
        }
        if (RecordingLimit.Standard.ShouldStop(elapsed))
        {
            Log.Info("recording limit reached");
            Finish();
        }
    }

    private void StopTimers()
    {
        limitTimer.Stop();
        recordingClock = null;
    }

    public void Dispose()
    {
        limitTimer.Dispose();
        capture?.Abandon();
    }
}
