using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using BarkFluff.Bft;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Xunit;

namespace BarkFluff.BftProxy.Tests;

[Collection(BftProxyTestCollection.Name)]
public sealed class ExitPolicyTests(BftProxyFixture fixture)
{
    [Fact]
    public async Task InvalidFirstFlightGetsNoReplyAndClosesAfterDeterministicQuietWindow()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        fixture.Random.DrainSingleByteFills();
        using var client = new TcpClient();
        await client.ConnectAsync(fixture.Proxy.ListenEndPoint, timeout.Token);
        var stream = client.GetStream();
        var invalidFlight = new byte[32 + 18];
        Array.Fill(invalidFlight, (byte)0xA3);
        await stream.WriteAsync(invalidFlight, timeout.Token);
        var response = stream.ReadAsync(new byte[1], timeout.Token).AsTask();

        await fixture.Random.WaitForSingleByteFillAsync(timeout.Token);
        Assert.False(response.IsCompleted);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await response);
    }

    [Fact]
    public async Task UnknownObfuscationKeyIsRejectedWithoutAnyReply()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        fixture.Random.DrainSingleByteFills();
        using var client = new TcpClient();
        await client.ConnectAsync(fixture.Proxy.ListenEndPoint, timeout.Token);
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        var flight = BftClientHandshake.CreateFlight(new BftClientHandshakeOptions
        {
            StaticPublicKey = BftCrypto.GetX25519PublicKey(BftProxyFixture.StaticPrivateKey),
            ObfsKey = wrongKey
        });
        var stream = client.GetStream();
        await stream.WriteAsync(flight.Salt, timeout.Token);
        await stream.WriteAsync(flight.Record, timeout.Token);
        CryptographicOperations.ZeroMemory(wrongKey);
        CryptographicOperations.ZeroMemory(flight.Record);
        var response = stream.ReadAsync(new byte[1], timeout.Token).AsTask();

        await fixture.Random.WaitForSingleByteFillAsync(timeout.Token);
        Assert.False(response.IsCompleted);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await response);
    }

    [Fact]
    public async Task IncompleteFirstRecordClosesAfterTenSecondDeadlineAndQuietWindow()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        fixture.Random.DrainSingleByteFills();
        using var client = new TcpClient();
        await client.ConnectAsync(fixture.Proxy.ListenEndPoint, timeout.Token);
        var flight = BftClientHandshake.CreateFlight(new BftClientHandshakeOptions
        {
            StaticPublicKey = BftCrypto.GetX25519PublicKey(BftProxyFixture.StaticPrivateKey),
            ObfsKey = BftProxyFixture.ObfsKey,
            Salt = Enumerable.Repeat((byte)0x27, 32).ToArray()
        });
        await client.GetStream().WriteAsync(flight.Salt, timeout.Token);
        await client.GetStream().WriteAsync(flight.Record.AsMemory(0, 18), timeout.Token);
        CryptographicOperations.ZeroMemory(flight.Record);
        var response = client.GetStream().ReadAsync(new byte[1], timeout.Token).AsTask();

        await fixture.Time.WaitForTimerAsync(TimeSpan.FromSeconds(10), timeout.Token);
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        await fixture.Random.WaitForSingleByteFillAsync(timeout.Token);
        Assert.False(response.IsCompleted);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await response);
    }

    [Fact]
    public async Task ExitRelayPreambleReadHasTenSecondDeadlineThenQuietClose()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        fixture.Random.DrainSingleByteFills();
        using var client = new TcpClient();
        await client.ConnectAsync(fixture.Proxy.RelayListenEndPoint!, timeout.Token);
        var response = client.GetStream().ReadAsync(new byte[1], timeout.Token).AsTask();
        await fixture.Time.WaitForTimerAsync(TimeSpan.FromSeconds(10), timeout.Token);
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        await fixture.Random.WaitForSingleByteFillAsync(timeout.Token);
        Assert.False(response.IsCompleted);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await response);
    }

    [Fact]
    public async Task PendingConnectionLimitClosesSeventeenthHandshakeFromSameIp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        fixture.Random.DrainSingleByteFills();
        var clients = new List<TcpClient>();
        try
        {
            for (var index = 0; index < 16; index++)
            {
                var client = new TcpClient();
                clients.Add(client);
                await client.ConnectAsync(fixture.Proxy.ListenEndPoint, timeout.Token);
                await client.GetStream().WriteAsync(new byte[50], timeout.Token);
            }
            var rejectedRead = clients.Select(client => client.GetStream().ReadAsync(new byte[1], timeout.Token).AsTask()).ToArray();
            for (var index = 0; index < 16; index++)
                await fixture.Random.WaitForSingleByteFillAsync(timeout.Token);

            using var overflow = new TcpClient();
            await overflow.ConnectAsync(fixture.Proxy.ListenEndPoint, timeout.Token);
            Assert.Equal(0, await overflow.GetStream().ReadAsync(new byte[1], timeout.Token));

            fixture.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.All(await Task.WhenAll(rejectedRead), count => Assert.Equal(0, count));
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }

    [Fact]
    public async Task RemovingAClientKeyDrainsItsTunnelAndRejectsNewConnections()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await fixture.ConnectAsync(timeout.Token, obfsKey: BftProxyFixture.RevocableObfsKey);
        var goAway = client.Tunnel.WaitForGoAwayAsync(timeout.Token);
        await File.WriteAllTextAsync(fixture.KeyFile, $"integration-user {Encode(BftProxyFixture.ObfsKey)}\n");
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0u, await goAway);

        fixture.Random.DrainSingleByteFills();
        using var rejected = new TcpClient();
        await rejected.ConnectAsync(fixture.Proxy.ListenEndPoint, timeout.Token);
        var handshake = BftClientTunnel.ConnectAsync(rejected.GetStream(), new BftClientTunnelOptions
        {
            Handshake = new BftClientHandshakeOptions
            {
                StaticPublicKey = BftCrypto.GetX25519PublicKey(BftProxyFixture.StaticPrivateKey),
                ObfsKey = BftProxyFixture.RevocableObfsKey
            }
        }, timeout.Token).AsTask();
        await fixture.Random.WaitForSingleByteFillAsync(timeout.Token);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<Exception>(async () => await handshake);
    }

    [Fact]
    public async Task ReplacingAClientKeyValueDrainsTunnelsUsingTheOldValue()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var directory = Path.Combine(Path.GetTempPath(), $"bftproxy-key-rotation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var keyFile = Path.Combine(directory, "clients.keys");
        var replacementKey = Enumerable.Repeat((byte)0xA2, 32).ToArray();
        try
        {
            await File.WriteAllTextAsync(keyFile, $"client {Encode(BftProxyFixture.ObfsKey)}\n");
            var configuration = new ExitConfiguration(new ProxyEndpoint("127.0.0.1", 0), null,
                BftProxyFixture.StaticPrivateKey, keyFile,
                [new BftServerObfsKey("client", BftProxyFixture.ObfsKey)], [], new HashSet<IPAddress>(), null,
                "rotation-test", fixture.ProxySecret, new Dictionary<byte, Uri>());
            await using var proxy = await BftProxyHost.StartExitAsync(configuration, fixture.Time, fixture.Random);
            using var socket = new TcpClient();
            await socket.ConnectAsync(proxy.ListenEndPoint, timeout.Token);
            await using var client = new BftTestClient(socket, await BftClientTunnel.ConnectAsync(socket.GetStream(), new BftClientTunnelOptions
            {
                Handshake = new BftClientHandshakeOptions
                {
                    StaticPublicKey = BftCrypto.GetX25519PublicKey(BftProxyFixture.StaticPrivateKey),
                    ObfsKey = BftProxyFixture.ObfsKey
                }
            }, timeout.Token));
            var goAway = client.Tunnel.WaitForGoAwayAsync(timeout.Token);

            await File.WriteAllTextAsync(keyFile, $"client {Encode(replacementKey)}\n");
            fixture.Time.Advance(TimeSpan.FromSeconds(2));

            Assert.Equal(0u, await goAway.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            CryptographicOperations.ZeroMemory(replacementKey);
        }
    }

    [Fact]
    public async Task EstablishedTunnelLimitsSendGoAwayForPerKeyAndGlobalOverflow()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using (var perKeyProxy = await StartExitAsync(maxTunnelsPerKey: 1, maxTunnels: 2))
        {
            await using var first = await ConnectAsync(perKeyProxy, BftProxyFixture.ObfsKey, timeout.Token);
            await using var rejected = await ConnectAsync(perKeyProxy, BftProxyFixture.ObfsKey, timeout.Token);
            Assert.Equal(0u, await rejected.Tunnel.WaitForGoAwayAsync(timeout.Token));
            Assert.False(first.Tunnel.Completion.IsCompleted);
        }

        await using (var globalProxy = await StartExitAsync(maxTunnelsPerKey: 2, maxTunnels: 1))
        {
            await using var first = await ConnectAsync(globalProxy, BftProxyFixture.ObfsKey, timeout.Token);
            await using var rejected = await ConnectAsync(globalProxy, BftProxyFixture.RevocableObfsKey, timeout.Token);
            Assert.Equal(0u, await rejected.Tunnel.WaitForGoAwayAsync(timeout.Token));
            Assert.False(first.Tunnel.Completion.IsCompleted);
        }
    }

    [Fact]
    public void PendingLimiterEnforcesItsTotalLimitAcrossDistinctAddresses()
    {
        var limiter = new PendingConnectionLimiter(2000, 1024);
        var leases = new List<IDisposable>();
        try
        {
            for (var index = 0; index < 1024; index++)
            {
                var address = new IPAddress([(byte)10, (byte)(index >> 8), (byte)index, (byte)1]);
                leases.Add(limiter.TryAcquire(address)!);
                Assert.NotNull(leases[^1]);
            }
            Assert.Null(limiter.TryAcquire(IPAddress.Parse("198.51.100.1")));
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
        }
    }

    [Fact]
    public void RelayPendingLimitUsesClientAddressAfterPreambleInsteadOfRelayAddress()
    {
        var limiter = new PendingConnectionLimiter(16, 1024);
        var leases = new List<PendingConnectionLimiter.Lease>();
        try
        {
            for (var index = 0; index < 17; index++)
            {
                var lease = limiter.TryAcquireUnattributed();
                Assert.NotNull(lease);
                leases.Add(lease);
                Assert.True(lease.TryChangeAddress(new IPAddress([(byte)198, (byte)51, (byte)100, (byte)(index + 1)])));
            }
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
        }
    }

    [Fact]
    public void ExitConfigUsesDefaultAndConfiguredEstablishedTunnelLimits()
    {
        var values = new Dictionary<string, string?>
        {
            ["BFT_MODE"] = "Exit",
            ["BFT_LISTEN"] = "127.0.0.1:8443",
            ["BFT_STATIC_PRIVATE_KEY"] = Encode(BftProxyFixture.StaticPrivateKey),
            ["BFT_CLIENT_KEYS_FILE"] = fixture.KeyFile,
            ["BFT_PROXY_ID"] = "test-proxy",
            ["BFT_ORIGIN_SECRET"] = fixture.ProxySecret
        };
        var defaults = ProxyConfiguration.Parse(values).Exit!;
        Assert.Equal(16, defaults.MaxTunnelsPerKey);
        Assert.Equal(4096, defaults.MaxTunnels);

        values["BFT_MAX_TUNNELS_PER_KEY"] = "3";
        values["BFT_MAX_TUNNELS"] = "17";
        var configured = ProxyConfiguration.Parse(values).Exit!;
        Assert.Equal(3, configured.MaxTunnelsPerKey);
        Assert.Equal(17, configured.MaxTunnels);
    }

    [Fact]
    public void ExitConfigRejectsInvalidEstablishedTunnelLimits()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ProxyConfiguration.Parse(new Dictionary<string, string?>
        {
            ["BFT_MODE"] = "Exit",
            ["BFT_LISTEN"] = "127.0.0.1:8443",
            ["BFT_STATIC_PRIVATE_KEY"] = Encode(BftProxyFixture.StaticPrivateKey),
            ["BFT_CLIENT_KEYS_FILE"] = fixture.KeyFile,
            ["BFT_PROXY_ID"] = "test-proxy",
            ["BFT_ORIGIN_SECRET"] = fixture.ProxySecret,
            ["BFT_MAX_TUNNELS"] = "0"
        }));
        Assert.Contains("BFT_MAX_TUNNELS", error.Message);
    }

    [Fact]
    public void ExitDisablesKestrelMinimumRequestAndResponseDataRates()
    {
        var limits = new KestrelServerLimits();
        Assert.NotNull(limits.MinRequestBodyDataRate);
        Assert.NotNull(limits.MinResponseDataRate);

        BftProxyHost.ConfigureKestrelRequestLimits(limits);

        Assert.Null(limits.MinRequestBodyDataRate);
        Assert.Null(limits.MinResponseDataRate);
    }

    [Fact]
    public void ScopedIpv6ClientIpOmitsItsScopeIdentifier()
    {
        Assert.Equal("fe80::1", BftForwarder.FormatClientIp(IPAddress.Parse("fe80::1%3")));
    }

    [Fact]
    public void UpstreamConnectionPoolExpiresAfterTenMinutes()
    {
        using var handler = BftForwarder.CreateUpstreamHandler(null);
        Assert.Equal(TimeSpan.FromMinutes(10), handler.PooledConnectionLifetime);
    }

    [Fact]
    public void ExitConfigRejectsProxyIdLongerThanTheBftHelloField()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ProxyConfiguration.Parse(new Dictionary<string, string?>
        {
            ["BFT_MODE"] = "Exit",
            ["BFT_LISTEN"] = "127.0.0.1:8443",
            ["BFT_STATIC_PRIVATE_KEY"] = Encode(BftProxyFixture.StaticPrivateKey),
            ["BFT_CLIENT_KEYS_FILE"] = fixture.KeyFile,
            ["BFT_PROXY_ID"] = new string('x', 33),
            ["BFT_ORIGIN_SECRET"] = fixture.ProxySecret
        }));

        Assert.Contains("BFTv1 HELLO", error.Message);
    }

    private static string Encode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private Task<BftProxyHost> StartExitAsync(int maxTunnelsPerKey, int maxTunnels) => BftProxyHost.StartExitAsync(
        new ExitConfiguration(new ProxyEndpoint("127.0.0.1", 0), null, BftProxyFixture.StaticPrivateKey, fixture.KeyFile,
            [new BftServerObfsKey("integration-user", BftProxyFixture.ObfsKey),
                new BftServerObfsKey("revocable-user", BftProxyFixture.RevocableObfsKey)], [], new HashSet<IPAddress>(), null,
            "limits-test", fixture.ProxySecret, new Dictionary<byte, Uri>(), maxTunnelsPerKey, maxTunnels),
        fixture.Time, fixture.Random);

    private static async Task<BftTestClient> ConnectAsync(BftProxyHost proxy, byte[] obfsKey, CancellationToken cancellationToken)
    {
        var socket = new TcpClient();
        try
        {
            await socket.ConnectAsync(proxy.ListenEndPoint, cancellationToken);
            var tunnel = await BftClientTunnel.ConnectAsync(socket.GetStream(), new BftClientTunnelOptions
            {
                Handshake = new BftClientHandshakeOptions
                {
                    StaticPublicKey = BftCrypto.GetX25519PublicKey(BftProxyFixture.StaticPrivateKey),
                    ObfsKey = obfsKey
                }
            }, cancellationToken);
            return new BftTestClient(socket, tunnel);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
