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

public class FetchPrekeyBundleQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact(Skip = "FetchBundleAsync uses raw SQL (DELETE ... RETURNING) incompatible with InMemory provider")]
    public async Task Handle_ValidRequest_ReturnsBundle()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        await _h.PrekeyStorage.RegisterBundleAsync(deviceId, user.Id, 1,
            new byte[] { 1 }, 1, new byte[] { 2 }, new byte[] { 3 },
            [(10, new byte[] { 4 })]);
        var handler = new FetchPrekeyBundleQueryHandler(_h.PrekeyStorage, TestHelper.CreateLogger<FetchPrekeyBundleQueryHandler>());

        var result = await handler.Handle(new FetchPrekeyBundleQuery
        {
            UserId = user.Id,
            DeviceId = deviceId.ToString()
        }, CancellationToken.None);

        result.Bundle.Should().NotBeNull();
        result.Bundle.HasOneTimePrekey.Should().BeTrue();
        result.RemainingOneTimePrekeys.Should().Be(0);
    }

    [Fact]
    public async Task Handle_InvalidDeviceId_ThrowsInvalidOperationException()
    {
        var handler = new FetchPrekeyBundleQueryHandler(_h.PrekeyStorage, TestHelper.CreateLogger<FetchPrekeyBundleQueryHandler>());

        var act = () => handler.Handle(new FetchPrekeyBundleQuery
        {
            UserId = 1,
            DeviceId = "invalid"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_BundleNotFound_ThrowsInvalidOperationException()
    {
        var handler = new FetchPrekeyBundleQueryHandler(_h.PrekeyStorage, TestHelper.CreateLogger<FetchPrekeyBundleQueryHandler>());

        var act = () => handler.Handle(new FetchPrekeyBundleQuery
        {
            UserId = 1,
            DeviceId = Guid.NewGuid().ToString()
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
