using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Federation.Tests.Services;

// docs/rearch/phase-1/step-1.4-discovery-knownservers.md, критерий готовности №1:
// юнит-тесты Resolver'а (моки трёх источников, порядок фолбэков, кросс-сверка, ServerNotFound).
public class ServerResolverTests
{
    private static FederationContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FederationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FederationContext(options);
    }

    private static ServerResolver CreateResolver(FederationContext context, FakeWellKnownClient wellKnown, FakeNavigatorClient navigator)
        => new(context, wellKnown, navigator, new MetricsCollector(), NullLogger<ServerResolver>.Instance);

    private static RemoteServerDocument Doc(string serverName, string endpoint, params (string KeyId, byte[] Pub)[] keys)
        => new(serverName, endpoint, [], [1], keys.Select(k => new RemoteSigningKey(k.KeyId, k.Pub, null)).ToList());

    private static byte[] FakeKey(byte seed) => Enumerable.Repeat(seed, 32).ToArray();

    [Fact]
    public async Task ResolveAsync_InvalidSyntax_ReturnsNull()
    {
        await using var context = CreateContext();
        var resolver = CreateResolver(context, new FakeWellKnownClient(), new FakeNavigatorClient());

        var result = await resolver.ResolveAsync("localhost");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_BothSourcesUnavailable_ReturnsNull_ServerNotFound()
    {
        await using var context = CreateContext();
        var wellKnown = new FakeWellKnownClient { Result = null };
        var navigator = new FakeNavigatorClient { Result = null };
        var resolver = CreateResolver(context, wellKnown, navigator);

        var result = await resolver.ResolveAsync("chat.example.org");

        result.Should().BeNull();
        wellKnown.CallCount.Should().Be(1);
        navigator.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_WellKnownAvailable_KeysMatchNavigator_Upserts()
    {
        await using var context = CreateContext();
        var key = FakeKey(1);
        var wellKnown = new FakeWellKnownClient { Result = Doc("chat.example.org", "https://fed.chat.example.org", ("ed25519:1", key)) };
        var navigator = new FakeNavigatorClient { Result = Doc("chat.example.org", "https://fed.chat.example.org", ("ed25519:1", key)) };
        var resolver = CreateResolver(context, wellKnown, navigator);

        var result = await resolver.ResolveAsync("chat.example.org");

        result.Should().NotBeNull();
        result!.Source.Should().Be(KnownServerSource.WellKnown);
        result.Keys.Should().ContainSingle(k => k.KeyId == "ed25519:1");
    }

    [Fact]
    public async Task ResolveAsync_FirstContact_CrosscheckMismatch_ReturnsNull()
    {
        await using var context = CreateContext();
        var wellKnown = new FakeWellKnownClient { Result = Doc("chat.example.org", "https://fed.chat.example.org", ("ed25519:1", FakeKey(1))) };
        var navigator = new FakeNavigatorClient { Result = Doc("chat.example.org", "https://fed.chat.example.org", ("ed25519:1", FakeKey(2))) };
        var resolver = CreateResolver(context, wellKnown, navigator);

        var result = await resolver.ResolveAsync("chat.example.org");

        result.Should().BeNull();
        (await context.KnownServers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_WellKnownUnavailable_FallsBackToNavigator()
    {
        await using var context = CreateContext();
        var wellKnown = new FakeWellKnownClient { Result = null };
        var navigator = new FakeNavigatorClient { Result = Doc("chat.example.org", "https://fed.chat.example.org", ("ed25519:1", FakeKey(1))) };
        var resolver = CreateResolver(context, wellKnown, navigator);

        var result = await resolver.ResolveAsync("chat.example.org");

        result.Should().NotBeNull();
        result!.Source.Should().Be(KnownServerSource.Navigator);
    }

    [Fact]
    public async Task ResolveAsync_ManualPeer_NeverRefetched()
    {
        await using var context = CreateContext();
        context.KnownServers.Add(new KnownServer
        {
            ServerName = "friend.example.org",
            FederationEndpoint = "http://10.0.0.5:7030",
            TlsSpkiSha256 = [],
            Source = KnownServerSource.Manual,
            Status = KnownServerStatus.Active,
            FirstSeenAt = DateTime.UtcNow.AddDays(-100),
            LastSeenAt = DateTime.UtcNow.AddDays(-100),
            LastKeyRefreshAt = DateTime.UtcNow.AddDays(-100),
        });
        await context.SaveChangesAsync();

        var wellKnown = new FakeWellKnownClient();
        var navigator = new FakeNavigatorClient();
        var resolver = CreateResolver(context, wellKnown, navigator);

        var result = await resolver.ResolveAsync("friend.example.org");

        result.Should().NotBeNull();
        wellKnown.CallCount.Should().Be(0);
        navigator.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_FreshCache_ReturnsWithoutCallingSources()
    {
        await using var context = CreateContext();
        context.KnownServers.Add(new KnownServer
        {
            ServerName = "chat.example.org",
            FederationEndpoint = "https://fed.chat.example.org",
            TlsSpkiSha256 = [],
            Source = KnownServerSource.WellKnown,
            Status = KnownServerStatus.Active,
            FirstSeenAt = DateTime.UtcNow.AddHours(-1),
            LastSeenAt = DateTime.UtcNow.AddHours(-1),
            LastKeyRefreshAt = DateTime.UtcNow.AddHours(-1),
        });
        await context.SaveChangesAsync();

        var wellKnown = new FakeWellKnownClient();
        var navigator = new FakeNavigatorClient();
        var resolver = CreateResolver(context, wellKnown, navigator);

        var result = await resolver.ResolveAsync("chat.example.org");

        result.Should().NotBeNull();
        wellKnown.CallCount.Should().Be(0);
        navigator.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_BlockedPeer_ReturnsNull()
    {
        await using var context = CreateContext();
        context.KnownServers.Add(new KnownServer
        {
            ServerName = "bad.example.org",
            FederationEndpoint = "https://fed.bad.example.org",
            TlsSpkiSha256 = [],
            Source = KnownServerSource.WellKnown,
            Status = KnownServerStatus.Blocked,
            FirstSeenAt = DateTime.UtcNow.AddDays(-2),
            LastSeenAt = DateTime.UtcNow.AddDays(-2),
            LastKeyRefreshAt = DateTime.UtcNow.AddDays(-2),
        });
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, new FakeWellKnownClient(), new FakeNavigatorClient());

        var result = await resolver.ResolveAsync("bad.example.org");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_KeyRotationWithoutTrustedContinuity_KeepsOldRecordUnchanged()
    {
        await using var context = CreateContext();
        var oldKey = FakeKey(1);
        context.KnownServers.Add(new KnownServer
        {
            ServerName = "chat.example.org",
            FederationEndpoint = "https://fed.chat.example.org",
            TlsSpkiSha256 = [],
            Source = KnownServerSource.WellKnown,
            Status = KnownServerStatus.Active,
            FirstSeenAt = DateTime.UtcNow.AddDays(-2),
            LastSeenAt = DateTime.UtcNow.AddDays(-2),
            LastKeyRefreshAt = DateTime.UtcNow.AddDays(-2),
            Keys = [new KnownServerKey { ServerName = "chat.example.org", KeyId = "ed25519:1", PublicKey = oldKey }],
        });
        await context.SaveChangesAsync();

        // Новый документ подписан совершенно другим ключом — старого ed25519:1 в нём нет.
        var wellKnown = new FakeWellKnownClient { Result = Doc("chat.example.org", "https://fed.chat.example.org", ("ed25519:9", FakeKey(9))) };
        var resolver = CreateResolver(context, wellKnown, new FakeNavigatorClient());

        var result = await resolver.ResolveAsync("chat.example.org");

        result.Should().NotBeNull();
        result!.Keys.Should().ContainSingle(k => k.KeyId == "ed25519:1"); // не обновлено
    }

    [Fact]
    public async Task ResolveAsync_KeyRotationWithTrustedContinuity_UpsertsNewKey()
    {
        await using var context = CreateContext();
        var oldKey = FakeKey(1);
        context.KnownServers.Add(new KnownServer
        {
            ServerName = "chat.example.org",
            FederationEndpoint = "https://fed.chat.example.org",
            TlsSpkiSha256 = [],
            Source = KnownServerSource.WellKnown,
            Status = KnownServerStatus.Active,
            FirstSeenAt = DateTime.UtcNow.AddDays(-2),
            LastSeenAt = DateTime.UtcNow.AddDays(-2),
            LastKeyRefreshAt = DateTime.UtcNow.AddDays(-2),
            Keys = [new KnownServerKey { ServerName = "chat.example.org", KeyId = "ed25519:1", PublicKey = oldKey }],
        });
        await context.SaveChangesAsync();

        // Ротация: новый документ содержит и старый ed25519:1 (с overlap), и новый ed25519:2.
        var wellKnown = new FakeWellKnownClient
        {
            Result = Doc("chat.example.org", "https://fed.chat.example.org", ("ed25519:1", oldKey), ("ed25519:2", FakeKey(2))),
        };
        var resolver = CreateResolver(context, wellKnown, new FakeNavigatorClient());

        var result = await resolver.ResolveAsync("chat.example.org");

        result.Should().NotBeNull();
        result!.Keys.Should().Contain(k => k.KeyId == "ed25519:2");
    }
}
