namespace Sorla.Core.Audio;

// The same two soft notes as Sorla on the Mac: low->high to start, high->low to stop.
public static class FeedbackSound
{
    public const int SampleRate = 48_000;
    public const double Duration = 0.46;
    public const double LowNote = 220.0;
    public const double HighNote = 329.63;
    public const double NoteDuration = 0.16;
    public const double NoteSpacing = 0.075;
    public const double Attack = 0.008;
    public const double DecayTime = 0.045;
    public const double PitchBend = 0.03;
    public const double PitchBendTime = 0.012;
    public const double SecondHarmonic = 0.08;
    public const double EchoDelay = 0.19;
    public const double EchoGain = 0.13;
    public const double FadeOut = 0.01;
    public const float Peak = 0.25f;

    public static float[] StartSamples() => Sequence([LowNote, HighNote]);

    public static float[] StopSamples() => Sequence([HighNote, LowNote]);

    // A single low note for a dictation that was called off.
    public static float[] CancelSamples() => Sequence([LowNote]);

    internal static float[] Sequence(double[] frequencies)
    {
        var count = (int)Math.Round(Duration * SampleRate);
        var dry = new double[count];
        for (var position = 0; position < frequencies.Length; position++)
        {
            var offset = (int)Math.Round(position * NoteSpacing * SampleRate);
            var note = Note(frequencies[position]);
            for (var i = 0; i < note.Length && offset + i < count; i++) dry[offset + i] += note[i];
        }

        var echoOffset = (int)Math.Round(EchoDelay * SampleRate);
        var fadeCount = (int)Math.Round(FadeOut * SampleRate);
        var mixed = (double[])dry.Clone();
        for (var i = echoOffset; i < count; i++) mixed[i] += dry[i - echoOffset] * EchoGain;
        for (var i = 0; i < fadeCount; i++) mixed[count - 1 - i] *= (double)i / fadeCount;

        var loudest = mixed.Select(Math.Abs).DefaultIfEmpty(1).Max();
        var scale = loudest > 0 ? Peak / loudest : 0;
        return mixed.Select(v => (float)(v * scale)).ToArray();
    }

    private static double[] Note(double frequency)
    {
        var count = (int)Math.Round(NoteDuration * SampleRate);
        var result = new double[count];
        var phase = 0.0;
        for (var i = 0; i < count; i++)
        {
            var seconds = (double)i / SampleRate;
            // A small downward settle at the onset gives the soft "drop" character.
            phase += 2 * Math.PI * frequency * (1 + PitchBend * Math.Exp(-seconds / PitchBendTime)) / SampleRate;
            var envelope = Math.Min(1, seconds / Attack) * Math.Exp(-seconds / DecayTime);
            result[i] = envelope * (Math.Sin(phase) + SecondHarmonic * Math.Sin(2 * phase));
        }
        return result;
    }
}
