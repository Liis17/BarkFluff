using BarkFluff.Onliner.Features.GetLocalPresence;
using BarkFluff.Proto.Users;

namespace BarkFluff.Onliner.Tests.Features.GetLocalPresence;

public class GetLocalPresenceQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private GetLocalPresenceQueryHandler CreateHandler()
        => new(_h.Presence, _h.DbContext, _h.CreateVisibilityFilter());

    [Fact]
    public async Task Handle_VisibleOnlineUser_ReturnsRealStatus()
    {
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);

        var response = await CreateHandler().Handle(
            new GetLocalPresenceQuery { UserIds = [10] }, CancellationToken.None);

        var status = response.Statuses.Should().ContainSingle().Subject;
        status.UserId.Should().Be(10);
        status.Status.Should().Be(ProtoStatusTypeId.StatusOnline);
    }

    [Theory]
    [InlineData(ProfileFieldVisibility.None)]
    [InlineData(ProfileFieldVisibility.Friends)]
    public async Task Handle_HiddenByPrivacy_ReturnsUnknownNotRealStatus(ProfileFieldVisibility visibility)
    {
        // Privacy применяет владелец данных (инвариант №27): реальный статус наружу не протекает.
        // FRIENDS === NONE, пока нет сервиса отношений.
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacy(10, visibility);

        var response = await CreateHandler().Handle(
            new GetLocalPresenceQuery { UserIds = [10] }, CancellationToken.None);

        response.Statuses.Should().ContainSingle()
            .Which.Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Handle_UsersServiceFailure_ReturnsUnknownFailClosed()
    {
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacyError(10);

        var response = await CreateHandler().Handle(
            new GetLocalPresenceQuery { UserIds = [10] }, CancellationToken.None);

        response.Statuses.Should().ContainSingle()
            .Which.Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Handle_OfflineUserWithDbRecord_ReturnsPersistedLastSeen()
    {
        var lastSeen = new DateTime(2026, 7, 20, 8, 30, 0, DateTimeKind.Utc);
        await _h.SeedDbStatus(10, DomainStatusTypeId.Offline, lastSeen);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);

        var response = await CreateHandler().Handle(
            new GetLocalPresenceQuery { UserIds = [10] }, CancellationToken.None);

        var status = response.Statuses.Should().ContainSingle().Subject;
        status.Status.Should().Be(ProtoStatusTypeId.StatusOffline);
        status.LastSeen.ToDateTime().Should().Be(lastSeen);
    }

    [Fact]
    public async Task Handle_UnknownUser_ReturnsUnknown()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);

        var response = await CreateHandler().Handle(
            new GetLocalPresenceQuery { UserIds = [10] }, CancellationToken.None);

        response.Statuses.Should().ContainSingle()
            .Which.Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Handle_DuplicateIds_AreCollapsed()
    {
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);

        var response = await CreateHandler().Handle(
            new GetLocalPresenceQuery { UserIds = [10, 10, 10] }, CancellationToken.None);

        response.Statuses.Should().ContainSingle();
    }
}
