using BarkFluff.Proto.Users;
using BarkFluff.Users.Features.Prekeys.FetchPrekeyBundle;
using BarkFluff.Users.Features.Prekeys.ListPeerDevices;
using BarkFluff.Users.Features.Prekeys.RegisterPrekeyBundle;
using BarkFluff.Users.Features.Prekeys.ReplenishOneTimePrekeys;
using BarkFluff.Users.Features.Prekeys.RotateSignedPrekey;
using BarkFluff.Users.Persistence.Services;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Tests.Features.Prekeys;

public class RotateSignedPrekeyCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_RotatesSignedPrekey()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        await _h.PrekeyStorage.RegisterBundleAsync(deviceId, user.Id, 1,
            new byte[] { 1 }, 1, new byte[] { 2 }, new byte[] { 3 }, []);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new RotateSignedPrekeyCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<RotateSignedPrekeyCommandHandler>());

        await handler.Handle(new RotateSignedPrekeyCommand
        {
            Request = new RotateSignedPrekeyRequest
            {
                SignedPrekey = new SignedPreKey
                {
                    PrekeyId = 2,
                    PublicKey = ByteString.CopyFrom(new byte[] { 10 }),
                    Signature = ByteString.CopyFrom(new byte[] { 20 }),
                }
            }
        }, CancellationToken.None);

        var bundle = await _h.DbContext.DevicePrekeyBundles.FirstOrDefaultAsync(b => b.DeviceId == deviceId);
        bundle!.SignedPrekeyId.Should().Be(2);
    }

    [Fact]
    public async Task Handle_NoPrekey_ThrowsInvalidArgument()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new RotateSignedPrekeyCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<RotateSignedPrekeyCommandHandler>());

        var act = () => handler.Handle(new RotateSignedPrekeyCommand
        {
            Request = new RotateSignedPrekeyRequest { SignedPrekey = null }
        }, CancellationToken.None);

        var error = await act.Should().ThrowAsync<PrekeyException>();
        error.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task Handle_KyberOnlyRequest_RotatesLastResortKey()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        await _h.PrekeyStorage.RegisterBundleAsync(deviceId, user.Id, 1,
            [1], 1, [2], [3], [], new KyberPrekeyInput(30, [30], [31]));
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new RotateSignedPrekeyCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<RotateSignedPrekeyCommandHandler>());

        await handler.Handle(new RotateSignedPrekeyCommand
        {
            Request = new RotateSignedPrekeyRequest
            {
                LastResortKyberPrekey = new SignedKyberPreKey
                {
                    PrekeyId = 40,
                    PublicKey = ByteString.CopyFrom([40]),
                    Signature = ByteString.CopyFrom([41]),
                },
            },
        }, CancellationToken.None);

        var bundle = await _h.DbContext.DevicePrekeyBundles.FirstAsync(bundle => bundle.DeviceId == deviceId);
        bundle.LastResortKyberPrekeyId.Should().Be(40);
        bundle.SignedPrekeyId.Should().Be(1);
    }

    [Fact]
    public async Task Handle_InvalidDeviceId_ThrowsUnauthenticatedError()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, "invalid");
        var handler = new RotateSignedPrekeyCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<RotateSignedPrekeyCommandHandler>());

        var act = () => handler.Handle(new RotateSignedPrekeyCommand
        {
            Request = new RotateSignedPrekeyRequest
            {
                SignedPrekey = new SignedPreKey { PrekeyId = 1, PublicKey = ByteString.Empty, Signature = ByteString.Empty }
            }
        }, CancellationToken.None);

        var error = await act.Should().ThrowAsync<PrekeyException>();
        error.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.Unauthenticated);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
