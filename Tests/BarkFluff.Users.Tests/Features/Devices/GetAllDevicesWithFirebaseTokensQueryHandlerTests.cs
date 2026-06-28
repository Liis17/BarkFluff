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

public class GetAllDevicesWithFirebaseTokensQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ReturnsAllDevicesWithTokens()
    {
        var user = await _h.SeedUser();
        await _h.SeedDevice(null, user.Id, firebaseToken: "t1");
        await _h.SeedDevice(null, user.Id, firebaseToken: "t2");
        var handler = new GetAllDevicesWithFirebaseTokensQueryHandler(_h.DevicesStorage, TestHelper.CreateLogger<GetAllDevicesWithFirebaseTokensQueryHandler>());

        var result = await handler.Handle(new GetAllDevicesWithFirebaseTokensQuery(), CancellationToken.None);

        result.Tokens.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
