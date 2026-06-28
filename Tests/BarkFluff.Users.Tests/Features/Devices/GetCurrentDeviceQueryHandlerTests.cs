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

public class GetCurrentDeviceQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ValidDeviceId_ReturnsDevice()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new GetCurrentDeviceQueryHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<GetCurrentDeviceQueryHandler>());

        var result = await handler.Handle(new GetCurrentDeviceQuery(), CancellationToken.None);

        result.Device.Should().NotBeNull();
        result.Device.DeviceId.Should().Be(deviceId.ToString());
    }

    [Fact]
    public async Task Handle_InvalidDeviceId_ReturnsEmptyResponse()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, "not-a-guid");
        var handler = new GetCurrentDeviceQueryHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<GetCurrentDeviceQueryHandler>());

        var result = await handler.Handle(new GetCurrentDeviceQuery(), CancellationToken.None);

        result.Device.Should().BeNull();
    }

    [Fact]
    public async Task Handle_EmptyDeviceId_ReturnsEmptyResponse()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, "");
        var handler = new GetCurrentDeviceQueryHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<GetCurrentDeviceQueryHandler>());

        var result = await handler.Handle(new GetCurrentDeviceQuery(), CancellationToken.None);

        result.Device.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DeviceNotFoundForUser_ReturnsEmptyResponse()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, Guid.NewGuid().ToString());
        var handler = new GetCurrentDeviceQueryHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<GetCurrentDeviceQueryHandler>());

        var result = await handler.Handle(new GetCurrentDeviceQuery(), CancellationToken.None);

        result.Device.Should().BeNull();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
