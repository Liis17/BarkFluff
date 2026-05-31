using BarkFluff.Updates.Features.PushNotifications;

namespace BarkFluff.Updates.Tests.PushNotifications;

public class PendingPushTrackerTests
{
    [Fact]
    public void RegisterPending_ReturnsLiveToken()
    {
        var tracker = new PendingPushTracker();

        var cts = tracker.RegisterPending(1, 10);

        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void CancelPending_CancelsRegisteredToken()
    {
        var tracker = new PendingPushTracker();
        var cts = tracker.RegisterPending(1, 10);

        tracker.CancelPending(1, 10);

        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void RemovePending_DoesNotCancelToken()
    {
        var tracker = new PendingPushTracker();
        var cts = tracker.RegisterPending(1, 10);

        tracker.RemovePending(1, 10);

        // RemovePending снимает запись, но НЕ отменяет push (в отличие от CancelPending).
        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void RegisterPending_SameKeyTwice_CancelsPreviousToken()
    {
        var tracker = new PendingPushTracker();

        var first = tracker.RegisterPending(1, 10);
        var second = tracker.RegisterPending(1, 10);

        first.IsCancellationRequested.Should().BeTrue();
        second.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void CancelPending_IsScopedToMessageAndUser()
    {
        var tracker = new PendingPushTracker();
        var target = tracker.RegisterPending(1, 10);
        var otherUser = tracker.RegisterPending(1, 20);
        var otherMessage = tracker.RegisterPending(2, 10);

        tracker.CancelPending(1, 10);

        target.IsCancellationRequested.Should().BeTrue();
        otherUser.IsCancellationRequested.Should().BeFalse();
        otherMessage.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void CancelPending_UnknownKey_DoesNotThrow()
    {
        var tracker = new PendingPushTracker();

        var act = () => tracker.CancelPending(999, 999);

        act.Should().NotThrow();
    }

    [Fact]
    public void CancelPending_AfterRemove_DoesNotThrow()
    {
        var tracker = new PendingPushTracker();
        tracker.RegisterPending(1, 10);
        tracker.RemovePending(1, 10);

        var act = () => tracker.CancelPending(1, 10);

        act.Should().NotThrow();
    }
}
