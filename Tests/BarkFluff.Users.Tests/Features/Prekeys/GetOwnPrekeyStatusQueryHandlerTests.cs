using BarkFluff.Users.Features.Prekeys.GetOwnPrekeyStatus;
using BarkFluff.Users.Persistence.Services;

using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.Prekeys;

public class GetOwnPrekeyStatusQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _helper = new();

    [Fact]
    public async Task Handle_DeviceWithoutBundle_ReturnsEmptyNonconsumingStatus()
    {
        var user = await _helper.SeedUser();
        var device = await _helper.SeedDevice(userId: user.Id);
        var handler = new GetOwnPrekeyStatusQueryHandler(
            _helper.PrekeyStorage,
            _helper.CreateUserContext(user.Id, device.Id.ToString()));

        var status = await handler.Handle(new GetOwnPrekeyStatusQuery(), CancellationToken.None);

        status.HasBundle.Should().BeFalse();
        status.SupportsPqxdh.Should().BeFalse();
        status.RemainingOneTimePrekeys.Should().Be(0);
        status.RemainingOneTimeKyberPrekeys.Should().Be(0);
        status.LastResortKyberPrekey.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MissingDeviceClaim_ThrowsUnauthenticatedError()
    {
        var user = await _helper.SeedUser();
        var handler = new GetOwnPrekeyStatusQueryHandler(
            _helper.PrekeyStorage,
            _helper.CreateUserContext(user.Id));

        var act = () => handler.Handle(new GetOwnPrekeyStatusQuery(), CancellationToken.None);

        var error = await act.Should().ThrowAsync<PrekeyException>();
        error.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.Unauthenticated);
    }

    public ValueTask DisposeAsync() => _helper.DbContext.DisposeAsync();
}
