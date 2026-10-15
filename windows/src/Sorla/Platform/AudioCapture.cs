using NAudio.CoreAudioApi;
using NAudio.Wave;
using Sorla.Core.Audio;
using Sorla.Core.Transcription;

namespace Sorla.Platform;

public sealed record MicrophoneDevice(string Id, string Name);

// A WASAPI capture stream; Windows' audio engine converts whatever the device runs at to 16 kHz mono.
public sealed class MicrophoneSource(string? deviceId) : IAudioSource
{
    private WasapiRecorder? recorder;

    public event Action<float[]>? Frames;
    public event Action<Exception?>? Stopped;

    public static IReadOnlyList<MicrophoneDevice> Devices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new MicrophoneDevice(d.ID, d.FriendlyName))
            .ToList();
    }

    public static MicrophoneDevice? Default()
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console)) return null;
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        return new MicrophoneDevice(device.ID, device.FriendlyName);
    }

    // The chosen device if it is still there, otherwise Windows' default.
    public static MicrophoneDevice? Resolve(string? deviceId) =>
        (string.IsNullOrEmpty(deviceId) ? null : Devices().FirstOrDefault(d => d.Id == deviceId)) ?? Default();

    public static string? DefaultDeviceName => Default()?.Name;

    public void Start()
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = (string.IsNullOrEmpty(deviceId) ? null : TryGet(enumerator, deviceId))
            ?? (enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
                : throw new InvalidOperationException("No microphone is connected."));
        var built = new WasapiRecorderBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithEventSync()
            .WithFormat(new WaveFormat(PianissimoTranscriber.SampleRate, 16, 1))
            .WithBufferLength(20)
            .Build();
        built.DataAvailable += OnData;
        built.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
        recorder = built;
        // Throws when Windows denies desktop apps the microphone or the device is in use exclusively.
        built.StartRecording();
    }

    private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        var count = buffer.Length / 2;
        if (count == 0) return;
        var samples = new float[count];
        // A silent packet (a glitch, or the device starting up) carries no data worth reading.
        if ((flags & AudioClientBufferFlags.Silent) == 0)
        {
            for (var i = 0; i < count; i++) samples[i] = BitConverter.ToInt16(buffer.Slice(i * 2, 2)) / 32768f;
        }
        Frames?.Invoke(samples);
    }

    private static MMDevice? TryGet(MMDeviceEnumerator enumerator, string id)
    {
        try
        {
            var device = enumerator.GetDevice(id);
            return device.State == DeviceState.Active ? device : null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        var device = recorder;
        recorder = null;
        if (device == null) return;
        try { device.StopRecording(); } catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        device.Dispose();
    }
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

    // Plays from the start each time the device is opened, like a person who starts talking at the press.
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

    public void Dispose()
    {
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
        }
    }
}
