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

public class GetDevicesQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ReturnsAllUserDevices()
    {
        var user = await _h.SeedUser();
        await _h.SeedDevice(null, user.Id, "Device 1");
        await _h.SeedDevice(null, user.Id, "Device 2");
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetDevicesQueryHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<GetDevicesQueryHandler>());

        var result = await handler.Handle(new GetDevicesQuery(), CancellationToken.None);

        result.Devices.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoDevices_ReturnsEmpty()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetDevicesQueryHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<GetDevicesQueryHandler>());

        var result = await handler.Handle(new GetDevicesQuery(), CancellationToken.None);

        result.Devices.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
