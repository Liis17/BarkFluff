using BarkFluff.Proto.Federation;
using BarkFluff.Proto.FederationInternal;
using BarkFluff.Users.Features.ResolveFederatedUser;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Moq;

namespace BarkFluff.Users.Tests.Features.ResolveFederatedUser;

public class ResolveFederatedUserQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_LocalFid_ReturnsLocalUser()
    {
        var user = await _h.SeedUser(username: "localbob", firstName: "Local");
        var fedClient = new Mock<FederationInternalApi.FederationInternalApiClient>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var handler = new ResolveFederatedUserQueryHandler(_h.UsersStorage, _h.RemoteUsersStorage, fedClient.Object, config, _h.Metrics);

        var result = await handler.Handle(new ResolveFederatedUserQuery { Fid = "localbob" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Username.Should().Be("localbob");
        result.FirstName.Should().Be("Local");
        result.Uuid.Should().Be(user.Uuid.ToString());
        fedClient.Verify(c => c.ResolveRemoteUserAsync(It.IsAny<ResolveRemoteUserRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OwnServerName_TreatedAsLocal()
    {
        var user = await _h.SeedUser(username: "alice");
        var fedClient = new Mock<FederationInternalApi.FederationInternalApiClient>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Federation:ServerName"] = "node1.test",
        }).Build();
        var handler = new ResolveFederatedUserQueryHandler(_h.UsersStorage, _h.RemoteUsersStorage, fedClient.Object, config, _h.Metrics);

        var result = await handler.Handle(new ResolveFederatedUserQuery { Fid = "@alice:node1.test" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Username.Should().Be("alice");
        result.Uuid.Should().Be(user.Uuid.ToString());
    }

    [Fact]
    public async Task Handle_RemoteCached_Fresh_NoFederationCall()
    {
        var remoteUuid = Guid.NewGuid();
        await _h.SeedRemoteUser(uuid: remoteUuid, username: "carol", serverName: "node2.test", firstName: "Carol");
        var fedClient = new Mock<FederationInternalApi.FederationInternalApiClient>();
        var config = TestHelper.EmptyConfiguration;
        var handler = new ResolveFederatedUserQueryHandler(_h.UsersStorage, _h.RemoteUsersStorage, fedClient.Object, config, _h.Metrics);

        var result = await handler.Handle(new ResolveFederatedUserQuery { Fid = "@carol:node2.test" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Uuid.Should().Be(remoteUuid.ToString());
        result.Username.Should().Be("carol");
        fedClient.Verify(c => c.ResolveRemoteUserAsync(It.IsAny<ResolveRemoteUserRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RemoteCached_Stale_GoesToFederation()
    {
        var remoteUuid = Guid.NewGuid();
        var staleTs = DateTime.UtcNow - TimeSpan.FromHours(48); // > 24ч TTL.
        await _h.SeedRemoteUser(uuid: remoteUuid, username: "stale", serverName: "node2.test", lastSyncedAt: staleTs);

        var newProfile = new GetUserProfileResponse
        {
            Found = true,
            Uuid = remoteUuid.ToString(),
            Username = "stale",
            FirstName = "Refreshed",
        };
        var fedClient = new Mock<FederationInternalApi.FederationInternalApiClient>();
        fedClient
            .Setup(c => c.ResolveRemoteUserAsync(It.IsAny<ResolveRemoteUserRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(BuildAsyncUnaryCall(new ResolveRemoteUserResponse { Found = true, Profile = newProfile, ServerName = "node2.test" }));

        var config = TestHelper.EmptyConfiguration;
        var handler = new ResolveFederatedUserQueryHandler(_h.UsersStorage, _h.RemoteUsersStorage, fedClient.Object, config, _h.Metrics);

        var result = await handler.Handle(new ResolveFederatedUserQuery { Fid = "@stale:node2.test" }, CancellationToken.None);

        result.Found.Should().BeTrue();
        result.FirstName.Should().Be("Refreshed");
        fedClient.Verify(c => c.ResolveRemoteUserAsync(It.IsAny<ResolveRemoteUserRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FederationDisabled_ReturnsNotFound()
    {
        await _h.SeedRemoteUser(username: "off", serverName: "node2.test");
        var fedClient = new Mock<FederationInternalApi.FederationInternalApiClient>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = "false",
        }).Build();
        var handler = new ResolveFederatedUserQueryHandler(_h.UsersStorage, _h.RemoteUsersStorage, fedClient.Object, config, _h.Metrics);

        var result = await handler.Handle(new ResolveFederatedUserQuery { Fid = "@off:node2.test" }, CancellationToken.None);

        result.Found.Should().BeFalse();
        fedClient.Verify(c => c.ResolveRemoteUserAsync(It.IsAny<ResolveRemoteUserRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidFid_ReturnsNotFound()
    {
        var fedClient = new Mock<FederationInternalApi.FederationInternalApiClient>();
        var handler = new ResolveFederatedUserQueryHandler(_h.UsersStorage, _h.RemoteUsersStorage, fedClient.Object, TestHelper.EmptyConfiguration, _h.Metrics);

        var result = await handler.Handle(new ResolveFederatedUserQuery { Fid = "!!!" }, CancellationToken.None);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_FederationNotFound_ReturnsNotFound()
    {
        var fedClient = new Mock<FederationInternalApi.FederationInternalApiClient>();
        fedClient
            .Setup(c => c.ResolveRemoteUserAsync(It.IsAny<ResolveRemoteUserRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(BuildAsyncUnaryCall(new ResolveRemoteUserResponse { Found = false }));

        var handler = new ResolveFederatedUserQueryHandler(_h.UsersStorage, _h.RemoteUsersStorage, fedClient.Object, TestHelper.EmptyConfiguration, _h.Metrics);

        var result = await handler.Handle(new ResolveFederatedUserQuery { Fid = "@nobody:node2.test" }, CancellationToken.None);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_FederationRpcError_ReturnsNotFound()
    {
        var fedClient = new Mock<FederationInternalApi.FederationInternalApiClient>();
        fedClient
            .Setup(c => c.ResolveRemoteUserAsync(It.IsAny<ResolveRemoteUserRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "peer down")));

        var handler = new ResolveFederatedUserQueryHandler(_h.UsersStorage, _h.RemoteUsersStorage, fedClient.Object, TestHelper.EmptyConfiguration, _h.Metrics);

        var result = await handler.Handle(new ResolveFederatedUserQuery { Fid = "@peerdown:node2.test" }, CancellationToken.None);

        result.Found.Should().BeFalse();
    }

    private static AsyncUnaryCall<T> BuildAsyncUnaryCall<T>(T response)
    {
        return new AsyncUnaryCall<T>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
