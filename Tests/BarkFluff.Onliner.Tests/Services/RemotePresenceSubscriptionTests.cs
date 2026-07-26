using BarkFluff.Onliner.Features.SubscribeToOnlineStatus;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Users;

namespace BarkFluff.Onliner.Tests.Services;

/// <summary>
/// uuid-ветка подписок на статусы (этап 4.2): начальный снимок, доставка изменений,
/// чистка индекса и неизменность локального поведения.
/// </summary>
public class RemotePresenceSubscriptionTests
{
    private readonly TestHelper _h = new();

    private SubscribeToOnlineStatusQueryHandler CreateHandler(long userId)
        => new(
            _h.CreateUserContext(userId),
            _h.SubscriptionsManager,
            _h.CreateVisibilityFilter(),
            _h.RemotePresence,
            _h.Metrics,
            TestHelper.CreateLogger<SubscribeToOnlineStatusQueryHandler>());

    private async Task RunSubscription(
        long userId,
        List<long> userIds,
        List<Guid> uuids,
        Grpc.Core.IServerStreamWriter<ProtoUserOnlineStatus> stream,
        int delayMs = 150)
    {
        using var cts = new CancellationTokenSource(delayMs);
        await CreateHandler(userId).Handle(new SubscribeToOnlineStatusQuery
        {
            UserIds = userIds,
            UserUuids = uuids,
            ResponseStream = stream,
            CancellationToken = cts.Token,
        });
    }

    [Fact]
    public async Task Subscribe_SendsInitialSnapshotOfKnownRemoteStatuses()
    {
        var uuid = Guid.NewGuid();
        var lastSeen = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);
        await _h.RemotePresence.UpsertAsync(uuid, DomainStatusTypeId.Online, lastSeen);

        var (stream, received) = TestHelper.CreateCollectingStatusStream();

        await RunSubscription(1, [], [uuid], stream.Object);

        var status = received.Should().ContainSingle().Subject;
        status.UserUuid.Should().Be(uuid.ToString());
        status.Status.Should().Be(ProtoStatusTypeId.StatusOnline);
        status.UserId.Should().Be(0);
    }

    [Fact]
    public async Task Subscribe_UnknownUuid_YieldsUnknownNotOffline()
    {
        // «Нода-партнёр статус не отдаёт» и «человек не в сети» — разные вещи.
        var uuid = Guid.NewGuid();
        var (stream, received) = TestHelper.CreateCollectingStatusStream();

        await RunSubscription(1, [], [uuid], stream.Object);

        received.Should().ContainSingle()
            .Which.Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Subscribe_RegistersUuidIndexAndCleansItOnDisconnect()
    {
        var uuid = Guid.NewGuid();
        var (stream, _) = TestHelper.CreateCollectingStatusStream();

        var subscription = RunSubscription(1, [], [uuid], stream.Object, delayMs: 300);

        await Task.Delay(100);
        _h.SubscriptionsManager.GetTrackedUuids().Should().Contain(uuid);
        _h.SubscriptionsManager.GetStreamsTrackingUuid(uuid).Should().ContainSingle();

        await subscription;

        // Утечки в обратном индексе быть не должно — иначе интерес-репортер будет вечно
        // держать подписку на чужой ноде.
        _h.SubscriptionsManager.GetTrackedUuids().Should().BeEmpty();
        _h.SubscriptionsManager.GetStreamsTrackingUuid(uuid).Should().BeEmpty();
    }

    [Fact]
    public async Task Notifier_DeliversRemoteStatusChangeToUuidSubscribers()
    {
        var uuid = Guid.NewGuid();
        var (stream, received) = TestHelper.CreateCollectingStatusStream();

        _h.SubscriptionsManager.RegisterSubscription(1, [], stream.Object, [uuid]);

        await _h.Notifier.NotifyRemoteStatusChanged(uuid, DomainStatusTypeId.Offline, DateTime.UtcNow);

        var status = received.Should().ContainSingle().Subject;
        status.UserUuid.Should().Be(uuid.ToString());
        status.Status.Should().Be(ProtoStatusTypeId.StatusOffline);
    }

    [Fact]
    public async Task Subscribe_LocalOnly_BehavesExactlyAsBefore()
    {
        // Регрессия: клиент, подписанный только по user_ids, не должен получить ни одного
        // лишнего сообщения из-за появления uuid-ветки.
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var (stream, received) = TestHelper.CreateCollectingStatusStream();

        await RunSubscription(1, [10], [], stream.Object);

        received.Should().BeEmpty();
        _h.SubscriptionsManager.GetTrackedUuids().Should().BeEmpty();
    }

    [Fact]
    public void UpdateAllSubscriptions_ReplacesUuidSet()
    {
        var oldUuid = Guid.NewGuid();
        var newUuid = Guid.NewGuid();
        var (stream, _) = TestHelper.CreateCollectingStatusStream();

        _h.SubscriptionsManager.RegisterSubscription(1, [], stream.Object, [oldUuid]);
        _h.SubscriptionsManager.UpdateAllSubscriptions(1, [], [newUuid]);

        _h.SubscriptionsManager.GetTrackedUuids().Should().BeEquivalentTo([newUuid]);
        _h.SubscriptionsManager.GetStreamsTrackingUuid(oldUuid).Should().BeEmpty();
    }

    [Fact]
    public void ParseUuids_DropsGarbageAndDuplicatesWithoutFailing()
    {
        var uuid = Guid.NewGuid();

        var parsed = PresenceUuids.Parse([uuid.ToString(), "not-a-uuid", uuid.ToString(), ""]);

        parsed.Should().BeEquivalentTo([uuid]);
    }
}
