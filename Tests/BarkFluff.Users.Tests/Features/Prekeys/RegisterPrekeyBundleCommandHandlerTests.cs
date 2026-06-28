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

public class RegisterPrekeyBundleCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ValidRequest_RegistersBundle()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new RegisterPrekeyBundleCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<RegisterPrekeyBundleCommandHandler>());

        await handler.Handle(new RegisterPrekeyBundleCommand
        {
            Request = new RegisterPrekeyBundleRequest
            {
                RegistrationId = 12345,
                IdentityPubkey = ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
                SignedPrekey = new SignedPreKey
                {
                    PrekeyId = 1,
                    PublicKey = ByteString.CopyFrom(new byte[] { 4, 5, 6 }),
                    Signature = ByteString.CopyFrom(new byte[] { 7, 8, 9 }),
                },
                OneTimePrekeys =
                {
                    new OneTimePreKey { PrekeyId = 10, PublicKey = ByteString.CopyFrom(new byte[] { 10 }) },
                    new OneTimePreKey { PrekeyId = 11, PublicKey = ByteString.CopyFrom(new byte[] { 11 }) },
                }
            }
        }, CancellationToken.None);

        var bundle = await _h.DbContext.DevicePrekeyBundles.FirstOrDefaultAsync(b => b.DeviceId == deviceId);
        bundle.Should().NotBeNull();
        bundle!.RegistrationId.Should().Be(12345);
    }

    [Fact]
    public async Task Handle_InvalidDeviceId_ReturnsWithoutError()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, "not-a-guid");
        var handler = new RegisterPrekeyBundleCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<RegisterPrekeyBundleCommandHandler>());

        var act = () => handler.Handle(new RegisterPrekeyBundleCommand
        {
            Request = new RegisterPrekeyBundleRequest
            {
                SignedPrekey = new SignedPreKey { PrekeyId = 1, PublicKey = ByteString.Empty, Signature = ByteString.Empty }
            }
        }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_NullSignedPrekey_ThrowsInvalidOperationException()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new RegisterPrekeyBundleCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<RegisterPrekeyBundleCommandHandler>());

        var act = () => handler.Handle(new RegisterPrekeyBundleCommand
        {
            Request = new RegisterPrekeyBundleRequest { SignedPrekey = null }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_Idempotent_OverwritesExisting()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new RegisterPrekeyBundleCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<RegisterPrekeyBundleCommandHandler>());

        var request = new RegisterPrekeyBundleCommand
        {
            Request = new RegisterPrekeyBundleRequest
            {
                RegistrationId = 1,
                IdentityPubkey = ByteString.CopyFrom(new byte[] { 1 }),
                SignedPrekey = new SignedPreKey { PrekeyId = 1, PublicKey = ByteString.CopyFrom(new byte[] { 1 }), Signature = ByteString.CopyFrom(new byte[] { 1 }) },
            }
        };

        await handler.Handle(request, CancellationToken.None);

        request.Request.RegistrationId = 2;
        await handler.Handle(request, CancellationToken.None);

        var bundle = await _h.DbContext.DevicePrekeyBundles.FirstOrDefaultAsync(b => b.DeviceId == deviceId);
        bundle!.RegistrationId.Should().Be(2);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
