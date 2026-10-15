namespace Sorla.Core.Transcription;

public sealed record DownloadProgress(long BytesDone, long BytesTotal, string CurrentFile)
{
    public double Fraction => BytesTotal == 0 ? 0 : (double)BytesDone / BytesTotal;
}

public sealed class ModelVerificationException(string file) : Exception($"{file} did not match its expected SHA-256.")
{
    public string File { get; } = file;
}

// The only network use in Sorla: fetching the model once from Hugging Face. Each file goes to a .part file,
// is checked against its pinned SHA-256 and only then takes its real name, so a broken download is never loaded.
public sealed class ModelDownloader(HttpClient http)
{
    public async Task DownloadAsync(string directory, ModelVariant variant, IProgress<DownloadProgress>? progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        var files = ModelFiles.Required(variant);
        var total = files.Sum(f => f.Bytes);
        long done = 0;

        foreach (var file in files)
        {
            var target = Path.Combine(directory, file.Name);
            if (File.Exists(target) && new FileInfo(target).Length == file.Bytes && ModelFiles.HasExpectedHash(target, file))
            {
                done += file.Bytes;
                progress?.Report(new DownloadProgress(done, total, file.Name));
                continue;
            }

            var partial = target + ".part";
            var start = done;
            using (var response = await http.GetAsync(ModelFiles.DownloadUri(file), HttpCompletionOption.ResponseHeadersRead, cancellation))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellation);
                await using var sink = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var buffer = new byte[1 << 16];
                long fileDone = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    await sink.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    fileDone += read;
                    progress?.Report(new DownloadProgress(start + fileDone, total, file.Name));
                }
            }

            if (!ModelFiles.HasExpectedHash(partial, file))
            {
                File.Delete(partial);
                throw new ModelVerificationException(file.Name);
            }
            File.Move(partial, target, overwrite: true);
            done = start + file.Bytes;
        }
    }
}
