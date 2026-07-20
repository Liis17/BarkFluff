using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.Proto.Navigator;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BarkFluff.Federation.Tests.Services;

public class NavigatorClientTests
{
    private static NavigatorClient CreateClient(Mock<NavigatorApi.NavigatorApiClient> mock)
        => new(mock.Object, NullLogger<NavigatorClient>.Instance);

    private static GetServerByNameResponse FoundResponse()
    {
        var response = new GetServerByNameResponse { Found = true };
        response.Server = new ServerInfo
        {
            ServerName = "peer.test",
            FederationEndpoint = "https://federation.peer.test",
        };
        response.Server.TlsSpkiSha256.Add("spki-1");
        response.Server.FederationProtocolVersions.Add(1);
        response.Server.SigningKeys.Add(new NavigatorSigningKey
        {
            KeyId = "ed25519:1",
            PublicKey = ByteString.CopyFrom(new byte[32]),
        });
        response.Server.SigningKeys.Add(new NavigatorSigningKey
        {
            KeyId = "ed25519:2",
            PublicKey = ByteString.CopyFrom(new byte[32]),
            ExpiredAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(30)),
        });
        return response;
    }

    [Fact]
    public async Task GetServerByNameAsync_Found_MapsToRemoteServerDocument()
    {
        var api = new Mock<NavigatorApi.NavigatorApiClient>();
        api.Setup(c => c.GetServerByNameAsync(
                It.Is<GetServerByNameRequest>(r => r.ServerName == "peer.test"),
                null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(FoundResponse()));

        var client = CreateClient(api);

        var document = await client.GetServerByNameAsync("peer.test");

        document.Should().NotBeNull();
        document!.ServerName.Should().Be("peer.test");
        document.FederationEndpoint.Should().Be("https://federation.peer.test");
        document.TlsSpkiSha256.Should().Equal("spki-1");
        document.ProtocolVersions.Should().Equal(1);
        document.SigningKeys.Should().HaveCount(2);
        document.SigningKeys[0].KeyId.Should().Be("ed25519:1");
        document.SigningKeys[0].ExpiredAt.Should().BeNull();
        document.SigningKeys[1].ExpiredAt.Should().NotBeNull();

        // Navigator-документ не подписан — континуитет доверия через него не устанавливается (P1-11).
        document.SignedByKeyId.Should().BeNull();
        document.SignedByPublicKey.Should().BeNull();
    }

    [Fact]
    public async Task GetServerByNameAsync_NotFound_ReturnsNull()
    {
        var api = new Mock<NavigatorApi.NavigatorApiClient>();
        api.Setup(c => c.GetServerByNameAsync(It.IsAny<GetServerByNameRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(new GetServerByNameResponse { Found = false }));

        var client = CreateClient(api);

        var document = await client.GetServerByNameAsync("unknown.test");

        document.Should().BeNull();
    }

    [Fact]
    public async Task GetServerByNameAsync_RpcFailure_ReturnsNull()
    {
        var api = new Mock<NavigatorApi.NavigatorApiClient>();
        api.Setup(c => c.GetServerByNameAsync(It.IsAny<GetServerByNameRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "navigator down")));

        var client = CreateClient(api);

        var document = await client.GetServerByNameAsync("peer.test");

        document.Should().BeNull();
    }
}
