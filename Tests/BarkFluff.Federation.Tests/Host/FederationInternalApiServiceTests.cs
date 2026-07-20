using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Host;
using BarkFluff.Federation.Infrastructure;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.FederationInternal;
using BarkFluff.Shared.Exceptions.Federation;

using Google.Protobuf;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BarkFluff.Federation.Tests.Host;

public class FederationInternalApiServiceTests
{
    private sealed class Fixture : IDisposable
    {
        public TestHelpers.TestDatabase Db { get; }
        public ServiceProvider Provider { get; }
        public FederationContext Context { get; }
        public ActiveSigningKeyCache KeyCache { get; }
        public WellKnownDocumentService WellKnown { get; }
        public FakeWellKnownClient FakeWellKnown { get; } = new();
        public FakeNavigatorClient FakeNavigator { get; } = new();
        public FederationInternalApiService Service { get; }

        public Fixture(IDictionary<string, string?>? configOverrides = null)
        {
            Db = TestHelpers.CreateDatabase();
            var configuration = TestHelpers.CreateConfiguration(configOverrides);
            Provider = TestHelpers.CreateProvider(Db, configuration, FakeWellKnown, FakeNavigator);
            Context = TestHelpers.CreateContext(Db);

            var signing = TestHelpers.CreateSigningKeyService(Context, configuration);
            KeyCache = new ActiveSigningKeyCache(Provider.GetRequiredService<IServiceScopeFactory>());
            WellKnown = new WellKnownDocumentService(Provider.GetRequiredService<IServiceScopeFactory>(), configuration);

            var resolver = new ServerResolver(
                Context, FakeWellKnown, FakeNavigator,
                Mock.Of<IS2SChannelInvalidator>(), new MetricsCollector(), NullLogger<ServerResolver>.Instance);

            var channelFactory = new S2SChannelFactory(
                Provider.GetRequiredService<IServiceScopeFactory>(),
                configuration,
                new LoopbackServernameValidator(),
                KeyCache,
                new MetricsCollector(),
                NullLogger<S2SChannelFactory>.Instance);

            var writer = new OutboxWriter(Context, signing, configuration, new MetricsCollector());

            Service = new FederationInternalApiService(
                Context, configuration, signing, WellKnown, KeyCache, resolver, channelFactory, writer);
        }

        public void Dispose()
        {
            Context.Dispose();
            Provider.Dispose();
        }
    }

    private static async Task SeedPeerAsync(FederationContext context, string serverName, string endpoint, KnownServerStatus status = KnownServerStatus.Active, params string[] keyIds)
    {
        var server = new KnownServer
        {
            ServerName = serverName,
            FederationEndpoint = endpoint,
            Source = KnownServerSource.Manual,
            Status = status,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            ProtocolVersion = 1,
        };
        foreach (var keyId in keyIds)
        {
            server.Keys.Add(new KnownServerKey
            {
                ServerName = serverName,
                KeyId = keyId,
                PublicKey = Enumerable.Repeat((byte)1, 32).ToArray(),
            });
        }
        context.KnownServers.Add(server);
        await context.SaveChangesAsync();
    }

    // ---------- RotateSigningKey ----------

    [Fact]
    public async Task RotateSigningKey_ReturnsNewAndOld_RefreshesCaches()
    {
        using var fixture = new Fixture(new Dictionary<string, string?>
        {
            ["Federation:ExternalEndpoint"] = "https://federation.node-a.test",
        });
        await TestHelpers.EnsureActiveKeyAsync(fixture.Context);

        var response = await fixture.Service.RotateSigningKey(new RotateSigningKeyRequest(), TestHelpers.CreateCallContext());

        response.NewKeyId.Should().Be("ed25519:2");
        response.OldKeyId.Should().Be("ed25519:1");
        response.OldKeyExpiresAt.ToDateTime().Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));

        // Исходящие подписи — новым ключом немедленно; well-known пересобран и содержит оба ключа.
        fixture.KeyCache.Current.Should().NotBeNull();
        fixture.KeyCache.Current!.KeyId.Should().Be("ed25519:2");
        var document = fixture.WellKnown.GetCachedDocument();
        document.Should().NotBeNull();
        document.Should().Contain("ed25519:1").And.Contain("ed25519:2");
    }

    // ---------- GetKnownServers ----------

    [Fact]
    public async Task GetKnownServers_ReturnsPeersWithKeys()
    {
        using var fixture = new Fixture();
        await SeedPeerAsync(fixture.Context, "peer-a.test", "https://federation.peer-a.test", KnownServerStatus.Active, "ed25519:1");
        await SeedPeerAsync(fixture.Context, "peer-b.test", "https://federation.peer-b.test", KnownServerStatus.Blocked, "ed25519:1", "ed25519:2");

        var response = await fixture.Service.GetKnownServers(new GetKnownServersRequest(), TestHelpers.CreateCallContext());

        response.Servers.Should().HaveCount(2);
        var peerA = response.Servers.Single(s => s.ServerName == "peer-a.test");
        peerA.FederationEndpoint.Should().Be("https://federation.peer-a.test");
        peerA.Status.Should().Be(nameof(KnownServerStatus.Active));
        peerA.Source.Should().Be(nameof(KnownServerSource.Manual));
        peerA.Keys.Should().HaveCount(1);

        response.Servers.Single(s => s.ServerName == "peer-b.test").Keys.Should().HaveCount(2);
    }

    // ---------- UpsertManualPeer ----------

    [Fact]
    public async Task UpsertManualPeer_InvalidServername_Throws()
    {
        using var fixture = new Fixture();

        var act = () => fixture.Service.UpsertManualPeer(
            new UpsertManualPeerRequest { ServerName = "!!!", FederationEndpoint = "https://x" },
            TestHelpers.CreateCallContext());

        await act.Should().ThrowAsync<InvalidServernameException>();
    }

    [Fact]
    public async Task UpsertManualPeer_NewPeer_CreatedNormalized()
    {
        using var fixture = new Fixture();
        var request = new UpsertManualPeerRequest
        {
            ServerName = "PEER.test",
            FederationEndpoint = "https://federation.peer.test",
        };
        request.Keys.Add(new SigningKey { KeyId = "ed25519:1", PublicKey = ByteString.CopyFrom(new byte[32]) });

        await fixture.Service.UpsertManualPeer(request, TestHelpers.CreateCallContext());

        var peer = await fixture.Context.KnownServers.Include(s => s.Keys).SingleAsync();
        peer.ServerName.Should().Be("peer.test", "servername нормализуется к lowercase A-label");
        peer.Source.Should().Be(KnownServerSource.Manual);
        peer.Status.Should().Be(KnownServerStatus.Active);
        peer.FederationEndpoint.Should().Be("https://federation.peer.test");
        peer.LastKeyRefreshAt.Should().NotBeNull();
        peer.Keys.Should().ContainSingle(k => k.KeyId == "ed25519:1");
    }

    [Fact]
    public async Task UpsertManualPeer_ExistingPeer_UpdatesAndReconcilesKeys()
    {
        using var fixture = new Fixture();
        await SeedPeerAsync(fixture.Context, "peer.test", "https://old.peer.test", keyIds: ["ed25519:1", "ed25519:2"]);

        var request = new UpsertManualPeerRequest
        {
            ServerName = "peer.test",
            FederationEndpoint = "https://new.peer.test",
        };
        request.Keys.Add(new SigningKey { KeyId = "ed25519:2", PublicKey = ByteString.CopyFrom(Enumerable.Repeat((byte)1, 32).ToArray()) });
        request.Keys.Add(new SigningKey { KeyId = "ed25519:3", PublicKey = ByteString.CopyFrom(new byte[32]) });

        await fixture.Service.UpsertManualPeer(request, TestHelpers.CreateCallContext());

        var peer = await fixture.Context.KnownServers.Include(s => s.Keys).SingleAsync();
        peer.FederationEndpoint.Should().Be("https://new.peer.test");

        // Админ-набор авторитетен: исчезнувший отозван, новый добавлен, присутствующий сохранён.
        peer.Keys.Single(k => k.KeyId == "ed25519:1").RevokedAt.Should().NotBeNull();
        peer.Keys.Single(k => k.KeyId == "ed25519:2").RevokedAt.Should().BeNull();
        peer.Keys.Should().ContainSingle(k => k.KeyId == "ed25519:3");
    }

    // ---------- SetServerBlocked ----------

    [Fact]
    public async Task SetServerBlocked_InvalidServername_Throws()
    {
        using var fixture = new Fixture();

        var act = () => fixture.Service.SetServerBlocked(
            new SetServerBlockedRequest { ServerName = "!!!", Blocked = true },
            TestHelpers.CreateCallContext());

        await act.Should().ThrowAsync<InvalidServernameException>();
    }

    [Fact]
    public async Task SetServerBlocked_UnknownPeer_Throws()
    {
        using var fixture = new Fixture();

        var act = () => fixture.Service.SetServerBlocked(
            new SetServerBlockedRequest { ServerName = "ghost.test", Blocked = true },
            TestHelpers.CreateCallContext());

        await act.Should().ThrowAsync<FederationPeerNotFoundException>();
    }

    [Fact]
    public async Task SetServerBlocked_BlockAndUnblock()
    {
        using var fixture = new Fixture();
        await SeedPeerAsync(fixture.Context, "peer.test", "https://federation.peer.test");

        await fixture.Service.SetServerBlocked(
            new SetServerBlockedRequest { ServerName = "peer.test", Blocked = true },
            TestHelpers.CreateCallContext());
        (await fixture.Context.KnownServers.SingleAsync()).Status.Should().Be(KnownServerStatus.Blocked);

        await fixture.Service.SetServerBlocked(
            new SetServerBlockedRequest { ServerName = "peer.test", Blocked = false },
            TestHelpers.CreateCallContext());
        (await fixture.Context.KnownServers.SingleAsync()).Status.Should().Be(KnownServerStatus.Active);
    }

    // ---------- GetFederationStatus ----------

    [Fact]
    public async Task GetFederationStatus_ReturnsCountersAndKeys()
    {
        using var fixture = new Fixture();
        await TestHelpers.EnsureActiveKeyAsync(fixture.Context);
        await SeedPeerAsync(fixture.Context, "peer-active.test", "https://federation.peer-active.test", KnownServerStatus.Active);
        await SeedPeerAsync(fixture.Context, "peer-blocked.test", "https://federation.peer-blocked.test", KnownServerStatus.Blocked);

        fixture.Context.Outbox.Add(new FederationOutbox
        {
            Destination = "peer-active.test",
            EventId = Guid.NewGuid(),
            EventType = "NewMessage",
            PayloadBytes = [1],
            CreatedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending,
        });
        fixture.Context.Outbox.Add(new FederationOutbox
        {
            Destination = "peer-active.test",
            EventId = Guid.NewGuid(),
            EventType = "NewMessage",
            PayloadBytes = [1],
            CreatedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow,
            Status = OutboxStatus.DeadLetter,
        });
        await fixture.Context.SaveChangesAsync();

        var response = await fixture.Service.GetFederationStatus(new GetFederationStatusRequest(), TestHelpers.CreateCallContext());

        response.ServerName.Should().Be(TestHelpers.OwnServerName);
        response.Enabled.Should().BeTrue();
        response.OutboxPending.Should().Be(1);
        response.OutboxDeadletter.Should().Be(1);
        response.KnownServersActive.Should().Be(1);
        response.OwnKeys.Should().ContainSingle(k => k.KeyId == "ed25519:1");
    }

    // ---------- EnqueueOutbound ----------

    [Fact]
    public async Task EnqueueOutbound_NullEvent_Throws()
    {
        using var fixture = new Fixture();

        var act = () => fixture.Service.EnqueueOutbound(new EnqueueOutboundRequest(), TestHelpers.CreateCallContext());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnqueueOutbound_NoDestinations_ReturnsZero()
    {
        using var fixture = new Fixture();
        var request = new EnqueueOutboundRequest
        {
            Event = new FederationEvent { EventId = Guid.NewGuid().ToString(), OriginServer = TestHelpers.OwnServerName },
        };

        var response = await fixture.Service.EnqueueOutbound(request, TestHelpers.CreateCallContext());

        response.Enqueued.Should().Be(0);
        response.EventId.Should().Be(request.Event.EventId);
    }

    [Fact]
    public async Task EnqueueOutbound_Valid_EnqueuesPerDestinationExcludingOwn()
    {
        using var fixture = new Fixture();
        await TestHelpers.EnsureActiveKeyAsync(fixture.Context);

        var chatId = Guid.NewGuid();
        var request = new EnqueueOutboundRequest
        {
            Event = new FederationEvent
            {
                EventId = Guid.NewGuid().ToString(),
                OriginServer = TestHelpers.OwnServerName,
                OriginTsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                NewMessage = new NewMessagePayload
                {
                    ChatId = chatId.ToString(),
                    FederatedMessageId = Guid.NewGuid().ToString(),
                },
            },
        };
        request.Destinations.Add("peer.test");
        request.Destinations.Add(TestHelpers.OwnServerName); // own-нода исключается

        var response = await fixture.Service.EnqueueOutbound(request, TestHelpers.CreateCallContext());

        response.Enqueued.Should().Be(1);

        var row = await fixture.Context.Outbox.SingleAsync();
        row.Destination.Should().Be("peer.test");
        row.ChatId.Should().Be(chatId, "chat id извлекается из payload события");
        row.EventId.Should().Be(Guid.Parse(request.Event.EventId));
        row.Status.Should().Be(OutboxStatus.Pending);
    }

    // ---------- ResolveRemoteUser ----------

    [Fact]
    public async Task ResolveRemoteUser_FederationDisabled_ReturnsNotFound()
    {
        using var fixture = new Fixture(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = "false",
        });

        var response = await fixture.Service.ResolveRemoteUser(
            new ResolveRemoteUserRequest { Fid = "@alice:peer.test" },
            TestHelpers.CreateCallContext());

        response.Found.Should().BeFalse();
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("@noserver")]
    [InlineData("@alice:!!!")]
    public async Task ResolveRemoteUser_InvalidFid_ReturnsNotFound(string fid)
    {
        using var fixture = new Fixture();

        var response = await fixture.Service.ResolveRemoteUser(
            new ResolveRemoteUserRequest { Fid = fid },
            TestHelpers.CreateCallContext());

        response.Found.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveRemoteUser_UuidWithoutServer_ReturnsNotFound()
    {
        using var fixture = new Fixture();

        var response = await fixture.Service.ResolveRemoteUser(
            new ResolveRemoteUserRequest { Uuid = Guid.NewGuid().ToString() },
            TestHelpers.CreateCallContext());

        response.Found.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveRemoteUser_UnknownServer_ReturnsNotFound()
    {
        using var fixture = new Fixture();
        // Оба discovery-источника отвечают «не нашли» → пир неизвестен.
        var response = await fixture.Service.ResolveRemoteUser(
            new ResolveRemoteUserRequest { Fid = "@alice:ghost.test" },
            TestHelpers.CreateCallContext());

        response.Found.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveRemoteUser_Success_ViaLoopbackPeer()
    {
        var profile = new GetUserProfileResponse
        {
            Found = true,
            Uuid = Guid.NewGuid().ToString(),
            Username = "alice",
            FirstName = "Alice",
        };
        var stub = new StubS2SApi { OnGetUserProfile = _ => profile };
        await using var server = await LoopbackS2SServer.StartAsync(stub);

        using var fixture = new Fixture();
        await TestHelpers.EnsureActiveKeyAsync(fixture.Context);
        await fixture.KeyCache.RefreshAsync(); // XFedClientInterceptor подписывает исходящий вызов
        await SeedPeerAsync(fixture.Context, server.HostName, server.Endpoint);

        var response = await fixture.Service.ResolveRemoteUser(
            new ResolveRemoteUserRequest { Fid = "@alice:peer.test" },
            TestHelpers.CreateCallContext());

        response.Found.Should().BeTrue();
        response.ServerName.Should().Be("peer.test");
        response.Profile.Uuid.Should().Be(profile.Uuid);
        response.Profile.Username.Should().Be("alice");
    }

    [Fact]
    public async Task ResolveRemoteUser_PeerRpcFails_ReturnsNotFound()
    {
        var stub = new StubS2SApi
        {
            OnGetUserProfile = _ => throw new InvalidOperationException("peer internal error"),
        };
        await using var server = await LoopbackS2SServer.StartAsync(stub);

        using var fixture = new Fixture();
        await TestHelpers.EnsureActiveKeyAsync(fixture.Context);
        await fixture.KeyCache.RefreshAsync();
        await SeedPeerAsync(fixture.Context, server.HostName, server.Endpoint);

        var response = await fixture.Service.ResolveRemoteUser(
            new ResolveRemoteUserRequest { Fid = "@alice:peer.test" },
            TestHelpers.CreateCallContext());

        response.Found.Should().BeFalse();
        response.ServerName.Should().Be("peer.test");
    }
}
