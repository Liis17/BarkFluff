using BarkFluff.Onliner.Features.GetOnlineStatus;
using BarkFluff.Proto.Users;

namespace BarkFluff.Onliner.Tests.Features.GetOnlineStatus;

public class GetOnlineStatusQueryHandlerEdgeCaseTests
{
    private readonly TestHelper _h = new();

    private GetOnlineStatusQueryHandler CreateHandler(long callerId)
    {
        return new GetOnlineStatusQueryHandler(
            _h.Presence,
            _h.DbContext,
            _h.CreateUserContext(callerId),
            _h.CreateVisibilityFilter(),
            TestHelper.CreateLogger<GetOnlineStatusQueryHandler>());
    }

    [Fact]
    public async Task Handle_OfflineInDb_ReturnsProtoOffline()
    {
        await _h.SeedDbStatus(1, DomainStatusTypeId.Offline, DateTime.UtcNow);
        _h.SetupUserPrivacy(1, ProfileFieldVisibility.All);
        var handler = CreateHandler(10);

        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [1] }, CancellationToken.None);

        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.StatusOffline);
    }
}
