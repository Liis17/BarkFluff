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

public class GetUserDevicesQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ReturnsDevicesForSpecificUser()
    {
        var user1 = await _h.SeedUser(username: "user1");
        var user2 = await _h.SeedUser(username: "user2");
        await _h.SeedDevice(null, user1.Id, "User1 Device");
        await _h.SeedDevice(null, user2.Id, "User2 Device");
        var handler = new GetUserDevicesQueryHandler(_h.DevicesStorage, TestHelper.CreateLogger<GetUserDevicesQueryHandler>());

        var result = await handler.Handle(new GetUserDevicesQuery { UserId = user1.Id }, CancellationToken.None);

        result.Devices.Should().ContainSingle(d => d.OriginalName == "User1 Device");
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
