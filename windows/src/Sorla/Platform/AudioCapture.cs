using NAudio.Wave;
using Sorla.Core.Audio;
using Sorla.Core.Transcription;

namespace Sorla.Platform;

// The microphone side of a recording, so a dictation can be driven from a file in tests.
public interface IAudioSource : IDisposable
{
    // Frames are mono float samples at 16 kHz, delivered on a background thread.
    event Action<float[]>? Frames;
    event Action<Exception?>? Stopped;
    void Start();
    void Stop();
}

// Windows' own mixer converts whatever the device runs at to 16 kHz mono, which is what the model wants.
public sealed class MicrophoneSource : IAudioSource
{
    private WaveIn? waveIn;

    public event Action<float[]>? Frames;
    public event Action<Exception?>? Stopped;

    public static bool HasDevice => WaveIn.DeviceCount > 0;

    public static string? DefaultDeviceName => WaveIn.DeviceCount > 0 ? WaveIn.GetCapabilities(0).ProductName : null;

    public void Start()
    {
        Stop();
        var device = new WaveIn
        {
            // -1 is WAVE_MAPPER: the default recording device chosen in Windows sound settings.
            DeviceNumber = -1,
            WaveFormat = new WaveFormat(PianissimoTranscriber.SampleRate, 16, 1),
            BufferMilliseconds = 30,
            NumberOfBuffers = 4,
        };
        device.DataAvailable += (_, e) =>
        {
            var count = e.BytesRecorded / 2;
            var samples = new float[count];
            for (var i = 0; i < count; i++) samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            Frames?.Invoke(samples);
        };
        device.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
        waveIn = device;
        // Throws MmException when there is no device or Windows denies desktop apps the microphone.
        device.StartRecording();
    }

    public void Stop()
    {
        var device = waveIn;
        waveIn = null;
        if (device == null) return;
        try { device.StopRecording(); } catch (NAudio.MmException) { }
        device.Dispose();
    }

    public void Dispose() => Stop();
}

// Plays a WAV file in real time as if it were spoken into the microphone, then silence. For automated tests only.
public sealed class FileAudioSource(string path) : IAudioSource
{
    private readonly float[] samples = Load(path);
    private System.Threading.Timer? timer;
    private int position;
    private readonly object gate = new();

    public event Action<float[]>? Frames;
    public event Action<Exception?>? Stopped;

    private static float[] Load(string path)
    {
        var audio = Wav.Read(path);
        return Resampler.Resample(audio.Samples, audio.SampleRate, PianissimoTranscriber.SampleRate);
    }

    public void Start()
    {
        lock (gate)
        {
            position = 0;
            timer?.Dispose();
            timer = new System.Threading.Timer(_ => Tick(), null, 0, 30);
        }
    }

    private void Tick()
    {
        float[] chunk;
        lock (gate)
        {
            if (timer == null) return;
            chunk = new float[480];
            var available = Math.Max(0, Math.Min(chunk.Length, samples.Length - position));
            if (available > 0) Array.Copy(samples, position, chunk, 0, available);
            position += chunk.Length;
        }
        Frames?.Invoke(chunk);
    }

    public void Stop()
    {
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
        }
        Stopped?.Invoke(null);
    }

    public void Dispose() => Stop();
}
