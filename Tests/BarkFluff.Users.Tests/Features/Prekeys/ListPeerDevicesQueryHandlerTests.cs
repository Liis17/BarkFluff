using BarkFluff.Proto.Users;
using BarkFluff.Users.Features.Prekeys.FetchPrekeyBundle;
using BarkFluff.Users.Features.Prekeys.ListPeerDevices;
using BarkFluff.Users.Features.Prekeys.RegisterPrekeyBundle;
using BarkFluff.Users.Features.Prekeys.ReplenishOneTimePrekeys;
using BarkFluff.Users.Features.Prekeys.RotateSignedPrekey;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Tests.Features.Prekeys;

public class ListPeerDevicesQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ReturnsDevicesWithBundleFlag()
    {
        var user = await _h.SeedUser();
        var d1 = await _h.SeedDevice(null, user.Id, "Device 1");
        await _h.PrekeyStorage.RegisterBundleAsync(d1.Id, user.Id, 1,
            new byte[] { 1 }, 1, new byte[] { 2 }, new byte[] { 3 }, []);
        var d2 = await _h.SeedDevice(null, user.Id, "Device 2");
        var handler = new ListPeerDevicesQueryHandler(_h.PrekeyStorage);

        var result = await handler.Handle(new ListPeerDevicesQuery { UserId = user.Id }, CancellationToken.None);

        result.Devices.Should().HaveCount(2);
        result.Devices.First(d => d.DeviceId == d1.Id.ToString()).HasBundle.Should().BeTrue();
        result.Devices.First(d => d.DeviceId == d2.Id.ToString()).HasBundle.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoDevices_ReturnsEmpty()
    {
        var user = await _h.SeedUser();
        var handler = new ListPeerDevicesQueryHandler(_h.PrekeyStorage);

        var result = await handler.Handle(new ListPeerDevicesQuery { UserId = user.Id }, CancellationToken.None);

        result.Devices.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
