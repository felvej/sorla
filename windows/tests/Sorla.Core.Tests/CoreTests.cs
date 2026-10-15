using Sorla.Core.Audio;
using Sorla.Core.Dictation;
using Sorla.Core.Transcription;

namespace Sorla.Core.Tests;

public class VocabularyTests
{
    private static readonly Vocabulary Vocab = new(["<unk> 0", "▁Hej 1", ", 2", "▁jag 3", "▁heter 4", "▁Bengt 5", ". 6", "s 7", "<blk> 8"]);

    [Fact]
    public void DecodesSentencePieceWithSwedishSpacing()
    {
        Assert.Equal("Hej, jag heter Bengts.", Vocab.Decode([1, 2, 3, 4, 5, 7, 6]));
    }

    [Fact]
    public void BlankIsFoundAndSkipped()
    {
        Assert.Equal(8, Vocab.BlankIndex);
        Assert.Equal("Hej", Vocab.Decode([8, 1, 8]));
    }

    [Theory]
    [InlineData(" hej", "hej")]
    [InlineData(" Hej , du .", "Hej, du.")]
    [InlineData(" klockan  tre", "klockan tre")]
    [InlineData(" åtta älgar", "åtta älgar")]
    public void CleansSpaces(string raw, string expected) => Assert.Equal(expected, Vocabulary.Clean(raw));
}

public class TdtGreedyDecoderTests
{
    // A scripted joint network: per call returns (token, duration).
    private sealed class Script((int Token, int Duration)[] steps, int vocab) : ITdtStep<int>
    {
        public int Calls { get; private set; }
        public List<(int Frame, int Previous)> Seen { get; } = [];

        public int InitialState() => 0;

        public (ReadOnlyMemory<float> TokenScores, int Duration, int State) Step(int frame, int previousToken, int state)
        {
            Seen.Add((frame, previousToken));
            var (token, duration) = steps[Math.Min(Calls++, steps.Length - 1)];
            var scores = new float[vocab];
            scores[token] = 1;
            return (scores, duration, state + 1);
        }
    }

    [Fact]
    public void FollowsDurationsAndFeedsBackLastToken()
    {
        const int blank = 4;
        var script = new Script([(1, 0), (2, 2), (blank, 1), (3, 1)], 5);
        var tokens = TdtGreedyDecoder.Decode(script, frameCount: 4, blankIndex: blank);
        Assert.Equal([1, 2, 3], tokens);
        Assert.Equal([(0, blank), (0, 1), (2, 2), (3, 2)], script.Seen);
    }

    [Fact]
    public void BlankWithZeroDurationStillAdvances()
    {
        var script = new Script([(2, 0)], 3);
        Assert.Empty(TdtGreedyDecoder.Decode(script, frameCount: 5, blankIndex: 2));
        Assert.Equal(5, script.Calls);
    }

    [Fact]
    public void CapsTokensPerFrame()
    {
        var script = new Script([(1, 0)], 3);
        var tokens = TdtGreedyDecoder.Decode(script, frameCount: 2, blankIndex: 2, maxTokensPerStep: 3);
        Assert.Equal(6, tokens.Count);
    }
}

public class RecentTranscriptTests
{
    [Fact]
    public void ExpiresAfterFiveMinutes()
    {
        var recent = new RecentTranscript();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        recent.Store("hej", now);
        Assert.Equal("hej", recent.Text(now.AddMinutes(4.9)));
        Assert.Null(recent.Text(now.AddMinutes(5)));
    }

    [Fact]
    public void ForgetDropsInFlightGenerations()
    {
        var recent = new RecentTranscript();
        var generation = recent.Generation;
        recent.Store("hej", DateTime.UtcNow);
        recent.Forget();
        Assert.Null(recent.Text(DateTime.UtcNow));
        Assert.False(recent.Accepts(generation));
        Assert.True(recent.Accepts(recent.Generation));
    }
}

public class DeliveryRulesTests
{
    [Fact]
    public void PastesOnlyIntoTheSameWindow()
    {
        Assert.True(DeliveryRules.ShouldAutoPaste(42, 42));
        Assert.False(DeliveryRules.ShouldAutoPaste(42, 43));
        Assert.True(DeliveryRules.ShouldAutoPaste(0, 43));
    }

    [Fact]
    public void RestoresClipboardOnlyIfUntouched()
    {
        Assert.True(DeliveryRules.ShouldRestoreClipboard(true, true, 7, 7));
        Assert.False(DeliveryRules.ShouldRestoreClipboard(true, true, 7, 8));
        Assert.False(DeliveryRules.ShouldRestoreClipboard(false, true, 7, 7));
        Assert.False(DeliveryRules.ShouldRestoreClipboard(true, false, 7, 7));
    }

    [Fact]
    public void RecordingLimitWarnsTenSecondsBeforeFiveMinutes()
    {
        var limit = RecordingLimit.Standard;
        Assert.False(limit.ShouldWarn(TimeSpan.FromSeconds(289)));
        Assert.True(limit.ShouldWarn(TimeSpan.FromSeconds(290)));
        Assert.False(limit.ShouldStop(TimeSpan.FromSeconds(299)));
        Assert.True(limit.ShouldStop(TimeSpan.FromSeconds(300)));
    }
}

public class AudioTests
{
    [Fact]
    public void FeedbackSoundsHaveExpectedLengthAndPeak()
    {
        var start = FeedbackSound.StartSamples();
        Assert.Equal((int)Math.Round(FeedbackSound.Duration * FeedbackSound.SampleRate), start.Length);
        Assert.Equal(FeedbackSound.Peak, start.Max(Math.Abs), 3);
        Assert.Equal(0, start[^1], 4);
        Assert.NotEqual(start, FeedbackSound.StopSamples());
    }

    [Fact]
    public void WavRoundTrips()
    {
        var samples = Enumerable.Range(0, 1000).Select(i => (float)Math.Sin(i / 10.0) * 0.5f).ToArray();
        var audio = Wav.Read(Wav.Encode16(samples, 16000));
        Assert.Equal(16000, audio.SampleRate);
        Assert.Equal(samples.Length, audio.Samples.Length);
        for (var i = 0; i < samples.Length; i++) Assert.True(Math.Abs(samples[i] - audio.Samples[i]) < 1e-4f);
    }

    [Fact]
    public void ResamplerKeepsLengthAndTone()
    {
        const int from = 48000, to = 16000;
        var tone = Enumerable.Range(0, from).Select(i => (float)Math.Sin(2 * Math.PI * 440 * i / from)).ToArray();
        var output = Resampler.Resample(tone, from, to);
        Assert.Equal(to, output.Length);
        // Away from the edges the 440 Hz tone survives with its amplitude.
        var middle = output.Skip(1000).Take(14000).ToArray();
        Assert.InRange(middle.Max(), 0.9f, 1.05f);
        var expected = Enumerable.Range(1000, 14000).Select(i => (float)Math.Sin(2 * Math.PI * 440 * i / to)).ToArray();
        var error = middle.Zip(expected, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(error < 0.05, $"max error {error}");
    }

    [Fact]
    public void SpectrumPutsALowToneInALowBand()
    {
        var analyzer = new SpectrumAnalyzer(16000);
        var low = Enumerable.Range(0, 1024).Select(i => 0.5f * (float)Math.Sin(2 * Math.PI * 150 * i / 16000)).ToArray();
        var high = Enumerable.Range(0, 1024).Select(i => 0.5f * (float)Math.Sin(2 * Math.PI * 4000 * i / 16000)).ToArray();
        var lowBands = analyzer.Analyze(low);
        var highBands = analyzer.Analyze(high);
        Assert.True(TdtGreedyDecoder.ArgMax(lowBands) <= 2);
        Assert.True(TdtGreedyDecoder.ArgMax(highBands) >= 5);
        Assert.All(analyzer.Analyze(new float[1024]), v => Assert.Equal(0, v));
    }

    [Fact]
    public void BarsMirrorAroundTheCentre()
    {
        Assert.Equal(VisualizerBars.BandRange(0, 7), VisualizerBars.BandRange(6, 7));
        Assert.Equal((0, 2), VisualizerBars.BandRange(3, 7));
        Assert.Equal(7, VisualizerBars.Magnitudes(new float[8], 1.0, 7).Length);
    }
}

public class SettingsTests
{
    [Fact]
    public void RoundTripsAndToleratesBrokenFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sorla-settings-{Guid.NewGuid()}.json");
        try
        {
            var settings = new Settings { TriggerKey = TriggerKey.RightShift, RecordingMode = RecordingMode.Toggle, PlaySounds = false };
            settings.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal(TriggerKey.RightShift, loaded.TriggerKey);
            Assert.Equal(RecordingMode.Toggle, loaded.RecordingMode);
            Assert.False(loaded.PlaySounds);
            Assert.Contains("\"RightShift\"", File.ReadAllText(path));

            File.WriteAllText(path, "{ not json");
            Assert.Equal(TriggerKeys.Default, Settings.Load(path).TriggerKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EveryEnglishStringHasASwedishTranslation()
    {
        var missing = Strings.English.Keys.Except(Strings.Swedish.Keys).ToList();
        Assert.Empty(missing);
        Assert.Empty(Strings.Swedish.Keys.Except(Strings.English.Keys));
    }

    [Fact]
    public void SwedishStringsUseNoTypographicDashes()
    {
        Assert.All(Strings.Swedish.Values.Concat(Strings.English.Values), v =>
        {
            Assert.DoesNotContain('—', v);
            Assert.DoesNotContain('–', v);
        });
    }

    [Fact]
    public void ModelFileListsMatchVariants()
    {
        Assert.Contains(ModelFiles.Required(ModelVariant.Int8), f => f.Name == "encoder-model.int8.onnx");
        Assert.Contains(ModelFiles.Required(ModelVariant.Int4), f => f.Name == "encoder-model.int4.onnx");
        Assert.All(ModelFiles.Required(ModelVariant.Int8), f => Assert.Equal(64, f.Sha256.Length));
        Assert.Equal(660_572_496, ModelFiles.TotalBytes(ModelVariant.Int8));
    }
}
