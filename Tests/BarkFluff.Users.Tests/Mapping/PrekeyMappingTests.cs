using BarkFluff.Users.Domain;
using BarkFluff.Users.Mapping;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Users.Tests.Mapping;

public class PrekeyMappingTests
{
    [Fact]
    public void ToGrpc_SignedPreKey_MapsAllFields()
    {
        var bundle = new DevicePrekeyBundle
        {
            DeviceId = Guid.NewGuid(),
            SignedPrekeyId = 42,
            SignedPrekeyPublic = new byte[] { 1, 2, 3 },
            SignedPrekeySignature = new byte[] { 4, 5, 6 },
        };

        var grpc = bundle.ToGrpc();

        grpc.PrekeyId.Should().Be(42u);
        grpc.PublicKey.Should().Equal(1, 2, 3);
        grpc.Signature.Should().Equal(4, 5, 6);
    }

    [Fact]
    public void ToGrpc_OneTimePrekey_MapsAllFields()
    {
        var prekey = new OneTimePrekey
        {
            PrekeyId = 99,
            PublicKey = new byte[] { 7, 8, 9 },
        };

        var grpc = prekey.ToGrpc();

        grpc.PrekeyId.Should().Be(99u);
        grpc.PublicKey.Should().Equal(7, 8, 9);
    }

    [Fact]
    public void ToGrpc_PrekeyBundle_WithOneTimePrekey()
    {
        var deviceId = Guid.NewGuid();
        var bundle = new DevicePrekeyBundle
        {
            DeviceId = deviceId,
            RegistrationId = 12345,
            IdentityPubkey = new byte[] { 1, 1, 1 },
            SignedPrekeyId = 10,
            SignedPrekeyPublic = new byte[] { 2, 2, 2 },
            SignedPrekeySignature = new byte[] { 3, 3, 3 },
        };
        var oneTime = new OneTimePrekey
        {
            PrekeyId = 20,
            PublicKey = new byte[] { 4, 4, 4 },
        };

        var grpc = bundle.ToGrpc(oneTime);

        grpc.DeviceId.Should().Be(deviceId.ToString());
        grpc.RegistrationId.Should().Be(12345u);
        grpc.IdentityPubkey.Should().NotBeEmpty();
        grpc.HasOneTimePrekey.Should().BeTrue();
        grpc.OneTimePrekey.PrekeyId.Should().Be(20u);
    }

    [Fact]
    public void ToGrpc_PrekeyBundle_WithoutOneTimePrekey()
    {
        var bundle = new DevicePrekeyBundle
        {
            DeviceId = Guid.NewGuid(),
            RegistrationId = 1,
            IdentityPubkey = [],
            SignedPrekeyId = 1,
            SignedPrekeyPublic = [],
            SignedPrekeySignature = [],
        };

        var grpc = bundle.ToGrpc(null);

        grpc.HasOneTimePrekey.Should().BeFalse();
    }

    [Fact]
    public void ToPeerInfoGrpc_WithCustomName()
    {
        var device = new UserDevice
        {
            Id = Guid.NewGuid(),
            OriginalName = "Pixel 8",
            CustomName = "My Phone",
            AuthorizedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var grpc = device.ToPeerInfoGrpc(true);

        grpc.DisplayName.Should().Be("My Phone");
        grpc.HasBundle.Should().BeTrue();
    }

    [Fact]
    public void ToPeerInfoGrpc_WithoutCustomName()
    {
        var device = new UserDevice
        {
            Id = Guid.NewGuid(),
            OriginalName = "Pixel 8",
            CustomName = null,
            AuthorizedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var grpc = device.ToPeerInfoGrpc(false);

        grpc.DisplayName.Should().Be("Pixel 8");
        grpc.HasBundle.Should().BeFalse();
    }
}
