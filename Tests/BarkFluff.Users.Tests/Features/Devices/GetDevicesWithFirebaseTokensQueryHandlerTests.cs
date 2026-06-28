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

public class GetDevicesWithFirebaseTokensQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ReturnsOnlyDevicesWithTokens()
    {
        var user = await _h.SeedUser();
        await _h.SeedDevice(null, user.Id, firebaseToken: "token1");
        await _h.SeedDevice(null, user.Id, firebaseToken: null);
        var handler = new GetDevicesWithFirebaseTokensQueryHandler(_h.DevicesStorage, TestHelper.CreateLogger<GetDevicesWithFirebaseTokensQueryHandler>());

        var result = await handler.Handle(new GetDevicesWithFirebaseTokensQuery { UserIds = [user.Id] }, CancellationToken.None);

        result.Tokens.Should().ContainSingle(t => t.FirebaseToken == "token1");
    }

    [Fact]
    public async Task Handle_EmptyUserIds_ReturnsEmpty()
    {
        var handler = new GetDevicesWithFirebaseTokensQueryHandler(_h.DevicesStorage, TestHelper.CreateLogger<GetDevicesWithFirebaseTokensQueryHandler>());

        var result = await handler.Handle(new GetDevicesWithFirebaseTokensQuery { UserIds = [] }, CancellationToken.None);

        result.Tokens.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
