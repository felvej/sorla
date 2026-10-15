using Sorla.Core.Audio;

namespace Sorla.Core.Tests;

public class MicrophoneServiceTests
{
    private sealed class FakeSource : IAudioSource
    {
        public readonly ManualResetEventSlim StartedSignal = new();
        public Exception? FailWith;
        public bool Disposed;

        public event Action<float[]>? Frames;
        public event Action<Exception?>? Stopped;

        public void Start()
        {
            if (FailWith != null) throw FailWith;
            StartedSignal.Set();
        }

        public void Emit(params float[] samples) => Frames?.Invoke(samples);
        public void Stop(Exception? error) => Stopped?.Invoke(error);
        public void Dispose() => Disposed = true;
    }

    private static (MicrophoneService Service, List<FakeSource> Opened) Create(TimeSpan keepWarm)
    {
        var opened = new List<FakeSource>();
        var service = new MicrophoneService(() =>
        {
            var source = new FakeSource();
            opened.Add(source);
            return source;
        }) { KeepWarm = keepWarm };
        return (service, opened);
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < until) Thread.Sleep(5);
        Assert.True(condition());
    }

    [Fact]
    public void ColdStartIsNotReadyUntilAudioArrives()
    {
        var (mic, opened) = Create(TimeSpan.FromMinutes(1));
        var capture = mic.Begin();
        var readyCount = 0;
        capture.Ready += _ => readyCount++;
        Assert.False(capture.IsReady);
        WaitUntil(() => opened.Count == 1 && opened[0].StartedSignal.IsSet);

        opened[0].Emit(0.1f, 0.2f);
        opened[0].Emit(0.3f);
        Assert.True(capture.IsReady);
        Assert.Equal(1, readyCount);
        var (samples, peak) = mic.End(capture);
        Assert.Equal([0.1f, 0.2f, 0.3f], samples);
        Assert.Equal(0.3f, peak);
    }

    [Fact]
    public void WarmStartIsReadyAtOnceWithPreRollAndSameDevice()
    {
        var (mic, opened) = Create(TimeSpan.FromMinutes(1));
        var first = mic.Begin();
        WaitUntil(() => opened.Count == 1 && opened[0].StartedSignal.IsSet);
        opened[0].Emit(1f);
        mic.End(first);

        // Between dictations only the last PreRoll survives.
        opened[0].Emit(new float[16_000]);
        opened[0].Emit(0.5f, 0.6f);
        var second = mic.Begin();
        Assert.True(second.IsReady);
        opened[0].Emit(0.7f);
        var (samples, _) = mic.End(second);
        Assert.Single(opened);
        Assert.Equal((int)(16_000 * MicrophoneService.PreRoll.TotalSeconds) + 1, samples.Length);
        Assert.Equal([0.5f, 0.6f, 0.7f], samples[^3..]);
    }

    [Fact]
    public void OffClosesTheDeviceAfterEachDictation()
    {
        var (mic, opened) = Create(TimeSpan.Zero);
        var capture = mic.Begin();
        WaitUntil(() => opened.Count == 1 && opened[0].StartedSignal.IsSet);
        opened[0].Emit(0.1f);
        mic.End(capture);
        WaitUntil(() => opened[0].Disposed);
        Assert.False(mic.IsOpen);

        mic.Begin();
        WaitUntil(() => opened.Count == 2);
    }

    [Fact]
    public void KeepWarmTimerClosesTheDevice()
    {
        var (mic, opened) = Create(TimeSpan.FromMilliseconds(100));
        var capture = mic.Begin();
        WaitUntil(() => opened.Count == 1 && opened[0].StartedSignal.IsSet);
        opened[0].Emit(0.1f);
        mic.End(capture);
        Assert.True(mic.IsOpen);
        WaitUntil(() => opened[0].Disposed && !mic.IsOpen);
    }

    [Fact]
    public void FailedStartFailsTheRecordingAndClosesTheDevice()
    {
        var mic = new MicrophoneService(() => new FakeSource { FailWith = new InvalidOperationException("denied") });
        var capture = mic.Begin();
        WaitUntil(() => capture.Failure != null);
        Assert.Equal("denied", capture.Failure!.Message);
        WaitUntil(() => !mic.IsOpen);
        Assert.False(capture.IsReady);
    }

    [Fact]
    public void DeviceThatStopsFailsTheRecording()
    {
        var (mic, opened) = Create(TimeSpan.FromMinutes(1));
        var capture = mic.Begin();
        WaitUntil(() => opened.Count == 1 && opened[0].StartedSignal.IsSet);
        opened[0].Emit(0.1f);
        opened[0].Stop(new IOException("unplugged"));
        Assert.Equal("unplugged", capture.Failure?.Message);
        Assert.False(mic.IsOpen);
        Assert.Empty(capture.Take().Samples);
    }

    [Fact]
    public void AbandonDropsTheAudio()
    {
        var (mic, opened) = Create(TimeSpan.FromMinutes(1));
        var capture = mic.Begin();
        WaitUntil(() => opened.Count == 1 && opened[0].StartedSignal.IsSet);
        opened[0].Emit(0.1f, 0.2f);
        mic.Abandon(capture);
        opened[0].Emit(0.3f);
        Assert.Empty(capture.Take().Samples);
    }

    [Fact]
    public void CloseForgetsThePreRoll()
    {
        var (mic, opened) = Create(TimeSpan.FromMinutes(1));
        var first = mic.Begin();
        WaitUntil(() => opened.Count == 1 && opened[0].StartedSignal.IsSet);
        opened[0].Emit(0.1f);
        mic.End(first);
        opened[0].Emit(0.9f);
        mic.Close();
        Assert.True(opened[0].Disposed || !mic.IsOpen);

        var second = mic.Begin();
        WaitUntil(() => opened.Count == 2 && opened[1].StartedSignal.IsSet);
        opened[1].Emit(0.2f);
        Assert.Equal([0.2f], mic.End(second).Samples);
    }

    [Fact]
    public void PrepareOpensWithoutRecording()
    {
        var (mic, opened) = Create(Timeout.InfiniteTimeSpan);
        mic.Prepare();
        WaitUntil(() => opened.Count == 1 && opened[0].StartedSignal.IsSet);
        opened[0].Emit(0.4f);
        var capture = mic.Begin();
        Assert.True(capture.IsReady);
        Assert.Equal([0.4f], mic.End(capture).Samples);
        Assert.True(mic.IsOpen);
    }
}
