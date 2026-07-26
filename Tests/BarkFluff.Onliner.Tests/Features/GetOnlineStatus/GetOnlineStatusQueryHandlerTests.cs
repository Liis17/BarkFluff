using BarkFluff.Onliner.Features.GetOnlineStatus;
using BarkFluff.Proto.Users;

namespace BarkFluff.Onliner.Tests.Features.GetOnlineStatus;

public class GetOnlineStatusQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private GetOnlineStatusQueryHandler CreateHandler(long callerId)
    {
        return new GetOnlineStatusQueryHandler(
            _h.Presence,
            _h.RemotePresence,
            _h.DbContext,
            _h.CreateUserContext(callerId),
            _h.CreateVisibilityFilter(),
            TestHelper.CreateLogger<GetOnlineStatusQueryHandler>());
    }

    [Fact]
    public async Task Handle_UserOnline_ReturnsStatus()
    {
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses.Should().HaveCount(1);
        result.UsersStatuses[0].UserId.Should().Be(10);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.StatusOnline);
    }

    [Fact]
    public async Task Handle_UserInDbNotOnline_ReturnsStatus()
    {
        await _h.SeedDbStatus(10, DomainStatusTypeId.Offline, DateTime.UtcNow);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses.Should().HaveCount(1);
        result.UsersStatuses[0].UserId.Should().Be(10);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.StatusOffline);
    }

    [Fact]
    public async Task Handle_PresenceTakesPrecedenceOverDb()
    {
        await _h.SeedDbStatus(10, DomainStatusTypeId.Offline, DateTime.UtcNow);
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.StatusOnline);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsUnknown()
    {
        _h.SetupUserPrivacy(999, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [999] }, CancellationToken.None);
        result.UsersStatuses.Should().HaveCount(1);
        result.UsersStatuses[0].UserId.Should().Be(999);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Handle_UserHiddenByPrivacy_ReturnsUnknown()
    {
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Handle_SelfStatus_ReturnsActualStatus()
    {
        await _h.Presence.MarkOnlineAsync(1);
        _h.SetupUserPrivacy(1, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [1] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.StatusOnline);
    }

    [Fact]
    public async Task Handle_MultipleUsers_ReturnsAll()
    {
        await _h.Presence.MarkOnlineAsync(10);
        await _h.Presence.MarkOnlineAsync(20);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        _h.SetupUserPrivacy(20, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10, 20] }, CancellationToken.None);
        result.UsersStatuses.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_MixedVisibility_ReturnsCorrectStatuses()
    {
        await _h.Presence.MarkOnlineAsync(10);
        await _h.Presence.MarkOnlineAsync(20);
        await _h.Presence.MarkOnlineAsync(1);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        _h.SetupUserPrivacy(20, ProfileFieldVisibility.None);
        _h.SetupUserPrivacy(1, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10, 20, 1] }, CancellationToken.None);
        result.UsersStatuses.First(s => s.UserId == 10).Status.Should().Be(ProtoStatusTypeId.StatusOnline);
        result.UsersStatuses.First(s => s.UserId == 20).Status.Should().Be(ProtoStatusTypeId.Unknown);
        result.UsersStatuses.First(s => s.UserId == 1).Status.Should().Be(ProtoStatusTypeId.StatusOnline);
    }

    [Fact]
    public async Task Handle_EmptyUserIds_ReturnsEmptyResponse()
    {
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [] }, CancellationToken.None);
        result.UsersStatuses.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_FriendsVisibility_ReturnsUnknown()
    {
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.Friends);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Handle_PrivacyCheckError_ReturnsUnknown()
    {
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacyError(10);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Handle_OnlineUser_MapsLastSeen()
    {
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].LastSeen.Should().NotBeNull();
        result.UsersStatuses[0].LastSeen.Seconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Handle_OfflineUserInDb_ReturnsOffline()
    {
        await _h.SeedDbStatus(10, DomainStatusTypeId.Offline, DateTime.UtcNow);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.StatusOffline);
    }

    [Fact]
    public async Task Handle_DuplicateUserIds_ReturnsStatusForEach()
    {
        await _h.Presence.MarkOnlineAsync(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10, 10] }, CancellationToken.None);
        result.UsersStatuses.Should().HaveCount(2);
    }
}
