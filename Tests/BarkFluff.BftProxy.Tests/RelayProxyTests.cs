using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BarkFluff.Bft;
using BarkFluff.BftProxy;
using Grpc.Core;
using Xunit;

namespace BarkFluff.BftProxy.Tests;

[Collection(BftProxyTestCollection.Name)]
public sealed class RelayProxyTests(BftProxyFixture fixture)
{
    [Fact]
    public async Task RelayForwardsGrpcAndHttpAndCombinesPreambleSaltAndFirstRecord()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var relay = await StartRelayAsync();
        await using var client = await fixture.ConnectViaRelayAsync(relay, timeout.Token);
        Assert.Equal(new byte[] { 1, 2, 4, 5, 7 }, client.Tunnel.Hello.ServiceIds);
        Assert.True(relay.LastInitialWriteLength > BftRelayPreamble.Length + 32);

        using (var http = client.CreateHttpClient(2, HttpVersion.Version11))
        using (var response = await http.GetAsync("/headers", timeout.Token))
        {
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            Assert.Equal("127.0.0.1", json.RootElement.GetProperty("clientIp").GetString());
            Assert.Equal("test-proxy", json.RootElement.GetProperty("proxyId").GetString());
        }

        using var channel = client.CreateGrpcChannel(1);
        var reply = await new ProxyTest.ProxyTestClient(channel)
            .UnaryAsync(new EchoRequest { Value = "relay" }, cancellationToken: timeout.Token).ResponseAsync;
        Assert.Equal("relay|HTTP/2", reply.Value);
    }

    [Fact]
    public async Task ExitAndRelayNormalizeIpv6ClientAddressForUpstream()
    {
        Assert.True(Socket.OSSupportsIPv6);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ipv6ExitConfig = new ExitConfiguration(new ProxyEndpoint("::1", 0), new ProxyEndpoint("::1", 0),
            BftProxyFixture.StaticPrivateKey, fixture.KeyFile,
            [new BftServerObfsKey("integration-user", BftProxyFixture.ObfsKey)], [],
            new HashSet<IPAddress> { IPAddress.IPv6Loopback }, fixture.RelayKey,
            "test-proxy", fixture.ProxySecret, new Dictionary<byte, Uri> { [2] = fixture.Upstream.Address });
        await using var ipv6Exit = await BftProxyHost.StartExitAsync(ipv6ExitConfig, fixture.Time, fixture.Random, fixture.VerifyUpstreamCertificate);

        await using (var direct = await ConnectOverIpv6Async(ipv6Exit.ListenEndPoint, timeout.Token))
            Assert.Equal("::1", await GetTrustedClientIpAsync(direct, timeout.Token));

        await using var relay = await BftRelayHost.StartAsync(new RelayConfiguration(
            new ProxyEndpoint("::1", 0), new ProxyEndpoint("::1", ipv6Exit.RelayListenEndPoint!.Port),
            "relay-ipv6", fixture.RelayKey, 4, 8, TimeSpan.FromSeconds(3)), fixture.Time, fixture.Random);
        await using var throughRelay = await ConnectOverIpv6Async(relay.ListenEndPoint, timeout.Token);
        Assert.Equal("::1", await GetTrustedClientIpAsync(throughRelay, timeout.Token));
    }

    [Fact]
    public async Task RelayConfigurationNeedsNoExitOrObfuscationKeys()
    {
        var configuration = ProxyConfiguration.Parse(new Dictionary<string, string?>
        {
            ["BFT_MODE"] = "Relay",
            ["BFT_LISTEN"] = "127.0.0.1:8443",
            ["BFT_NEXT_HOP"] = "exit.example:8444",
            ["BFT_RELAY_ID"] = "relay-a",
            ["BFT_RELAY_KEY"] = Encode(fixture.RelayKey),
            ["BFT_RELAY_MAX_CONNECTIONS_PER_IP"] = "7",
            ["BFT_RELAY_MAX_CONNECTIONS"] = "127",
            ["BFT_RELAY_CONNECT_TIMEOUT_SECONDS"] = "9"
        });

        Assert.Null(configuration.Exit);
        Assert.NotNull(configuration.Relay);
        Assert.Equal(7, configuration.Relay.MaxConnectionsPerIp);
        Assert.Equal(127, configuration.Relay.MaxConnections);
        Assert.Equal(TimeSpan.FromSeconds(9), configuration.Relay.ConnectTimeout);
    }

    [Fact]
    public async Task RelaySlowlorisIsLimitedAndClosedAtTheSaltDeadline()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var relay = await StartRelayAsync(maxConnectionsPerIp: 1, maxConnections: 2);
        using var slowClient = new TcpClient();
        await slowClient.ConnectAsync(relay.ListenEndPoint, timeout.Token);
        var slowRead = slowClient.GetStream().ReadAsync(new byte[1], timeout.Token).AsTask();
        await fixture.Time.WaitForTimerAsync(TimeSpan.FromSeconds(10), timeout.Token);

        using var rejectedClient = new TcpClient();
        await rejectedClient.ConnectAsync(relay.ListenEndPoint, timeout.Token);
        Assert.Equal(0, await rejectedClient.GetStream().ReadAsync(new byte[1], timeout.Token));

        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, await slowRead);
    }

    [Fact]
    public async Task ExitRelayRejectsPreambleBoundToAnotherSaltWithoutReply()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        fixture.Random.DrainSingleByteFills();
        using var client = new TcpClient();
        await client.ConnectAsync(fixture.Proxy.RelayListenEndPoint!, timeout.Token);
        var stream = client.GetStream();
        var salt = Enumerable.Repeat((byte)0x31, 32).ToArray();
        var otherSalt = Enumerable.Repeat((byte)0x32, 32).ToArray();
        var nonce = Enumerable.Range(0, BftRelayPreamble.RelayNonceLength).Select(index => (byte)(index + 1)).ToArray();
        var preamble = BftRelayPreamble.Create(fixture.RelayKey, salt, nonce, IPAddress.Loopback, 41000,
            fixture.Time.GetUtcNow().ToUnixTimeSeconds(), "relay-a");
        await stream.WriteAsync(preamble.Concat(otherSalt).ToArray(), timeout.Token);
        var response = stream.ReadAsync(new byte[1], timeout.Token).AsTask();
        await fixture.Random.WaitForSingleByteFillAsync(timeout.Token);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await response);
    }

    [Fact]
    public async Task ExitRelayRejectsExpiredPreambleWithoutReply()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        fixture.Random.DrainSingleByteFills();
        using var client = new TcpClient();
        await client.ConnectAsync(fixture.Proxy.RelayListenEndPoint!, timeout.Token);
        var stream = client.GetStream();
        var salt = Enumerable.Repeat((byte)0x44, 32).ToArray();
        var nonce = Enumerable.Range(0, BftRelayPreamble.RelayNonceLength).Select(index => (byte)(index + 1)).ToArray();
        var preamble = BftRelayPreamble.Create(fixture.RelayKey, salt, nonce, IPAddress.Loopback, 41001,
            fixture.Time.GetUtcNow().ToUnixTimeSeconds() - 121, "relay-a");
        await stream.WriteAsync(preamble.Concat(salt).ToArray(), timeout.Token);
        var response = stream.ReadAsync(new byte[1], timeout.Token).AsTask();
        await fixture.Random.WaitForSingleByteFillAsync(timeout.Token);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await response);
    }

    [Fact]
    public async Task ExitRelayPortImmediatelyClosesNonAllowlistedPeer()
    {
        var configuration = new ExitConfiguration(new ProxyEndpoint("127.0.0.1", 0), new ProxyEndpoint("127.0.0.1", 0),
            BftProxyFixture.StaticPrivateKey, fixture.KeyFile,
            [new BftServerObfsKey("integration-user", BftProxyFixture.ObfsKey)], [],
            new HashSet<IPAddress> { IPAddress.Parse("192.0.2.1") }, fixture.RelayKey,
            "test-proxy", fixture.ProxySecret, new Dictionary<byte, Uri>());
        await using var isolatedExit = await BftProxyHost.StartExitAsync(configuration, fixture.Time, fixture.Random);
        using var client = new TcpClient();
        await client.ConnectAsync(isolatedExit.RelayListenEndPoint!, CancellationToken.None);
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1]));
    }

    [Fact]
    public async Task RelayRejectsRepeatedClientSaltAndLeavesTheFirstTunnelAlive()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        fixture.Random.DrainSingleByteFills();
        await using var relay = await StartRelayAsync();
        var salt = Enumerable.Repeat((byte)0x67, 32).ToArray();
        await using var first = await fixture.ConnectViaRelayAsync(relay, timeout.Token, salt);

        var duplicateTask = fixture.ConnectViaRelayAsync(relay, timeout.Token, salt);
        await fixture.Random.WaitForSingleByteFillAsync(timeout.Token);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<Exception>(() => duplicateTask);
        Assert.False(first.Tunnel.Completion.IsCompleted);
    }

    [Fact]
    public async Task RelayClosesClientWhenNextHopIsUnavailable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var port = ReserveUnusedPort();
        var config = new RelayConfiguration(new ProxyEndpoint("127.0.0.1", 0), new ProxyEndpoint("127.0.0.1", port),
            "relay-a", fixture.RelayKey, 1, 2, TimeSpan.FromSeconds(1));
        await using var relay = await BftRelayHost.StartAsync(config, fixture.Time, fixture.Random);
        using var client = new TcpClient();
        await client.ConnectAsync(relay.ListenEndPoint, timeout.Token);
        await client.GetStream().WriteAsync(new byte[32], timeout.Token);
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1], timeout.Token));
    }

    private Task<BftRelayHost> StartRelayAsync(int maxConnectionsPerIp = 8, int maxConnections = 32) =>
        BftRelayHost.StartAsync(new RelayConfiguration(
            new ProxyEndpoint("127.0.0.1", 0),
            new ProxyEndpoint("127.0.0.1", fixture.Proxy.RelayListenEndPoint!.Port),
            "relay-a", fixture.RelayKey, maxConnectionsPerIp, maxConnections, TimeSpan.FromSeconds(3)),
            fixture.Time, fixture.Random);

    private static async Task<BftTestClient> ConnectOverIpv6Async(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var socket = new TcpClient(AddressFamily.InterNetworkV6);
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken);
            var tunnel = await BftClientTunnel.ConnectAsync(socket.GetStream(), new BftClientTunnelOptions
            {
                Handshake = new BftClientHandshakeOptions
                {
                    StaticPublicKey = BftCrypto.GetX25519PublicKey(BftProxyFixture.StaticPrivateKey),
                    ObfsKey = BftProxyFixture.ObfsKey
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

    private async Task<string?> GetTrustedClientIpAsync(BftTestClient client, CancellationToken cancellationToken)
    {
        using var http = client.CreateHttpClient(2, HttpVersion.Version11);
        using var response = await http.GetAsync("/headers", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("clientIp").GetString();
    }

    private static int ReserveUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string Encode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
