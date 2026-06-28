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

public class SetNotificationsEnabledCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ValidDevice_SetsEnabled()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new SetNotificationsEnabledCommandHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<SetNotificationsEnabledCommandHandler>());

        await handler.Handle(new SetNotificationsEnabledCommand { Enabled = false }, CancellationToken.None);

        var device = await _h.DevicesStorage.GetDeviceById(deviceId, user.Id);
        device!.NotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_InvalidDeviceId_ReturnsWithoutError()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, "invalid");
        var handler = new SetNotificationsEnabledCommandHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<SetNotificationsEnabledCommandHandler>());

        var act = () => handler.Handle(new SetNotificationsEnabledCommand { Enabled = true }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
