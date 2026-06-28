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

public class RenameDeviceCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ValidRequest_RenamesDevice()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new RenameDeviceCommandHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<RenameDeviceCommandHandler>());

        await handler.Handle(new RenameDeviceCommand { DeviceId = deviceId, CustomName = "My Phone" }, CancellationToken.None);

        var device = await _h.DevicesStorage.GetDeviceById(deviceId, user.Id);
        device!.CustomName.Should().Be("My Phone");
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
