using BarkFluff.Onliner.Features.UpsertRemoteStatus;
using BarkFluff.Onliner.Messages;

using MassTransit;

namespace BarkFluff.Onliner.Tests.Features.UpsertRemoteStatus;

public class UpsertRemoteStatusCommandHandlerTests
{
    private readonly TestHelper _h = new();

    private UpsertRemoteStatusCommandHandler CreateHandler()
        => new(_h.RemotePresence, _h.PublishEndpointMock.Object, _h.Metrics);

    [Fact]
    public async Task Handle_StoresStatusInRemoteCache()
    {
        var uuid = Guid.NewGuid();
        var lastSeen = new DateTime(2026, 7, 26, 10, 0, 0, DateTimeKind.Utc);

        await CreateHandler().Handle(new UpsertRemoteStatusCommand
        {
            UserUuid = uuid,
            Status = DomainStatusTypeId.Online,
            LastSeen = lastSeen,
        }, CancellationToken.None);

        var stored = await _h.RemotePresence.GetManyAsync([uuid]);

        stored.Should().ContainKey(uuid);
        stored[uuid].Status.Should().Be(DomainStatusTypeId.Online);
        stored[uuid].LastSeen.Should().Be(lastSeen);
    }

    [Fact]
    public async Task Handle_PublishesFanOutEventWithUuid()
    {
        var uuid = Guid.NewGuid();

        await CreateHandler().Handle(new UpsertRemoteStatusCommand
        {
            UserUuid = uuid,
            Status = DomainStatusTypeId.Online,
            LastSeen = DateTime.UtcNow,
        }, CancellationToken.None);

        // UserId остаётся нулевым: у remote-пользователя локального идентификатора нет.
        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.Is<OnlineStatusChangedEvent>(e =>
                e.UserUuid == uuid
                && e.UserId == 0
                && e.Status == (int)DomainStatusTypeId.Online),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DoesNotTouchLocalPresenceStore()
    {
        // Изоляция хранилищ: remote-статус не должен попадать в sorted set, который
        // обслуживают OfflineDetectionService и DatabasePersistenceService.
        await CreateHandler().Handle(new UpsertRemoteStatusCommand
        {
            UserUuid = Guid.NewGuid(),
            Status = DomainStatusTypeId.Online,
            LastSeen = DateTime.UtcNow,
        }, CancellationToken.None);

        (await _h.Presence.GetOnlineSnapshotAsync()).Should().BeEmpty();
        (await _h.Presence.GetOnlineCountAsync()).Should().Be(0);
        _h.RemotePresence.Count.Should().Be(1);
    }

    [Fact]
    public async Task Handle_UnknownStatus_IsStoredAsUnknownNotOffline()
    {
        // Гашение статуса при обрыве S2S-стрима (этап 4.3) приходит именно как UNKNOWN.
        var uuid = Guid.NewGuid();

        await CreateHandler().Handle(new UpsertRemoteStatusCommand
        {
            UserUuid = uuid,
            Status = DomainStatusTypeId.Unknown,
            LastSeen = DateTime.UtcNow,
        }, CancellationToken.None);

        var stored = await _h.RemotePresence.GetAsync(uuid);

        stored.Should().NotBeNull();
        stored!.Value.Status.Should().Be(DomainStatusTypeId.Unknown);
    }
}
