using System.Numerics;

namespace Sorla.Core.Audio;

// Eight log-spaced bands from 90 Hz to 7 kHz, in 0...1, for the live waveform. Port of the Mac analyzer.
public sealed class SpectrumAnalyzer
{
    public const int FrameCount = 1024;
    public const int BandCount = 8;
    private const double LowestFrequency = 90;
    private const double HighestFrequency = 7000;
    private const float FloorDecibels = -60;
    private const float CeilingDecibels = -14;
    private const float TiltDecibelsPerOctave = 3;

    private readonly float[] window = new float[FrameCount];
    private readonly Complex[] buffer = new Complex[FrameCount];
    private readonly int[] lowerBins = new int[BandCount];
    private readonly int[] upperBins = new int[BandCount];
    private readonly float[] offsetDecibels = new float[BandCount];

    public int SampleRate { get; }

    public SpectrumAnalyzer(int sampleRate)
    {
        SampleRate = sampleRate;
        double windowSum = 0;
        for (var i = 0; i < FrameCount; i++)
        {
            window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / FrameCount)));
            windowSum += window[i];
        }
        var half = FrameCount / 2;
        var binWidth = (double)sampleRate / FrameCount;
        var ratio = Math.Pow(HighestFrequency / LowestFrequency, 1.0 / BandCount);
        for (var band = 0; band < BandCount; band++)
        {
            var low = LowestFrequency * Math.Pow(ratio, band);
            var high = low * ratio;
            var first = Math.Max(1, (int)Math.Ceiling(low / binWidth));
            var last = Math.Min(half - 1, Math.Max(first, (int)Math.Ceiling(high / binWidth) - 1));
            lowerBins[band] = first;
            upperBins[band] = last;
            // Speech energy falls with frequency; a gentle tilt keeps the upper bands visible.
            var tilt = TiltDecibelsPerOctave * (float)Math.Log2(Math.Sqrt(low * high) / LowestFrequency);
            offsetDecibels[band] = tilt - 20 * (float)Math.Log10(windowSum);
        }
    }

    public float[] Analyze(ReadOnlySpan<float> samples)
    {
        var result = new float[BandCount];
        if (samples.IsEmpty) return result;
        var used = Math.Min(samples.Length, FrameCount);
        var tail = samples[^used..];
        for (var i = 0; i < FrameCount; i++) buffer[i] = i < used ? new Complex(tail[i] * window[i], 0) : Complex.Zero;
        Fft(buffer);

        const float range = CeilingDecibels - FloorDecibels;
        for (var band = 0; band < BandCount; band++)
        {
            double peak = 0;
            for (var bin = lowerBins[band]; bin <= upperBins[band]; bin++)
            {
                var magnitude = buffer[bin].Magnitude;
                peak = Math.Max(peak, magnitude * magnitude);
            }
            if (peak <= 0) continue;
            var decibels = 10 * (float)Math.Log10(peak) + offsetDecibels[band];
            result[band] = Math.Clamp((decibels - FloorDecibels) / range, 0, 1);
        }
        return result;
    }

    internal static void Fft(Complex[] data)
    {
        var n = data.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var start = 0; start < n; start += length)
            {
                var w = Complex.One;
                for (var k = 0; k < length / 2; k++)
                {
                    var even = data[start + k];
                    var odd = data[start + k + length / 2] * w;
                    data[start + k] = even + odd;
                    data[start + k + length / 2] = even - odd;
                    w *= step;
                }
            }
        }
    }
}

public sealed class BandSmoother
{
    private const float Attack = 0.3f;
    private const float Release = 0.08f;
    // Pulling every band part-way toward the mean keeps the shape but stops bars twitching independently.
    private const float Coupling = 0.15f;
    private readonly float[] values = new float[SpectrumAnalyzer.BandCount];

    public float[] Update(float[] target)
    {
        var mean = target.Average();
        for (var band = 0; band < values.Length; band++)
        {
            var blended = Math.Clamp(target[band] * (1 - Coupling) + mean * Coupling, 0, 1);
            var coefficient = blended > values[band] ? Attack : Release;
            values[band] = Math.Clamp(values[band] + (blended - values[band]) * coefficient, 0, 1);
        }
        return (float[])values.Clone();
    }

    public void Reset() => Array.Clear(values);
}

public static class VisualizerBars
{
    // Above 1 so differences between bands grow instead of every bar sitting near the same height.
    private const float ContrastExponent = 1.35f;
    private const float WobbleFloor = 0.45f;
    private const double ShimmerPeriod = 1.1;
    private const double ShimmerWidth = 2.2;

    // Bars mirror around a centre bar, so each distance from it gets an equal slice of the 8 bands.
    public static (int Lower, int Upper) BandRange(int bar, int barCount)
    {
        if (barCount % 2 != 1) throw new ArgumentException("bar count must be odd", nameof(barCount));
        var half = barCount / 2;
        var distance = Math.Abs(bar - half);
        return (distance * 8 / (half + 1), (distance + 1) * 8 / (half + 1));
    }

    public static float[] Magnitudes(float[] bands, double time, int barCount)
    {
        var result = new float[barCount];
        for (var bar = 0; bar < barCount; bar++)
        {
            var (lower, upper) = BandRange(bar, barCount);
            float sum = 0;
            for (var b = lower; b < upper; b++) sum += bands[b];
            var level = MathF.Pow(Math.Clamp(sum / (upper - lower), 0, 1), ContrastExponent);
            var motion = WobbleFloor + (1 - WobbleFloor) * Wobble(bar, time);
            result[bar] = Math.Clamp(level * motion, 0, 1);
        }
        return result;
    }

    // Each bar sways on its own irregular rhythm so the voice's level shows as varied, lifelike heights.
    internal static float Wobble(int bar, double time)
    {
        var spread = bar * 0.618034;
        var slow = 5.0 + 3.0 * (spread % 1);
        var fast = 8.5 + 4.0 * (spread * 1.7 % 1);
        var a = Math.Sin(time * slow % (2 * Math.PI) + spread * 5.1);
        var b = Math.Sin(time * fast % (2 * Math.PI) + spread * 2.3);
        return Math.Clamp((float)(0.5 + 0.3 * a + 0.2 * b), 0, 1);
    }

    // A highlight that sweeps across the bars while the model is working.
    public static float[] Shimmer(double time, int barCount)
    {
        var progress = time % ShimmerPeriod / ShimmerPeriod;
        var position = progress * (barCount + 2 * ShimmerWidth) - ShimmerWidth;
        var result = new float[barCount];
        for (var bar = 0; bar < barCount; bar++)
        {
            var distance = (bar - position) / ShimmerWidth;
            result[bar] = Math.Clamp((float)Math.Exp(-distance * distance), 0, 1);
        }
        return result;
    }
}
