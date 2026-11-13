using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using BarkFluff.Bft;
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
}
