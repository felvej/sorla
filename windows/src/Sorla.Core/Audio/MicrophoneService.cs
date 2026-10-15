namespace Sorla.Core.Audio;

// The microphone side of a recording, so the service can be driven by a file or a fake in tests.
public interface IAudioSource : IDisposable
{
    // Mono float samples at 16 kHz, delivered on a background thread.
    event Action<float[]>? Frames;
    // Raised when the device stops by itself; the exception says why (unplugged, access revoked).
    event Action<Exception?>? Stopped;
    void Start();
}

// One recording: its audio and its live spectrum, kept apart from any other recording.
public sealed class Capture
{
    private readonly object gate = new();
    private readonly List<float> samples = [];
    private readonly float[] window = new float[SpectrumAnalyzer.FrameCount];
    private int fill;
    private float peak;
    private bool spectrumPending;
    private bool ready;
    private Exception? failure;

    // Raised once, when the first audio of this recording has arrived: the moment the user can speak.
    public event Action<Capture>? Ready;
    // Raised if the microphone could not be opened or stopped while recording.
    public event Action<Capture>? Failed;
    // Raised on the audio thread when there is a new spectrum to read, at most once until it has been read.
    public event Action<Capture>? SpectrumReady;

    public bool IsReady
    {
        get { lock (gate) return ready; }
    }

    public Exception? Failure
    {
        get { lock (gate) return failure; }
    }

    internal void Append(ReadOnlySpan<float> frames)
    {
        bool becameReady, notify;
        lock (gate)
        {
            if (failure != null) return;
            foreach (var s in frames)
            {
                samples.Add(s);
                peak = Math.Max(peak, Math.Abs(s));
            }
            // A sliding window of the newest 1024 samples for the waveform.
            if (frames.Length >= window.Length)
            {
                frames[^window.Length..].CopyTo(window);
                fill = window.Length;
            }
            else
            {
                var keep = Math.Min(fill, window.Length - frames.Length);
                Array.Copy(window, fill - keep, window, 0, keep);
                frames.CopyTo(window.AsSpan(keep));
                fill = keep + frames.Length;
            }
            becameReady = !ready;
            ready = true;
            notify = !spectrumPending;
            spectrumPending = true;
        }
        if (becameReady) Ready?.Invoke(this);
        if (notify) SpectrumReady?.Invoke(this);
    }

    internal void Fail(Exception error)
    {
        lock (gate)
        {
            if (failure != null) return;
            failure = error;
            samples.Clear();
        }
        Failed?.Invoke(this);
    }

    public float[] ReadSpectrum(SpectrumAnalyzer analyzer)
    {
        lock (gate)
        {
            spectrumPending = false;
            return analyzer.Analyze(window.AsSpan(0, fill));
        }
    }

    internal (float[] Samples, float Peak) Take()
    {
        lock (gate)
        {
            var captured = samples.ToArray();
            samples.Clear();
            return (captured, peak);
        }
    }

    internal void Clear()
    {
        lock (gate) samples.Clear();
    }
}

// Owns the microphone. Opening a device is slow (150-250 ms for a built-in microphone, over a second for a Bluetooth
// headset switching to its headset profile), so after a dictation the device can be kept open for a while: the next
// dictation then starts within a few milliseconds. While it is open and nobody is recording, only the last
// PreRoll of audio is held, in memory, so a word begun as the key went down is not cut; everything older is dropped.
public sealed class MicrophoneService : IDisposable
{
    public static readonly TimeSpan PreRoll = TimeSpan.FromMilliseconds(300);

    private readonly object gate = new();
    private readonly float[] ring;
    private Func<IAudioSource> factory;
    private IAudioSource? source;
    private Task opening = Task.CompletedTask;
    private bool hasAudio;
    private int ringPosition;
    private int ringFill;
    private Capture? active;
    private Timer? closeTimer;
    private int generation;

    public MicrophoneService(Func<IAudioSource> factory, int sampleRate = 16_000)
    {
        this.factory = factory;
        ring = new float[(int)(sampleRate * PreRoll.TotalSeconds)];
    }

    // How long the microphone stays open after a dictation: Zero closes it at once, InfiniteTimeSpan never.
    public TimeSpan KeepWarm { get; set; } = TimeSpan.FromMinutes(1);

    public bool IsOpen
    {
        get { lock (gate) return source != null; }
    }

    // Raised (on any thread) when the device opens or closes, for the status shown to the user.
    public event Action? OpenChanged;

    // A different device: the open one is closed, and the next dictation opens the new one.
    public void Reconfigure(Func<IAudioSource> newFactory)
    {
        lock (gate) factory = newFactory;
        Close();
    }

    // Opens the device without recording, for "always ready".
    public void Prepare()
    {
        lock (gate)
        {
            CancelCloseTimer();
            OpenLocked();
        }
    }

    public Capture Begin()
    {
        var capture = new Capture();
        float[]? preRoll = null;
        lock (gate)
        {
            CancelCloseTimer();
            active?.Clear();
            active = capture;
            if (source != null && hasAudio)
            {
                preRoll = ReadRingLocked();
            }
            else if (source == null)
            {
                OpenLocked();
            }
        }
        // Warm: the recording starts with the last moments before the key went down, and is ready at once.
        if (preRoll != null) capture.Append(preRoll);
        return capture;
    }

    // Ends a recording and hands over its audio; the device stays open for KeepWarm.
    public (float[] Samples, float Peak) End(Capture capture)
    {
        lock (gate)
        {
            if (active == capture) active = null;
            ScheduleCloseLocked();
        }
        return capture.Take();
    }

    // A cancelled recording is never transcribed, so its audio is dropped at once.
    public void Abandon(Capture capture)
    {
        capture.Clear();
        lock (gate)
        {
            if (active == capture) active = null;
            ScheduleCloseLocked();
        }
    }

    // Closes the device now and forgets the pre-roll (screen locked, PC going to sleep, device changed).
    public void Close()
    {
        Closing closing;
        lock (gate) closing = CloseLocked();
        Finish(closing, new InvalidOperationException("The microphone was closed."));
    }

    private readonly record struct Closing(IAudioSource? Source, Task Opening, Capture? Interrupted);

    private Closing CloseLocked()
    {
        CancelCloseTimer();
        var closing = new Closing(source, opening, active);
        source = null;
        active = null;
        hasAudio = false;
        ClearRingLocked();
        generation++;
        return closing;
    }

    // Outside the lock: event handlers never run while it is held.
    private void Finish(Closing closing, Exception reason)
    {
        closing.Interrupted?.Fail(reason);
        if (closing.Source is not { } device) return;
        device.Frames -= OnFrames;
        device.Stopped -= OnStopped;
        // After the start has finished either way, so open and close never run at once.
        closing.Opening.ContinueWith(_ => device.Dispose(), TaskScheduler.Default);
        OpenChanged?.Invoke();
    }

    private void OpenLocked()
    {
        if (source != null) return;
        var device = factory();
        device.Frames += OnFrames;
        device.Stopped += OnStopped;
        source = device;
        var openedGeneration = ++generation;
        opening = Task.Run(device.Start);
        opening.ContinueWith(t => OnStartFailed(openedGeneration, t.Exception!.GetBaseException()),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        OpenChanged?.Invoke();
    }

    private void OnStartFailed(int openedGeneration, Exception error)
    {
        Closing closing;
        lock (gate)
        {
            if (openedGeneration != generation) return;
            closing = CloseLocked();
        }
        Finish(closing, error);
    }

    private void OnStopped(Exception? error)
    {
        Closing closing;
        lock (gate) closing = CloseLocked();
        Finish(closing, error ?? new InvalidOperationException("The microphone stopped."));
    }

    private void OnFrames(float[] frames)
    {
        Capture? target;
        lock (gate)
        {
            hasAudio = true;
            target = active;
            if (target == null) WriteRingLocked(frames);
        }
        target?.Append(frames);
    }

    private void WriteRingLocked(ReadOnlySpan<float> frames)
    {
        if (frames.Length >= ring.Length)
        {
            frames[^ring.Length..].CopyTo(ring);
            ringPosition = 0;
            ringFill = ring.Length;
            return;
        }
        foreach (var s in frames)
        {
            ring[ringPosition] = s;
            ringPosition = (ringPosition + 1) % ring.Length;
        }
        ringFill = Math.Min(ring.Length, ringFill + frames.Length);
    }

    private float[] ReadRingLocked()
    {
        var result = new float[ringFill];
        var start = (ringPosition - ringFill + ring.Length) % ring.Length;
        for (var i = 0; i < ringFill; i++) result[i] = ring[(start + i) % ring.Length];
        ClearRingLocked();
        return result;
    }

    private void ClearRingLocked()
    {
        Array.Clear(ring);
        ringPosition = 0;
        ringFill = 0;
    }

    private void ScheduleCloseLocked()
    {
        CancelCloseTimer();
        if (KeepWarm == Timeout.InfiniteTimeSpan) return;
        var scheduledGeneration = generation;
        if (KeepWarm <= TimeSpan.Zero)
        {
            ThreadPool.QueueUserWorkItem(_ => CloseIfIdle(scheduledGeneration));
            return;
        }
        closeTimer = new Timer(_ => CloseIfIdle(scheduledGeneration), null, KeepWarm, Timeout.InfiniteTimeSpan);
    }

    // Checked and closed under one lock, so a dictation begun as the timer fires is never cut off.
    private void CloseIfIdle(int scheduledGeneration)
    {
        Closing closing;
        lock (gate)
        {
            if (active != null || scheduledGeneration != generation) return;
            closing = CloseLocked();
        }
        Finish(closing, new InvalidOperationException("The microphone was closed."));
    }

    private void CancelCloseTimer()
    {
        closeTimer?.Dispose();
        closeTimer = null;
    }

    public void Dispose() => Close();
}
