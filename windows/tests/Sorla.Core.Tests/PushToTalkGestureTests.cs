using Sorla.Core.Dictation;
using static Sorla.Core.Dictation.GestureEvent;

namespace Sorla.Core.Tests;

public class PushToTalkGestureTests
{
    [Fact]
    public void HoldAndReleasePastMinimumFinishes()
    {
        var g = new PushToTalkGesture();
        Assert.Equal(GestureAction.Start, g.Handle(new TriggerDown(0)));
        Assert.True(g.IsWaitingForRelease);
        Assert.Equal(GestureAction.Finish, g.Handle(new TriggerUp(1)));
        Assert.False(g.IsWaitingForRelease);
    }

    [Fact]
    public void ShortTapIsDiscarded()
    {
        var g = new PushToTalkGesture();
        g.Handle(new TriggerDown(0));
        Assert.Equal(GestureAction.Discard, g.Handle(new TriggerUp(0.1)));
    }

    [Fact]
    public void ShortcutWithTriggerHeldIsDiscardedQuickly()
    {
        var g = new PushToTalkGesture();
        g.Handle(new TriggerDown(0));
        Assert.Equal(GestureAction.Discard, g.Handle(new OtherKeyDown(0.1)));
        Assert.Null(g.Handle(new TriggerUp(0.2)));
        // The next press starts afresh.
        Assert.Equal(GestureAction.Start, g.Handle(new TriggerDown(1)));
    }

    [Fact]
    public void KeyAfterMinimumHoldCancelsAudibly()
    {
        var g = new PushToTalkGesture();
        g.Handle(new TriggerDown(0));
        Assert.Equal(GestureAction.Cancel, g.Handle(new OtherKeyDown(1)));
        Assert.Null(g.Handle(new TriggerUp(2)));
    }

    [Fact]
    public void LateClickIsIgnoredEarlyClickDiscards()
    {
        var late = new PushToTalkGesture();
        late.Handle(new TriggerDown(0));
        Assert.Null(late.Handle(new Click(1)));
        Assert.Equal(GestureAction.Finish, late.Handle(new TriggerUp(2)));

        var early = new PushToTalkGesture();
        early.Handle(new TriggerDown(0));
        Assert.Equal(GestureAction.Discard, early.Handle(new Click(0.1)));
    }

    [Fact]
    public void EscapeCancelsWhileHolding()
    {
        var g = new PushToTalkGesture();
        g.Handle(new TriggerDown(0));
        Assert.Equal(GestureAction.Cancel, g.Handle(new Escape()));
        Assert.Null(g.Handle(new TriggerUp(1)));
        Assert.False(g.IsWaitingForRelease);
    }

    [Fact]
    public void ToggleStartsOnTapAndFinishesOnSecondTap()
    {
        var g = new PushToTalkGesture(RecordingMode.Toggle);
        Assert.Null(g.Handle(new TriggerDown(0)));
        Assert.Equal(GestureAction.Start, g.Handle(new TriggerUp(0.1)));
        Assert.False(g.IsWaitingForRelease);
        Assert.Null(g.Handle(new TriggerDown(3)));
        Assert.Equal(GestureAction.Finish, g.Handle(new TriggerUp(3.1)));
    }

    [Fact]
    public void ToggleShortcutDoesNotStartOrStop()
    {
        var g = new PushToTalkGesture(RecordingMode.Toggle);
        g.Handle(new TriggerDown(0));
        g.Handle(new OtherKeyDown(0.05));
        Assert.Null(g.Handle(new TriggerUp(0.1)));

        g.Handle(new TriggerDown(1));
        Assert.Equal(GestureAction.Start, g.Handle(new TriggerUp(1.1)));
        g.Handle(new TriggerDown(2));
        g.Handle(new OtherKeyDown(2.05));
        Assert.Null(g.Handle(new TriggerUp(2.1)));
        Assert.Equal(GestureAction.Cancel, g.Handle(new Escape()));
    }

    [Fact]
    public void ExternalStartStopsOnlyOnCleanPress()
    {
        var g = new PushToTalkGesture();
        g.RecordingStartedElsewhere();
        g.Handle(new TriggerDown(0));
        g.Handle(new OtherKeyDown(0.1));
        Assert.Null(g.Handle(new TriggerUp(0.2)));
        g.Handle(new TriggerDown(1));
        Assert.Equal(GestureAction.Finish, g.Handle(new TriggerUp(1.1)));
    }
}
