using System.Diagnostics;
using Sorla.Core;
using Sorla.Core.Transcription;

namespace Sorla;

public enum ModelState
{
    Missing,
    Loading,
    Ready,
    Failed,
}

// Owns the loaded model. Loading takes several seconds, so it runs in the background at launch.
public sealed class ModelHost : IDisposable
{
    private readonly Settings settings;
    private readonly SynchronizationContext context;
    private Task<PianissimoTranscriber>? loading;
    private PianissimoTranscriber? transcriber;

    public ModelState State { get; private set; } = ModelState.Missing;
    public string? FailureMessage { get; private set; }
    public event Action? StateChanged;

    public ModelHost(Settings settings)
    {
        this.settings = settings;
        context = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public string Directory => settings.EffectiveModelDirectory;
    public bool IsInstalled => ModelFiles.IsInstalled(Directory, settings.ModelVariant);

    public void Load()
    {
        if (!IsInstalled)
        {
            SetState(ModelState.Missing);
            return;
        }
        if (State == ModelState.Loading) return;
        SetState(ModelState.Loading);
        var directory = Directory;
        var variant = settings.ModelVariant;
        loading = Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            var model = new PianissimoTranscriber(directory, variant);
            model.WarmUp();
            Log.Info($"model ready ({variant}) in {clock.ElapsedMilliseconds} ms");
            return model;
        });
        loading.ContinueWith(task => context.Post(_ =>
        {
            if (task.IsCompletedSuccessfully)
            {
                transcriber?.Dispose();
                transcriber = task.Result;
                SetState(ModelState.Ready);
            }
            else
            {
                var error = task.Exception?.GetBaseException();
                FailureMessage = error?.Message;
                if (error != null) Log.Error("model load failed", error);
                SetState(ModelState.Failed);
            }
        }, null), TaskScheduler.Default);
    }

    public void Reload()
    {
        transcriber?.Dispose();
        transcriber = null;
        State = ModelState.Missing;
        Load();
    }

    // Waits for a load in progress, so a dictation made in the first seconds after launch is still transcribed.
    public async Task<ITranscriber?> GetAsync()
    {
        if (transcriber != null) return transcriber;
        if (loading == null) return null;
        try
        {
            return await loading;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SetState(ModelState next)
    {
        State = next;
        StateChanged?.Invoke();
    }

    public void Dispose() => transcriber?.Dispose();
}
