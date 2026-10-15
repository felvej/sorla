namespace Sorla.Core.Dictation;

public enum RecordingMode
{
    PushToTalk,
    Toggle,
}

public enum GestureAction
{
    Start,
    Finish,
    // A dictation the user could see or hear was called off, so Sorla says so.
    Cancel,
    // The press never became a dictation (too short, or a Ctrl-shortcut), so it goes quietly.
    Discard,
}

public abstract record GestureEvent
{
    public sealed record TriggerDown(double At) : GestureEvent;
    public sealed record TriggerUp(double At) : GestureEvent;
    public sealed record OtherKeyDown(double At) : GestureEvent;
    public sealed record Click(double At) : GestureEvent;
    public sealed record Escape : GestureEvent;
}

// Port of the Mac PushToTalkGesture: turns key and click events into start/finish/cancel decisions.
public sealed class PushToTalkGesture(RecordingMode mode = RecordingMode.PushToTalk, double minimumHold = PushToTalkGesture.DefaultMinimumHold)
{
    public const double DefaultMinimumHold = 0.3;

    private enum Kind
    {
        Idle,
        Holding,
        HeldDirty,
        Cancelled,
        Recording,
        RecordingHeld,
        RecordingHeldDirty,
        External,
        ExternalHeld,
        ExternalHeldDirty,
    }

    private Kind state = Kind.Idle;
    private double since;

    public RecordingMode Mode => mode;

    // A press is in progress, so a lost release would leave the gesture stuck.
    public bool IsWaitingForRelease => state is Kind.Holding or Kind.HeldDirty or Kind.Cancelled
        or Kind.RecordingHeld or Kind.RecordingHeldDirty or Kind.ExternalHeld or Kind.ExternalHeldDirty;

    public GestureAction? Handle(GestureEvent e)
    {
        if (e is GestureEvent.Escape) return HandleEscape();
        switch (state)
        {
            case Kind.External or Kind.ExternalHeld or Kind.ExternalHeldDirty:
                return HandleExternal(e);
            case Kind.Cancelled:
                if (e is GestureEvent.TriggerUp) state = Kind.Idle;
                return null;
        }
        return mode == RecordingMode.PushToTalk ? HandlePushToTalk(e) : HandleToggle(e);
    }

    // Esc calls off whatever is recording, however it was started; a held key must still be let go first.
    private GestureAction? HandleEscape()
    {
        switch (state)
        {
            case Kind.Holding when mode == RecordingMode.PushToTalk:
            case Kind.RecordingHeld or Kind.RecordingHeldDirty or Kind.ExternalHeld or Kind.ExternalHeldDirty:
                state = Kind.Cancelled;
                return GestureAction.Cancel;
            case Kind.Recording or Kind.External:
                state = Kind.Idle;
                return GestureAction.Cancel;
            case Kind.Holding:
                state = Kind.HeldDirty;
                return null;
            default:
                return null;
        }
    }

    private GestureAction? HandlePushToTalk(GestureEvent e)
    {
        switch (state, e)
        {
            case (Kind.Idle, GestureEvent.TriggerDown down):
                state = Kind.Holding;
                since = down.At;
                return GestureAction.Start;

            case (Kind.Holding, GestureEvent.TriggerUp up):
                state = Kind.Idle;
                return IsPastMinimumHold(up.At) ? GestureAction.Finish : GestureAction.Discard;

            // A key with the trigger held is a shortcut (Ctrl+C), not speech.
            case (Kind.Holding, GestureEvent.OtherKeyDown key):
                state = Kind.Cancelled;
                return IsPastMinimumHold(key.At) ? GestureAction.Cancel : GestureAction.Discard;

            // Past the minimum hold a click is a stray one, not a Ctrl+click.
            case (Kind.Holding, GestureEvent.Click click):
                if (IsPastMinimumHold(click.At)) return null;
                state = Kind.Cancelled;
                return GestureAction.Discard;

            default:
                return null;
        }
    }

    // A clean tap (down+up, no key or click between) toggles idle->recording or recording->finished/cancelled.
    private GestureAction? HandleToggle(GestureEvent e)
    {
        switch (state, e)
        {
            case (Kind.Idle, GestureEvent.TriggerDown down):
                state = Kind.Holding;
                since = down.At;
                return null;

            case (Kind.Holding, GestureEvent.TriggerUp):
                state = Kind.Recording;
                return GestureAction.Start;

            case (Kind.Holding, GestureEvent.OtherKeyDown or GestureEvent.Click):
                state = Kind.HeldDirty;
                return null;

            case (Kind.HeldDirty, GestureEvent.TriggerUp):
                state = Kind.Idle;
                return null;

            case (Kind.Recording, GestureEvent.TriggerDown):
                state = Kind.RecordingHeld;
                return null;

            case (Kind.RecordingHeld, GestureEvent.TriggerUp up):
                state = Kind.Idle;
                return IsPastMinimumHold(up.At) ? GestureAction.Finish : GestureAction.Cancel;

            case (Kind.RecordingHeld, GestureEvent.OtherKeyDown or GestureEvent.Click):
                state = Kind.RecordingHeldDirty;
                return null;

            case (Kind.RecordingHeldDirty, GestureEvent.TriggerUp):
                state = Kind.Recording;
                return null;

            default:
                return null;
        }
    }

    // A dictation started from the menu wasn't started by the key, so only a clean press and release stops it.
    public void RecordingStartedElsewhere() => state = Kind.External;

    private GestureAction? HandleExternal(GestureEvent e)
    {
        switch (state, e)
        {
            case (Kind.External, GestureEvent.TriggerDown):
                state = Kind.ExternalHeld;
                return null;
            case (Kind.ExternalHeld, GestureEvent.OtherKeyDown or GestureEvent.Click):
                state = Kind.ExternalHeldDirty;
                return null;
            case (Kind.ExternalHeld, GestureEvent.TriggerUp):
                state = Kind.Idle;
                return GestureAction.Finish;
            case (Kind.ExternalHeldDirty, GestureEvent.TriggerUp):
                state = Kind.External;
                return null;
            default:
                return null;
        }
    }

    private bool IsPastMinimumHold(double at) => at - since >= minimumHold;

    public void Reset() => state = Kind.Idle;
}
