using System.Buffers.Binary;
using System.Text;

namespace Sorla.Core.Audio;

public sealed record PcmAudio(float[] Samples, int SampleRate);

public static class Wav
{
    // Reads 16-bit, 24-bit, 32-bit PCM or 32-bit float WAV and mixes all channels down to mono.
    public static PcmAudio Read(byte[] data)
    {
        if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "RIFF" || Encoding.ASCII.GetString(data, 8, 4) != "WAVE")
        {
            throw new FormatException("Not a RIFF/WAVE file.");
        }
        int format = 0, channels = 0, sampleRate = 0, bits = 0;
        var position = 12;
        while (position + 8 <= data.Length)
        {
            var id = Encoding.ASCII.GetString(data, position, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position + 4));
            var body = position + 8;
            if (id == "fmt ")
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + 14));
                if (format == 0xFFFE && size >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + 24));
            }
            else if (id == "data")
            {
                if (channels == 0) throw new FormatException("data chunk before fmt chunk.");
                var length = Math.Min(size, data.Length - body);
                return new PcmAudio(Decode(data.AsSpan(body, length), format, channels, bits), sampleRate);
            }
            position = body + size + (size & 1);
        }
        throw new FormatException("No data chunk.");
    }

    public static PcmAudio Read(string path) => Read(File.ReadAllBytes(path));

    private static float[] Decode(ReadOnlySpan<byte> bytes, int format, int channels, int bits)
    {
        var bytesPerSample = bits / 8;
        var frames = bytes.Length / (bytesPerSample * channels);
        var result = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            float sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = (frame * channels + channel) * bytesPerSample;
                sum += (format, bits) switch
                {
                    (1, 16) => BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]) / 32768f,
                    (1, 24) => ((bytes[offset] | bytes[offset + 1] << 8 | (sbyte)bytes[offset + 2] << 16)) / 8388608f,
                    (1, 32) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]) / 2147483648f,
                    (3, 32) => BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]),
                    _ => throw new FormatException($"Unsupported WAV format {format}/{bits}-bit."),
                };
            }
            result[frame] = sum / channels;
        }
        return result;
    }

    public static byte[] Encode16(ReadOnlySpan<float> samples, int sampleRate)
    {
        var dataSize = samples.Length * 2;
        var data = new byte[44 + dataSize];
        var span = data.AsSpan();
        Encoding.ASCII.GetBytes("RIFF").CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataSize);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        Encoding.ASCII.GetBytes("data").CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);
        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1f, 1f);
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + i * 2)..], (short)MathF.Round(clamped * short.MaxValue));
        }
        return data;
    }
}

public static class Resampler
{
    // Windowed-sinc resampling with a cutoff below the lower Nyquist, so downsampling doesn't alias.
    public static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate || input.Length == 0) return input;
        var ratio = (double)toRate / fromRate;
        var outputLength = (int)Math.Floor(input.Length * ratio);
        var output = new float[outputLength];
        var cutoff = Math.Min(1.0, ratio) * 0.95;
        const int halfTaps = 16;
        var scale = 1 / Math.Min(1.0, ratio);
        var reach = (int)Math.Ceiling(halfTaps * scale);
        for (var i = 0; i < outputLength; i++)
        {
            var center = i / ratio;
            var first = (int)Math.Floor(center) - reach + 1;
            double sum = 0, weightSum = 0;
            for (var j = first; j < first + 2 * reach; j++)
            {
                if (j < 0 || j >= input.Length) continue;
                var x = (j - center) * cutoff;
                var sinc = Math.Abs(x) < 1e-9 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
                var w = (j - center) / reach;
                var window = Math.Abs(w) >= 1 ? 0 : 0.5 * (1 + Math.Cos(Math.PI * w));
                var weight = sinc * window;
                sum += input[j] * weight;
                weightSum += weight;
            }
            output[i] = weightSum == 0 ? 0 : (float)(sum / weightSum);
        }
        return output;
    }
}
