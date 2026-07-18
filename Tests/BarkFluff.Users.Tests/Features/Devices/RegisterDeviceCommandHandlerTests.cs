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

public class RegisterDeviceCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_NewDevice_Registers()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        var handler = new RegisterDeviceCommandHandler(_h.DevicesStorage, TestHelper.CreateLogger<RegisterDeviceCommandHandler>());

        var result = await handler.Handle(new RegisterDeviceCommand
        {
            DeviceId = deviceId,
            UserId = user.Id,
            OriginalName = "Pixel 8",
            AppName = "BarkFluff",
            OperationSystem = "Android 15"
        }, CancellationToken.None);

        result.Device.Should().NotBeNull();
        result.Device.DeviceId.Should().Be(deviceId.ToString());
    }

    [Fact]
    public async Task Handle_ExistingDevice_Updates()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id, "Old Name");
        var handler = new RegisterDeviceCommandHandler(_h.DevicesStorage, TestHelper.CreateLogger<RegisterDeviceCommandHandler>());

        var result = await handler.Handle(new RegisterDeviceCommand
        {
            DeviceId = deviceId,
            UserId = user.Id,
            OriginalName = "New Name",
            AppName = "Updated"
        }, CancellationToken.None);

        result.Device.OriginalName.Should().Be("New Name");
    }

    [Fact]
    public async Task Handle_DeviceRegisteredToAnotherUser_MovesDevice()
    {
        var previousUser = await _h.SeedUser(
            username: "previous_owner",
            email: "previous_owner@test.com");
        var currentUser = await _h.SeedUser(
            username: "current_owner",
            email: "current_owner@test.com");
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, previousUser.Id, "Old Name");
        var handler = new RegisterDeviceCommandHandler(
            _h.DevicesStorage,
            TestHelper.CreateLogger<RegisterDeviceCommandHandler>());

        await handler.Handle(new RegisterDeviceCommand
        {
            DeviceId = deviceId,
            UserId = currentUser.Id,
            OriginalName = "New Name",
            AppName = "BarkFluff"
        }, CancellationToken.None);

        (await _h.DevicesStorage.GetDevicesByUserId(previousUser.Id)).Should().BeEmpty();
        (await _h.DevicesStorage.GetDevicesByUserId(currentUser.Id))
            .Should().ContainSingle(device => device.Id == deviceId && device.OriginalName == "New Name");
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
