using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Features.GetOnlineStatus;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Users;

namespace BarkFluff.Onliner.Tests.Features.GetOnlineStatus;

public class GetOnlineStatusQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private GetOnlineStatusQueryHandler CreateHandler(long callerId)
    {
        return new GetOnlineStatusQueryHandler(
            _h.Storage,
            _h.DbContext,
            _h.CreateUserContext(callerId),
            _h.CreateVisibilityFilter(),
            TestHelper.CreateLogger<GetOnlineStatusQueryHandler>());
    }

    [Fact]
    public async Task Handle_UserInMemory_ReturnsStatus()
    {
        _h.Storage.UpdateStatus(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses.Should().HaveCount(1);
        result.UsersStatuses[0].UserId.Should().Be(10);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.StatusOnline);
    }

    [Fact]
    public async Task Handle_UserInDbNotInMemory_ReturnsStatus()
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
    public async Task Handle_MemoryTakesPrecedenceOverDb()
    {
        await _h.SeedDbStatus(10, DomainStatusTypeId.Offline, DateTime.UtcNow);
        _h.Storage.UpdateStatus(10);
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
        _h.Storage.UpdateStatus(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Handle_SelfStatus_ReturnsActualStatus()
    {
        _h.Storage.UpdateStatus(1);
        _h.SetupUserPrivacy(1, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [1] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.StatusOnline);
    }

    [Fact]
    public async Task Handle_MultipleUsers_ReturnsAll()
    {
        _h.Storage.UpdateStatus(10);
        _h.Storage.UpdateStatus(20);
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
        _h.Storage.UpdateStatus(10);
        _h.Storage.UpdateStatus(20);
        _h.Storage.UpdateStatus(1);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        _h.SetupUserPrivacy(20, ProfileFieldVisibility.None);
        _h.SetupUserPrivacy(1, ProfileFieldVisibility.None);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10, 20, 1] }, CancellationToken.None);
        var status10 = result.UsersStatuses.First(s => s.UserId == 10);
        var status20 = result.UsersStatuses.First(s => s.UserId == 20);
        var status1 = result.UsersStatuses.First(s => s.UserId == 1);
        status10.Status.Should().Be(ProtoStatusTypeId.StatusOnline);
        status20.Status.Should().Be(ProtoStatusTypeId.Unknown);
        status1.Status.Should().Be(ProtoStatusTypeId.StatusOnline);
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
        _h.Storage.UpdateStatus(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.Friends);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Handle_PrivacyCheckError_ReturnsUnknown()
    {
        _h.Storage.UpdateStatus(10);
        _h.SetupUserPrivacyError(10);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.Unknown);
    }

    [Fact]
    public async Task Handle_OnlineUserInMemory_MapsLastSeen()
    {
        _h.Storage.UpdateStatus(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].LastSeen.Should().NotBeNull();
        result.UsersStatuses[0].LastSeen.Seconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Handle_OfflineUserInMemory_ReturnsOffline()
    {
        _h.Storage.UpdateStatus(10);
        _h.Storage.SetOffline(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10] }, CancellationToken.None);
        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.StatusOffline);
    }

    [Fact]
    public async Task Handle_DuplicateUserIds_ReturnsStatusForEach()
    {
        _h.Storage.UpdateStatus(10);
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var handler = CreateHandler(1);
        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [10, 10] }, CancellationToken.None);
        result.UsersStatuses.Should().HaveCount(2);
    }
}
