using System.Text;
using System.Text.RegularExpressions;

namespace Sorla.Core.Transcription;

// The model's 8,192 SentencePiece tokens plus the blank, read from vocab.txt ("token id" per line).
public sealed partial class Vocabulary
{
    private const string WordBoundary = "▁";
    private readonly string[] tokens;

    public int Count => tokens.Length;
    public int BlankIndex { get; }

    public Vocabulary(IEnumerable<string> lines)
    {
        var map = new Dictionary<int, string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var split = line.LastIndexOf(' ');
            if (split <= 0 || !int.TryParse(line.AsSpan(split + 1), out var id))
            {
                throw new FormatException($"Unexpected vocab line: {line}");
            }
            map[id] = line[..split];
        }
        tokens = new string[map.Count == 0 ? 0 : map.Keys.Max() + 1];
        foreach (var (id, token) in map) tokens[id] = token;
        BlankIndex = Array.IndexOf(tokens, "<blk>");
        if (BlankIndex < 0) BlankIndex = tokens.Length - 1;
    }

    public static Vocabulary Load(string path) => new(File.ReadAllLines(path, Encoding.UTF8));

    public string Decode(IEnumerable<int> ids)
    {
        var builder = new StringBuilder();
        foreach (var id in ids)
        {
            if (id < 0 || id >= tokens.Length || id == BlankIndex) continue;
            builder.Append(tokens[id]);
        }
        return Clean(builder.ToString().Replace(WordBoundary, " "));
    }

    // Same rule as onnx-asr: no leading space, no space before punctuation, one space before a word.
    internal static string Clean(string text) =>
        SpacePattern().Replace(text, m => m.Groups[1].Success ? " " : "").TrimEnd();

    [GeneratedRegex(@"\A\s+|\s+\B|(\s+)\b")]
    private static partial Regex SpacePattern();
}
