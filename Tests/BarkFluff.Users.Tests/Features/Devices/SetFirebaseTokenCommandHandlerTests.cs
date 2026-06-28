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

public class SetFirebaseTokenCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ValidToken_SetsToken()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new SetFirebaseTokenCommandHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<SetFirebaseTokenCommandHandler>());

        await handler.Handle(new SetFirebaseTokenCommand { FirebaseToken = "abc123" }, CancellationToken.None);

        var device = await _h.DevicesStorage.GetDeviceById(deviceId, user.Id);
        device!.FirebaseDeviceToken.Should().Be("abc123");
    }

    [Fact]
    public async Task Handle_InvalidDeviceId_ReturnsWithoutError()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, "not-a-guid");
        var handler = new SetFirebaseTokenCommandHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<SetFirebaseTokenCommandHandler>());

        var act = () => handler.Handle(new SetFirebaseTokenCommand { FirebaseToken = "abc123" }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_EmptyToken_ReturnsWithoutError()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, Guid.NewGuid().ToString());
        var handler = new SetFirebaseTokenCommandHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<SetFirebaseTokenCommandHandler>());

        var act = () => handler.Handle(new SetFirebaseTokenCommand { FirebaseToken = "" }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_TokenTooLong_ReturnsWithoutError()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, Guid.NewGuid().ToString());
        var handler = new SetFirebaseTokenCommandHandler(_h.DevicesStorage, ctx, TestHelper.CreateLogger<SetFirebaseTokenCommandHandler>());

        var act = () => handler.Handle(new SetFirebaseTokenCommand { FirebaseToken = new string('x', 257) }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
