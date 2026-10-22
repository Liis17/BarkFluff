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
            new byte[] { 1 }, 1, new byte[] { 2 }, new byte[] { 3 }, [],
            new KyberPrekeyInput(300, [30], [31]));
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
                },
                KyberPrekeys =
                {
                    new SignedKyberPreKey { PrekeyId = 200, PublicKey = ByteString.CopyFrom([3]), Signature = ByteString.CopyFrom([4]) },
                    new SignedKyberPreKey { PrekeyId = 201, PublicKey = ByteString.CopyFrom([5]), Signature = ByteString.CopyFrom([6]) },
                }
            }
        }, CancellationToken.None);

        result.TotalOneTimePrekeys.Should().Be(2);
        result.TotalOneTimeKyberPrekeys.Should().Be(2);
    }

    [Fact]
    public async Task Handle_DuplicatePrekeyIdsWithDifferentPayload_RejectsRequest()
    {
        var user = await _h.SeedUser();
        var deviceId = Guid.NewGuid();
        await _h.SeedDevice(deviceId, user.Id);
        await _h.PrekeyStorage.RegisterBundleAsync(deviceId, user.Id, 1,
            new byte[] { 1 }, 1, new byte[] { 2 }, new byte[] { 3 },
            [(100, new byte[] { 1 })]);
        var ctx = _h.CreateUserContext(user.Id, deviceId.ToString());
        var handler = new ReplenishOneTimePrekeysCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<ReplenishOneTimePrekeysCommandHandler>());

        var act = () => handler.Handle(new ReplenishOneTimePrekeysCommand
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

        var error = await act.Should().ThrowAsync<PrekeyException>();
        error.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.AlreadyExists);
    }

    [Fact]
    public async Task Handle_InvalidDeviceId_ThrowsUnauthenticatedError()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id, "not-a-guid");
        var handler = new ReplenishOneTimePrekeysCommandHandler(_h.PrekeyStorage, ctx, TestHelper.CreateLogger<ReplenishOneTimePrekeysCommandHandler>());

        var act = () => handler.Handle(new ReplenishOneTimePrekeysCommand
        {
            Request = new ReplenishOneTimePrekeysRequest()
        }, CancellationToken.None);

        var error = await act.Should().ThrowAsync<PrekeyException>();
        error.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.Unauthenticated);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
