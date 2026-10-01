using Sorla.Core.Audio;
using Sorla.Platform;

namespace Sorla;

// One recording: its device, its audio and its live spectrum, so a recording that is still closing can never mix
// with the next one. Audio arrives on the device's thread and is guarded by a lock.
public sealed class Capture
{
    private readonly IAudioSource device;
    private readonly object gate = new();
    private readonly List<float> samples = [];
    private readonly float[] window = new float[SpectrumAnalyzer.FrameCount];
    private int fill;
    private float peak;
    private bool spectrumPending;

    // Opening the device takes 100-200 ms, so it happens off the UI thread.
    public Task Started { get; }

    // Raised on the audio thread when there is a new spectrum to read, at most once until it has been read.
    public event Action<Capture>? SpectrumReady;

    public Capture(IAudioSource device)
    {
        this.device = device;
        device.Frames += OnFrames;
        Started = Task.Run(device.Start);
    }

    private void OnFrames(float[] frames)
    {
        bool notify;
        lock (gate)
        {
            samples.AddRange(frames);
            foreach (var s in frames) peak = Math.Max(peak, Math.Abs(s));
            // A sliding window of the newest 1024 samples for the waveform.
            if (frames.Length >= window.Length)
            {
                Array.Copy(frames, frames.Length - window.Length, window, 0, window.Length);
                fill = window.Length;
            }
            else
            {
                var keep = Math.Min(fill, window.Length - frames.Length);
                Array.Copy(window, fill - keep, window, 0, keep);
                Array.Copy(frames, 0, window, keep, frames.Length);
                fill = keep + frames.Length;
            }
            notify = !spectrumPending;
            spectrumPending = true;
        }
        if (notify) SpectrumReady?.Invoke(this);
    }

    public float[] ReadSpectrum(SpectrumAnalyzer analyzer)
    {
        lock (gate)
        {
            spectrumPending = false;
            return analyzer.Analyze(window.AsSpan(0, fill));
        }
    }

    // Waits for the device's last buffers, then hands over everything recorded.
    public async Task<(float[] Samples, float Peak)> StopAsync()
    {
        // After the start has finished either way, so open and close never run at once.
        await Started.ContinueWith(_ => device.Dispose(), TaskScheduler.Default);
        device.Frames -= OnFrames;
        lock (gate)
        {
            var captured = samples.ToArray();
            samples.Clear();
            return (captured, peak);
        }
    }

    // A cancelled recording is never transcribed, so its audio is dropped at once.
    public void Abandon()
    {
        device.Frames -= OnFrames;
        lock (gate) samples.Clear();
        Started.ContinueWith(_ => device.Dispose(), TaskScheduler.Default);
    }
}
