using BarkFluff.Federation.BackgroundServices;
using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Federation.Tests.BackgroundServices;

public class PeerRefreshBackgroundServiceTests
{
    private static readonly byte[] TrustedPublicKey = Enumerable.Repeat((byte)7, 32).ToArray();
    private static readonly byte[] NewPublicKey = Enumerable.Repeat((byte)9, 32).ToArray();

    private static (ServiceProvider Provider, PeerRefreshBackgroundService Service) Create(
        TestHelpers.TestDatabase db,
        FakeWellKnownClient wellKnown,
        IDictionary<string, string?>? configOverrides = null)
    {
        var provider = TestHelpers.CreateProvider(db, TestHelpers.CreateConfiguration(configOverrides), wellKnownClient: wellKnown);
        var service = new PeerRefreshBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<FederationSwitch>(),
            provider.GetRequiredService<BarkFluff.GrpcServer.Metrics.MetricsCollector>(),
            NullLogger<PeerRefreshBackgroundService>.Instance);
        return (provider, service);
    }

    private static async Task SeedPeerAsync(
        TestHelpers.TestDatabase db,
        string serverName,
        KnownServerSource source,
        KnownServerStatus status,
        DateTime? lastKeyRefreshAt,
        bool withTrustedKey = true)
    {
        await using var context = TestHelpers.CreateContext(db);
        var server = new KnownServer
        {
            ServerName = serverName,
            FederationEndpoint = $"https://federation.{serverName}",
            Source = source,
            Status = status,
            FirstSeenAt = DateTime.UtcNow.AddDays(-10),
            LastSeenAt = DateTime.UtcNow.AddDays(-10),
            LastKeyRefreshAt = lastKeyRefreshAt,
            ProtocolVersion = 1,
        };
        if (withTrustedKey)
        {
            server.Keys.Add(new KnownServerKey
            {
                ServerName = serverName,
                KeyId = "ed25519:1",
                PublicKey = TrustedPublicKey,
            });
        }
        context.KnownServers.Add(server);
        await context.SaveChangesAsync();
    }

    // Документ, подписанный доверенным ключом — проходит континуитет доверия (P1-11).
    private static RemoteServerDocument TrustedDoc(string serverName, params (string KeyId, byte[] Pub)[] keys)
        => new(
            serverName,
            $"https://federation.{serverName}",
            [],
            [1],
            keys.Select(k => new RemoteSigningKey(k.KeyId, k.Pub, null)).ToList(),
            "ed25519:1",
            TrustedPublicKey);

    private static async Task<KnownServer> ReloadAsync(TestHelpers.TestDatabase db, string serverName)
    {
        await using var context = TestHelpers.CreateContext(db);
        return await context.KnownServers.Include(s => s.Keys).SingleAsync(s => s.ServerName == serverName);
    }

    private static async Task RunOnceAsync(PeerRefreshBackgroundService service, int settleMs = 300)
    {
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(settleMs); // первая итерация выполняется сразу при старте
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Refresh_InactiveSwitch_PeersUntouched()
    {
        var db = TestHelpers.CreateDatabase();
        await SeedPeerAsync(db, "peer.test", KnownServerSource.WellKnown, KnownServerStatus.Active, lastKeyRefreshAt: null);

        var wellKnown = new FakeWellKnownClient { Result = TrustedDoc("peer.test", ("ed25519:1", TrustedPublicKey)) };
        var (provider, service) = Create(db, wellKnown, new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = "false",
        });
        using (provider)
        {
            await RunOnceAsync(service);

            (await ReloadAsync(db, "peer.test")).LastKeyRefreshAt.Should().BeNull();
            wellKnown.CallCount.Should().Be(0, "выключенная нода не ходит в сеть");
        }
    }

    [Fact]
    public async Task Refresh_ManualPeerDue_RefreshesKeysWithTrustedContinuity()
    {
        var db = TestHelpers.CreateDatabase();
        await SeedPeerAsync(db, "peer.test", KnownServerSource.Manual, KnownServerStatus.Active, DateTime.UtcNow.AddDays(-2));

        var wellKnown = new FakeWellKnownClient
        {
            Result = TrustedDoc("peer.test", ("ed25519:1", TrustedPublicKey), ("ed25519:2", NewPublicKey)),
        };
        var (provider, service) = Create(db, wellKnown);
        using (provider)
        {
            await RunOnceAsync(service);

            var peer = await ReloadAsync(db, "peer.test");
            peer.Keys.Should().Contain(k => k.KeyId == "ed25519:2", "новый ключ из well-known подтянут");
            peer.Keys.Should().Contain(k => k.KeyId == "ed25519:1" && k.RevokedAt == null, "старый ключ не отозван");
            peer.LastKeyRefreshAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public async Task Refresh_ManualPeerNotDue_Skipped()
    {
        var db = TestHelpers.CreateDatabase();
        await SeedPeerAsync(db, "peer.test", KnownServerSource.Manual, KnownServerStatus.Active, DateTime.UtcNow.AddHours(-1));

        var wellKnown = new FakeWellKnownClient
        {
            Result = TrustedDoc("peer.test", ("ed25519:1", TrustedPublicKey), ("ed25519:2", NewPublicKey)),
        };
        var (provider, service) = Create(db, wellKnown);
        using (provider)
        {
            await RunOnceAsync(service);

            (await ReloadAsync(db, "peer.test")).Keys.Should().HaveCount(1);
            wellKnown.CallCount.Should().Be(0, "refresh раз в сутки — час назад ещё свежо");
        }
    }

    [Fact]
    public async Task Refresh_DiscoveryPeerFailing_MarkedUnreachableAfterThreeFailures()
    {
        var db = TestHelpers.CreateDatabase();
        await SeedPeerAsync(db, "peer.test", KnownServerSource.WellKnown, KnownServerStatus.Active, lastKeyRefreshAt: null);

        // Оба источника недоступны → ResolveAsync возвращает null → счётчик неудач растёт.
        var wellKnown = new FakeWellKnownClient { Result = null };
        var (provider, service) = Create(db, wellKnown);
        using (provider)
        {
            await RunOnceAsync(service);
            (await ReloadAsync(db, "peer.test")).Status.Should().Be(KnownServerStatus.Active);

            await RunOnceAsync(service);
            (await ReloadAsync(db, "peer.test")).Status.Should().Be(KnownServerStatus.Active);

            await RunOnceAsync(service);
            (await ReloadAsync(db, "peer.test")).Status.Should().Be(KnownServerStatus.Unreachable, "три неудачи подряд → Unreachable");
        }
    }

    [Fact]
    public async Task Refresh_UnreachablePeerRecovers_WhenDiscoveryHeals()
    {
        var db = TestHelpers.CreateDatabase();
        await SeedPeerAsync(db, "peer.test", KnownServerSource.WellKnown, KnownServerStatus.Unreachable, lastKeyRefreshAt: null);

        var wellKnown = new FakeWellKnownClient { Result = TrustedDoc("peer.test", ("ed25519:1", TrustedPublicKey)) };
        var (provider, service) = Create(db, wellKnown);
        using (provider)
        {
            await RunOnceAsync(service);

            var peer = await ReloadAsync(db, "peer.test");
            peer.Status.Should().Be(KnownServerStatus.Active, "успешный резолв возвращает пира в Active");
            peer.LastKeyRefreshAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public async Task Refresh_FreshPeer_Skipped()
    {
        var db = TestHelpers.CreateDatabase();
        await SeedPeerAsync(db, "peer.test", KnownServerSource.WellKnown, KnownServerStatus.Active, DateTime.UtcNow.AddHours(-1));

        var wellKnown = new FakeWellKnownClient { Result = null };
        var (provider, service) = Create(db, wellKnown);
        using (provider)
        {
            await RunOnceAsync(service);

            (await ReloadAsync(db, "peer.test")).Status.Should().Be(KnownServerStatus.Active);
        }
    }
}
