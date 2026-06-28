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

public class ReplenishOneTimePrekeysCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_AddsNewPrekeys()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        await _h.PrekeyStorage.RegisterBundleAsync(deviceId, user.Id, 1,
            new byte[] { 1 }, 1, new byte[] { 2 }, new byte[] { 3 }, []);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new ReplenishOneTimePrekeysCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<ReplenishOneTimePrekeysCommandHandler>());

        var result = await handler.Handle(new ReplenishOneTimePrekeysCommand
        {
            Request = new ReplenishOneTimePrekeysRequest
            {
                Prekeys =
                {
                    new OneTimePreKey { PrekeyId = 100, PublicKey = ByteString.CopyFrom(new byte[] { 1 }) },
                    new OneTimePreKey { PrekeyId = 101, PublicKey = ByteString.CopyFrom(new byte[] { 2 }) },
                }
            }
        }, CancellationToken.None);

        result.TotalOneTimePrekeys.Should().Be(2);
    }

    [Fact]
    public async Task Handle_DuplicatePrekeyIds_Ignored()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        await _h.PrekeyStorage.RegisterBundleAsync(deviceId, user.Id, 1,
            new byte[] { 1 }, 1, new byte[] { 2 }, new byte[] { 3 },
            [(100, new byte[] { 1 })]);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new ReplenishOneTimePrekeysCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<ReplenishOneTimePrekeysCommandHandler>());

        var result = await handler.Handle(new ReplenishOneTimePrekeysCommand
        {
            Request = new ReplenishOneTimePrekeysRequest
            {
                Prekeys =
                {
                    new OneTimePreKey { PrekeyId = 100, PublicKey = ByteString.CopyFrom(new byte[] { 2 }) },
                    new OneTimePreKey { PrekeyId = 101, PublicKey = ByteString.CopyFrom(new byte[] { 3 }) },
                }
            }
        }, CancellationToken.None);

        result.TotalOneTimePrekeys.Should().Be(2);
    }

    [Fact]
    public async Task Handle_InvalidDeviceId_ReturnsEmpty()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, "not-a-guid");
        var handler = new ReplenishOneTimePrekeysCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<ReplenishOneTimePrekeysCommandHandler>());

        var result = await handler.Handle(new ReplenishOneTimePrekeysCommand
        {
            Request = new ReplenishOneTimePrekeysRequest()
        }, CancellationToken.None);

        result.TotalOneTimePrekeys.Should().Be(0);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
