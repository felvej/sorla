using System.Net.Http.Headers;
using Sorla.Core;
using Sorla.Core.Transcription;

namespace Sorla;

// One download at a time, owned by the app so it carries on if the window that started it is closed.
public sealed class ModelInstall(Settings settings, ModelHost model)
{
    private CancellationTokenSource? cancellation;

    public bool IsRunning => cancellation != null;
    public DownloadProgress? Progress { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public async Task RunAsync()
    {
        if (IsRunning) return;
        cancellation = new CancellationTokenSource();
        Error = null;
        Progress = new DownloadProgress(0, ModelFiles.TotalBytes(settings.ModelVariant), "");
        Changed?.Invoke();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Sorla-Windows", AppVersion.Current));
            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress = p;
                Changed?.Invoke();
            });
            Log.Info($"model download started ({settings.ModelVariant})");
            await new ModelDownloader(http).DownloadAsync(settings.EffectiveModelDirectory, settings.ModelVariant, progress, cancellation.Token);
            Log.Info("model download finished");
            model.Reload();
        }
        catch (Exception e) when (e is HttpRequestException or IOException or ModelVerificationException or TaskCanceledException or UnauthorizedAccessException)
        {
            Log.Error("model download failed", e);
            Error = e.Message;
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
            Changed?.Invoke();
        }
    }

    public void Cancel() => cancellation?.Cancel();
}

public static class AppVersion
{
    public static string Current => typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
