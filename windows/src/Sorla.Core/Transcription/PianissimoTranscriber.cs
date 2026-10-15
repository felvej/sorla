using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Sorla.Core.Transcription;

public interface ITranscriber : IDisposable
{
    string Transcribe(float[] samples16k);
}

// Klang Pianissimo (Parakeet TDT 0.6B) on onnxruntime, CPU only: preprocessor -> encoder -> greedy TDT decoding.
public sealed class PianissimoTranscriber : ITranscriber
{
    public const int SampleRate = 16_000;

    private readonly InferenceSession preprocessor;
    private readonly InferenceSession encoder;
    private readonly InferenceSession decoderJoint;
    private readonly Vocabulary vocabulary;
    private readonly int stateLayers;
    private readonly int stateSize;
    private readonly object gate = new();

    // Official ONNX Runtime builds emit telemetry events (through ETW on Windows, which Windows may upload).
    // Turned off before any session exists; nothing about the audio or text is ever in them, but nothing is sent.
    public static void DisableTelemetry() => OrtEnv.Instance().DisableTelemetryEvents();

    public PianissimoTranscriber(string modelDirectory, ModelVariant variant = ModelVariant.Int8, int? threads = null)
    {
        DisableTelemetry();
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = threads ?? Math.Max(1, Math.Min(8, Environment.ProcessorCount / 2)),
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        var files = ModelFiles.For(variant);
        preprocessor = new InferenceSession(Path.Combine(modelDirectory, ModelFiles.Preprocessor), options);
        encoder = new InferenceSession(Path.Combine(modelDirectory, files.Encoder), options);
        decoderJoint = new InferenceSession(Path.Combine(modelDirectory, files.DecoderJoint), options);
        vocabulary = Vocabulary.Load(Path.Combine(modelDirectory, ModelFiles.Vocabulary));

        var stateShape = decoderJoint.InputMetadata["input_states_1"].Dimensions;
        stateLayers = stateShape[0];
        stateSize = stateShape[2];
    }

    // The first run is slow while onnxruntime allocates, so it happens at launch rather than on the first dictation.
    public void WarmUp() => Transcribe(new float[SampleRate]);

    public string Transcribe(float[] samples16k)
    {
        if (samples16k.Length == 0) return "";
        lock (gate)
        {
            var (features, featureLength) = Preprocess(samples16k);
            var (encoded, frames, dimension) = Encode(features, featureLength);
            var step = new OnnxTdtStep(decoderJoint, encoded, dimension, vocabulary.Count, stateLayers, stateSize);
            var ids = TdtGreedyDecoder.Decode(step, frames, vocabulary.BlankIndex);
            return vocabulary.Decode(ids);
        }
    }

    private (DenseTensor<float> Features, long Length) Preprocess(float[] samples)
    {
        var waveforms = new DenseTensor<float>(samples, [1, samples.Length]);
        var lengths = new DenseTensor<long>(new long[] { samples.Length }, [1]);
        using var results = preprocessor.Run([
            NamedOnnxValue.CreateFromTensor("waveforms", waveforms),
            NamedOnnxValue.CreateFromTensor("waveforms_lens", lengths),
        ]);
        var features = results.First(r => r.Name == "features").AsTensor<float>().ToDenseTensor();
        var length = results.First(r => r.Name == "features_lens").AsTensor<long>()[0];
        return (features, length);
    }

    // Returns the encoder output as [frames, dimension], row-major, so each frame is one contiguous slice.
    private (float[] Encoded, int Frames, int Dimension) Encode(DenseTensor<float> features, long featureLength)
    {
        var lengths = new DenseTensor<long>(new[] { featureLength }, [1]);
        using var results = encoder.Run([
            NamedOnnxValue.CreateFromTensor("audio_signal", features),
            NamedOnnxValue.CreateFromTensor("length", lengths),
        ]);
        var outputs = results.First(r => r.Name == "outputs").AsTensor<float>();
        var encodedLength = (int)results.First(r => r.Name == "encoded_lengths").AsTensor<long>()[0];
        var dimension = outputs.Dimensions[1];
        var total = outputs.Dimensions[2];
        var frames = Math.Min(encodedLength, total);
        var transposed = new float[frames * dimension];
        var source = outputs.ToDenseTensor().Buffer.Span;
        for (var d = 0; d < dimension; d++)
        {
            var row = source.Slice(d * total, total);
            for (var t = 0; t < frames; t++) transposed[t * dimension + d] = row[t];
        }
        return (transposed, frames, dimension);
    }

    public void Dispose()
    {
        preprocessor.Dispose();
        encoder.Dispose();
        decoderJoint.Dispose();
    }

    private sealed class OnnxTdtStep(
        InferenceSession session, float[] encoded, int dimension, int vocabSize, int layers, int size)
        : ITdtStep<(float[] H, float[] C)>
    {
        private readonly float[] frameBuffer = new float[dimension];

        public (float[] H, float[] C) InitialState() => (new float[layers * size], new float[layers * size]);

        public (ReadOnlyMemory<float> TokenScores, int Duration, (float[] H, float[] C) State) Step(
            int frame, int previousToken, (float[] H, float[] C) state)
        {
            Array.Copy(encoded, frame * dimension, frameBuffer, 0, dimension);
            using var results = session.Run([
                NamedOnnxValue.CreateFromTensor("encoder_outputs", new DenseTensor<float>(frameBuffer, [1, dimension, 1])),
                NamedOnnxValue.CreateFromTensor("targets", new DenseTensor<int>(new[] { previousToken }, [1, 1])),
                NamedOnnxValue.CreateFromTensor("target_length", new DenseTensor<int>(new[] { 1 }, [1])),
                NamedOnnxValue.CreateFromTensor("input_states_1", new DenseTensor<float>(state.H, [layers, 1, size])),
                NamedOnnxValue.CreateFromTensor("input_states_2", new DenseTensor<float>(state.C, [layers, 1, size])),
            ]);
            var output = results.First(r => r.Name == "outputs").AsTensor<float>().ToArray();
            var h = results.First(r => r.Name == "output_states_1").AsTensor<float>().ToArray();
            var c = results.First(r => r.Name == "output_states_2").AsTensor<float>().ToArray();
            var duration = TdtGreedyDecoder.ArgMax(output.AsSpan(vocabSize));
            return (output.AsMemory(0, vocabSize), duration, (h, c));
        }
    }
}
