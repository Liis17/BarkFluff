using BarkFluff.Users.Features.Devices.ClearFirebaseToken;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.Devices;

public class ClearFirebaseTokenCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_CurrentDevice_ClearsOnlyItsFirebaseToken()
    {
        var user = await _h.SeedUser();
        var currentDeviceId = Guid.NewGuid();
        var otherDeviceId = Guid.NewGuid();
        await _h.SeedDevice(currentDeviceId, user.Id, firebaseToken: "current-token");
        await _h.SeedDevice(otherDeviceId, user.Id, firebaseToken: "other-token");
        var context = _h.CreateUserContext(user.Id, currentDeviceId.ToString());
        var handler = new ClearFirebaseTokenCommandHandler(
            _h.DevicesStorage,
            context,
            TestHelper.CreateLogger<ClearFirebaseTokenCommandHandler>());

        await handler.Handle(new ClearFirebaseTokenCommand(), CancellationToken.None);

        (await _h.DevicesStorage.GetDeviceById(currentDeviceId, user.Id))!.FirebaseDeviceToken.Should().BeNull();
        (await _h.DevicesStorage.GetDeviceById(otherDeviceId, user.Id))!.FirebaseDeviceToken.Should().Be("other-token");
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
