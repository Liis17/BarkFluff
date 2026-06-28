using BarkFluff.Users.Features.Devices.DeleteUserDevice;
using BarkFluff.Users.Features.Devices.GetDevices;
using BarkFluff.Users.Features.Devices.GetCurrentDevice;
using BarkFluff.Users.Features.Devices.GetUserDevices;
using BarkFluff.Users.Features.Devices.RegisterDevice;
using BarkFluff.Users.Features.Devices.RenameDevice;
using BarkFluff.Users.Features.Devices.SetFirebaseToken;
using BarkFluff.Users.Features.Devices.SetNotificationsEnabled;
using BarkFluff.Users.Features.Devices.GetDevicesWithFirebaseTokens;
using BarkFluff.Users.Features.Devices.GetDevicesWithFirebaseTokensByDeviceIds;
using BarkFluff.Users.Features.Devices.GetAllDevicesWithFirebaseTokens;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.Devices;

public class GetDevicesWithFirebaseTokensByDeviceIdsQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ReturnsMatchingDevices()
    {
        var user = await _h.SeedUser();
        var d1 = await _h.SeedDevice(null, user.Id, firebaseToken: "token1");
        var d2 = await _h.SeedDevice(null, user.Id, firebaseToken: "token2");
        var handler = new GetDevicesWithFirebaseTokensByDeviceIdsQueryHandler(_h.DevicesStorage, TestHelper.CreateLogger<GetDevicesWithFirebaseTokensByDeviceIdsQueryHandler>());

        var result = await handler.Handle(new GetDevicesWithFirebaseTokensByDeviceIdsQuery { DeviceIds = [d1.Id] }, CancellationToken.None);

        result.Tokens.Should().ContainSingle(t => t.FirebaseToken == "token1");
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
