using System.Security.Cryptography;

namespace Sorla.Core.Transcription;

public enum ModelVariant
{
    Int8,
    Int4,
}

public sealed record ModelFile(string Name, long Bytes, string Sha256);

// Klang's own ONNX export of Pianissimo, pinned to one revision with the hashes from its manifest.json,
// so what is downloaded is exactly what was tested.
public static class ModelFiles
{
    public const string Repository = "KlangAI/pianissimo-sv-onnx";
    public const string Revision = "32118e6c01a88e3c4b4b735971195303a88d21ce";
    public const string DirectoryName = "pianissimo-sv-onnx";

    public const string Preprocessor = "nemo128.onnx";
    public const string Vocabulary = "vocab.txt";

    private static readonly ModelFile[] Shared =
    [
        new("config.json", 116, "f19eee59d2ba995f6d5cdb164e30dd64d2a8b7ed0ce9f2198ab68a5467445694"),
        new(Preprocessor, 138_824, "5b4a84c52eeaa615dc46d781cc7e4598f9b432184831d57493c48adcd01371a9"),
        new(Vocabulary, 93_939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d"),
    ];

    public static (string Encoder, string DecoderJoint) For(ModelVariant variant) => variant switch
    {
        ModelVariant.Int4 => ("encoder-model.int4.onnx", "decoder_joint-model.int4.onnx"),
        _ => ("encoder-model.int8.onnx", "decoder_joint-model.int8.onnx"),
    };

    public static IReadOnlyList<ModelFile> Required(ModelVariant variant) => variant switch
    {
        ModelVariant.Int4 =>
        [
            .. Shared,
            new("decoder_joint-model.int4.onnx", 28_378_227, "bf9b99051419623751e34d542c0076ab50f84bbde4f68f095850b2d4cefdafcd"),
            new("encoder-model.int4.onnx", 486_421_211, "689b18b1c58df738e4a309757a817f4a8fcdad8e99d770f6810b3449be95b729"),
        ],
        _ =>
        [
            .. Shared,
            new("decoder_joint-model.int8.onnx", 30_025_652, "0f9213242acd8874f2717c5e3cdb888d4e7673ddf3abb9ee3a2fd2eaf0acdd03"),
            new("encoder-model.int8.onnx", 630_313_965, "13288a5f4f009bf6bf8a3260044d098f6922e8caf1c564521d4962fa5749f66c"),
        ],
    };

    public static long TotalBytes(ModelVariant variant) => Required(variant).Sum(f => f.Bytes);

    public static string DefaultDirectory => Path.Combine(AppPaths.LocalData, "models", DirectoryName);

    public static Uri DownloadUri(ModelFile file) =>
        new($"https://huggingface.co/{Repository}/resolve/{Revision}/{file.Name}");

    // A cheap check at launch: every file there with the right size. Hashes are checked once, after download.
    public static bool IsInstalled(string directory, ModelVariant variant) =>
        Required(variant).All(f =>
        {
            var info = new FileInfo(Path.Combine(directory, f.Name));
            return info.Exists && info.Length == f.Bytes;
        });

    public static bool HasExpectedHash(string path, ModelFile file)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream)) == file.Sha256;
    }
}
