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

public class DeleteUserDeviceCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_DeletesDevice()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        var handler = new DeleteUserDeviceCommandHandler(_h.DevicesStorage, TestHelper.CreateLogger<DeleteUserDeviceCommandHandler>());

        await handler.Handle(new DeleteUserDeviceCommand { DeviceId = deviceId, UserId = user.Id }, CancellationToken.None);

        var device = await _h.DevicesStorage.GetDeviceById(deviceId, user.Id);
        device.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NonExistentDevice_CompletesWithoutError()
    {
        var user = await _h.SeedUser();
        var handler = new DeleteUserDeviceCommandHandler(_h.DevicesStorage, TestHelper.CreateLogger<DeleteUserDeviceCommandHandler>());

        var act = () => handler.Handle(new DeleteUserDeviceCommand { DeviceId = Guid.NewGuid(), UserId = user.Id }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
