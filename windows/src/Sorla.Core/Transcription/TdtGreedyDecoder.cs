namespace Sorla.Core.Transcription;

// One step of the decoder+joint network: given the last emitted token and the LSTM state,
// returns token scores, the predicted duration (frames to skip) and the state after that token.
public interface ITdtStep<TState>
{
    TState InitialState();
    (ReadOnlyMemory<float> TokenScores, int Duration, TState State) Step(int frame, int previousToken, TState state);
}

// Greedy Token-and-Duration Transducer decoding, as in NeMo and onnx-asr.
public static class TdtGreedyDecoder
{
    public const int DefaultMaxTokensPerStep = 10;

    public static List<int> Decode<TState>(ITdtStep<TState> step, int frameCount, int blankIndex, int maxTokensPerStep = DefaultMaxTokensPerStep)
    {
        var tokens = new List<int>();
        var state = step.InitialState();
        var frame = 0;
        var emittedAtFrame = 0;
        while (frame < frameCount)
        {
            var previous = tokens.Count > 0 ? tokens[^1] : blankIndex;
            var (scores, duration, next) = step.Step(frame, previous, state);
            var token = ArgMax(scores.Span);
            if (token != blankIndex)
            {
                state = next;
                tokens.Add(token);
                emittedAtFrame++;
            }
            if (duration > 0)
            {
                frame += duration;
                emittedAtFrame = 0;
            }
            else if (token == blankIndex || emittedAtFrame == maxTokensPerStep)
            {
                frame++;
                emittedAtFrame = 0;
            }
        }
        return tokens;
    }

    public static int ArgMax(ReadOnlySpan<float> values)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best]) best = i;
        }
        return best;
    }
}
