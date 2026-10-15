using Sorla.Core.Audio;
using Sorla.Core.Transcription;

namespace Sorla.Core.Tests;

// Runs the real model when it is installed (and skips otherwise), on Swedish speech made with Windows' own
// sv-SE voice by Scripts/make-test-audio.ps1.
public class TranscriptionIntegrationTests
{
    private static string AudioDirectory => Path.Combine(AppPaths.LocalData, "testaudio");

    public static bool ModelAvailable => ModelFiles.IsInstalled(ModelFiles.DefaultDirectory, ModelVariant.Int8);

    [Fact]
    public void TranscribesSwedishSpeech()
    {
        var file = Path.Combine(AudioDirectory, "mote.wav");
        if (!ModelAvailable || !File.Exists(file))
        {
            Assert.Skip("Model or test audio not installed.");
        }
        using var transcriber = new PianissimoTranscriber(ModelFiles.DefaultDirectory);
        var audio = Wav.Read(file);
        var text = transcriber.Transcribe(Resampler.Resample(audio.Samples, audio.SampleRate, PianissimoTranscriber.SampleRate));
        Assert.Equal("Mötet flyttas till torsdag klockan tre. Kan du skicka underlaget innan dess?", text);

        Assert.Equal("", transcriber.Transcribe(new float[16000]).Trim());
    }
}
