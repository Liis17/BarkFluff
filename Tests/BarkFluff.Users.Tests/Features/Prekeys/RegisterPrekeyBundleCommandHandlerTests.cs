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
    public async Task Handle_InvalidDeviceId_ThrowsUnauthenticatedError()
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

        var error = await act.Should().ThrowAsync<PrekeyException>();
        error.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.Unauthenticated);
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

        var error = await act.Should().ThrowAsync<PrekeyException>();
        error.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.InvalidArgument);
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

    [Fact]
    public async Task Handle_LegacyRetryWithoutPq_PreservesPublishedPqState()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        await _h.PrekeyStorage.RegisterBundleAsync(
            deviceId, user.Id, 1, [1], 1, [2], [3], [],
            new KyberPrekeyInput(30, [30], [31]),
            [new KyberPrekeyInput(20, [20], [21])]);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new RegisterPrekeyBundleCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<RegisterPrekeyBundleCommandHandler>());

        await handler.Handle(new RegisterPrekeyBundleCommand
        {
            Request = new RegisterPrekeyBundleRequest
            {
                RegistrationId = 1,
                IdentityPubkey = Google.Protobuf.ByteString.CopyFrom([1]),
                SignedPrekey = new SignedPreKey
                {
                    PrekeyId = 1,
                    PublicKey = Google.Protobuf.ByteString.CopyFrom([2]),
                    Signature = Google.Protobuf.ByteString.CopyFrom([3]),
                },
            },
        }, CancellationToken.None);

        var status = await _h.PrekeyStorage.GetOwnPrekeyStatusAsync(deviceId, user.Id);
        status.Bundle!.LastResortKyberPrekeyId.Should().Be(30);
        status.RemainingKyberPrekeys.Should().Be(1);
    }

    [Fact]
    public async Task Handle_KyberPoolWithoutLastResort_RejectsIncompletePqBundle()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new RegisterPrekeyBundleCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<RegisterPrekeyBundleCommandHandler>());

        var act = () => handler.Handle(new RegisterPrekeyBundleCommand
        {
            Request = new RegisterPrekeyBundleRequest
            {
                RegistrationId = 1,
                IdentityPubkey = Google.Protobuf.ByteString.CopyFrom([1]),
                SignedPrekey = new SignedPreKey
                {
                    PublicKey = Google.Protobuf.ByteString.CopyFrom([2]),
                    Signature = Google.Protobuf.ByteString.CopyFrom([3]),
                },
                OneTimeKyberPrekeys =
                {
                    new SignedKyberPreKey
                    {
                        PrekeyId = 20,
                        PublicKey = Google.Protobuf.ByteString.CopyFrom([20]),
                        Signature = Google.Protobuf.ByteString.CopyFrom([21]),
                    },
                },
            },
        }, CancellationToken.None);

        var error = await act.Should().ThrowAsync<PrekeyException>();
        error.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.InvalidArgument);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
