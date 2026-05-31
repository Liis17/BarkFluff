using BarkFluff.Updates.Features.Shared;

using Grpc.Core;

namespace BarkFluff.Updates.Tests.Shared;

// Тестовое событие и конкретные наследники абстрактных базовых менеджеров.
// Базы используются 5 (User) и 3 (Device) реальными StreamSubscriptionsManager.
public sealed class TestEvent { }

public sealed class TestUserSubscriptions : UserStreamSubscriptionsBase<TestEvent> { }

public sealed class TestDeviceSubscriptions : DeviceStreamSubscriptionsBase<TestEvent> { }

public class UserStreamSubscriptionsBaseTests
{
    private static IServerStreamWriter<TestEvent> Stream() => Mock.Of<IServerStreamWriter<TestEvent>>();

    [Fact]
    public void RegisterSubscription_AddsStreamAndIncrementsCount()
    {
        var subs = new TestUserSubscriptions();
        var stream = Stream();

        subs.RegisterSubscription(1, stream);

        subs.ActiveCount.Should().Be(1);
        subs.GetUserStreams(1).Should().ContainSingle().Which.Should().BeSameAs(stream);
    }

    [Fact]
    public void GetUserStreams_ReturnsAllStreamsForUser()
    {
        var subs = new TestUserSubscriptions();
        var s1 = Stream();
        var s2 = Stream();

        subs.RegisterSubscription(1, s1);
        subs.RegisterSubscription(1, s2);

        subs.GetUserStreams(1).Should().BeEquivalentTo(new[] { s1, s2 });
        subs.ActiveCount.Should().Be(2);
    }

    [Fact]
    public void RemoveSubscription_RemovesStreamAndDecrementsCount()
    {
        var subs = new TestUserSubscriptions();
        var id = subs.RegisterSubscription(1, Stream());

        subs.RemoveSubscription(1, id);

        subs.ActiveCount.Should().Be(0);
        subs.GetUserStreams(1).Should().BeEmpty();
    }

    [Fact]
    public void RemoveSubscription_UnknownId_DoesNotChangeCount()
    {
        var subs = new TestUserSubscriptions();
        subs.RegisterSubscription(1, Stream());

        subs.RemoveSubscription(1, Guid.NewGuid());

        subs.ActiveCount.Should().Be(1);
    }

    [Fact]
    public void RemoveSubscription_Twice_DecrementsOnlyOnce()
    {
        var subs = new TestUserSubscriptions();
        subs.RegisterSubscription(1, Stream());
        var id = subs.RegisterSubscription(1, Stream());

        subs.RemoveSubscription(1, id);
        subs.RemoveSubscription(1, id);

        subs.ActiveCount.Should().Be(1);
    }

    [Fact]
    public void GetUserStreams_NoSubscriptions_ReturnsEmpty()
    {
        var subs = new TestUserSubscriptions();

        subs.GetUserStreams(42).Should().BeEmpty();
    }

    [Fact]
    public void Subscriptions_AreIsolatedByUser()
    {
        var subs = new TestUserSubscriptions();
        var s1 = Stream();

        subs.RegisterSubscription(1, s1);

        subs.GetUserStreams(2).Should().BeEmpty();
        subs.GetUserStreams(1).Should().ContainSingle().Which.Should().BeSameAs(s1);
    }

    [Fact]
    public void ConcurrentRegister_CountMatchesRegistrations()
    {
        var subs = new TestUserSubscriptions();

        Parallel.For(0, 200, _ => subs.RegisterSubscription(1, Stream()));

        subs.ActiveCount.Should().Be(200);
        subs.GetUserStreams(1).Should().HaveCount(200);
    }
}

public class DeviceStreamSubscriptionsBaseTests
{
    private static readonly Guid DeviceA = Guid.NewGuid();
    private static readonly Guid DeviceB = Guid.NewGuid();

    private static IServerStreamWriter<TestEvent> Stream() => Mock.Of<IServerStreamWriter<TestEvent>>();

    [Fact]
    public void RegisterSubscription_AddsStreamAndIncrementsCount()
    {
        var subs = new TestDeviceSubscriptions();
        var stream = Stream();

        subs.RegisterSubscription(1, DeviceA, stream);

        subs.ActiveCount.Should().Be(1);
        subs.GetDeviceStreams(1, DeviceA).Should().ContainSingle().Which.Should().BeSameAs(stream);
    }

    [Fact]
    public void HasActiveStreams_TrueAfterRegister_FalseAfterRemove()
    {
        var subs = new TestDeviceSubscriptions();
        var id = subs.RegisterSubscription(1, DeviceA, Stream());

        subs.HasActiveStreams(1, DeviceA).Should().BeTrue();

        subs.RemoveSubscription(1, DeviceA, id);

        subs.HasActiveStreams(1, DeviceA).Should().BeFalse();
        subs.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void Subscriptions_AreIsolatedByDevice()
    {
        var subs = new TestDeviceSubscriptions();
        var streamA = Stream();

        subs.RegisterSubscription(1, DeviceA, streamA);

        subs.GetDeviceStreams(1, DeviceB).Should().BeEmpty();
        subs.HasActiveStreams(1, DeviceB).Should().BeFalse();
        subs.GetDeviceStreams(1, DeviceA).Should().ContainSingle().Which.Should().BeSameAs(streamA);
    }

    [Fact]
    public void GetDeviceStreams_Unknown_ReturnsEmpty()
    {
        var subs = new TestDeviceSubscriptions();

        subs.GetDeviceStreams(1, DeviceA).Should().BeEmpty();
        subs.HasActiveStreams(1, DeviceA).Should().BeFalse();
    }

    [Fact]
    public void RemoveSubscription_Twice_DecrementsOnlyOnce()
    {
        var subs = new TestDeviceSubscriptions();
        subs.RegisterSubscription(1, DeviceA, Stream());
        var id = subs.RegisterSubscription(1, DeviceA, Stream());

        subs.RemoveSubscription(1, DeviceA, id);
        subs.RemoveSubscription(1, DeviceA, id);

        subs.ActiveCount.Should().Be(1);
    }
}
