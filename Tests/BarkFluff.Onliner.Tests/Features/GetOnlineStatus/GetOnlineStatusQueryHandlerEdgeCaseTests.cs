using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Features.GetOnlineStatus;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Users;

namespace BarkFluff.Onliner.Tests.Features.GetOnlineStatus;

public class GetOnlineStatusQueryHandlerEdgeCaseTests
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
    public async Task Handle_UnknownStatusInStorage_ReturnsProtoUnknown()
    {
        _h.Storage.SetOffline(1);
        _h.SetupUserPrivacy(1, ProfileFieldVisibility.All);
        var handler = CreateHandler(10);

        var result = await handler.Handle(
            new GetOnlineStatusQuery { UserIds = [1] }, CancellationToken.None);

        result.UsersStatuses[0].Status.Should().Be(ProtoStatusTypeId.StatusOffline);
    }
}
