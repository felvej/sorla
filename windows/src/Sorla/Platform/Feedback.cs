using System.Media;
using Sorla.Core.Audio;

namespace Sorla.Platform;

// Synthesised once at launch, so there are no sound files to ship.
public sealed class Feedback : IDisposable
{
    private readonly SoundPlayer start = Create(FeedbackSound.StartSamples());
    private readonly SoundPlayer stop = Create(FeedbackSound.StopSamples());
    private readonly SoundPlayer cancel = Create(FeedbackSound.CancelSamples());

    public bool Enabled { get; set; } = true;

    private static SoundPlayer Create(float[] samples)
    {
        var player = new SoundPlayer(new MemoryStream(Wav.Encode16(samples, FeedbackSound.SampleRate)));
        player.Load();
        return player;
    }

    public void Start() => Play(start);
    public void Stop() => Play(stop);
    public void Cancel() => Play(cancel);

    private void Play(SoundPlayer player)
    {
        if (!Enabled) return;
        try { player.Play(); } catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        start.Dispose();
        stop.Dispose();
        cancel.Dispose();
    }
}
