namespace Sorla.Core.Dictation;

// Paste Last only recovers a paste that just went wrong, so the text is kept in memory for a few minutes at most.
public sealed class RecentTranscript
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private string? text;

    public DateTime? ExpiresAt { get; private set; }

    // Bumped by Forget(), so a transcription that was in flight when the PC locked is dropped too.
    public int Generation { get; private set; }

    public void Store(string value, DateTime now)
    {
        text = value;
        ExpiresAt = now + Lifetime;
    }

    // Checked on every use as well, since a PC that slept may have missed the one-shot expiry.
    public string? Text(DateTime now)
    {
        if (ExpiresAt is { } expires && now >= expires) Clear();
        return text;
    }

    public void Clear()
    {
        text = null;
        ExpiresAt = null;
    }

    public void Forget()
    {
        Clear();
        Generation++;
    }

    // A transcription begun before the last Forget() may neither be pasted nor kept.
    public bool Accepts(int generation) => generation == Generation;
}

// A forgotten toggle recording would keep the microphone open, so a dictation stops by itself after five minutes.
public sealed record RecordingLimit(TimeSpan Maximum, TimeSpan WarningLead)
{
    public static readonly RecordingLimit Standard = new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10));

    public TimeSpan WarningAt => Maximum - WarningLead < TimeSpan.Zero ? TimeSpan.Zero : Maximum - WarningLead;

    public bool ShouldWarn(TimeSpan elapsed) => elapsed >= WarningAt;

    public bool ShouldStop(TimeSpan elapsed) => elapsed >= Maximum;
}

public static class DeliveryRules
{
    // The foreground window may have changed between release and delivery; a paste would land in the wrong place.
    public static bool ShouldAutoPaste(nint windowAtRelease, nint windowAtDelivery) =>
        windowAtRelease == 0 || windowAtDelivery == 0 || windowAtRelease == windowAtDelivery;

    // Only put the user's clipboard back if nothing else has written to it since Sorla did.
    public static bool ShouldRestoreClipboard(bool keepSetting, bool pasteDelivered, uint sequenceAfterWrite, uint currentSequence) =>
        keepSetting && pasteDelivered && sequenceAfterWrite == currentSequence;

    // Shortest audio worth sending to the model; anything shorter is a stray tap.
    public const double MinimumSpeechSeconds = 0.25;

    // Audio this quiet throughout means a muted or dead microphone, not speech.
    public const float SilencePeak = 0.003f;
}
