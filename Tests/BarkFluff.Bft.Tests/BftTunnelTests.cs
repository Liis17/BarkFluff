using System.Security.Cryptography;
using BarkFluff.Bft;
using Xunit;

namespace BarkFluff.Bft.Tests;

public sealed class BftTunnelTests
{
    private static readonly byte[] ExitPrivateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] ClientEphemeral = Enumerable.Range(0x61, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] ServerEphemeral = Enumerable.Range(0x81, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] ObfsKey = Enumerable.Range(0x20, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public async Task TunnelHandshake_AuthModesAndServiceStreams()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var pair = await ConnectAsync(null, [] , timeout.Token);
        Assert.Equal("lv-1", pair.Client.Hello.ProxyId);
        Assert.Equal(new byte[] { 1, 4, 5 }, pair.Client.Hello.ServiceIds);

        var first = await pair.Client.OpenStreamAsync(1, timeout.Token);
        var second = await pair.Client.OpenStreamAsync(4, timeout.Token);
        var third = await pair.Client.OpenStreamAsync(5, timeout.Token);
        var accepted = new[]
        {
            await pair.Server.AcceptStreamAsync(timeout.Token),
            await pair.Server.AcceptStreamAsync(timeout.Token),
            await pair.Server.AcceptStreamAsync(timeout.Token)
        };
        Assert.Equal(new byte[] { 1, 4, 5 }, accepted.Select(stream => stream.ServiceId));
        Assert.Equal(new uint[] { 1, 2, 3 }, accepted.Select(stream => stream.StreamId));

        await using var validPair = await ConnectAsync("valid-token", ["valid-token"], timeout.Token);
        Assert.Equal("lv-1", validPair.Client.Hello.ProxyId);

        var invalidPair = CreateTransportPair();
        await using var invalidClientTransport = invalidPair.Client;
        await using var invalidServerTransport = invalidPair.Server;
        var invalidServer = BftServerTunnel.TryAcceptAsync(invalidPair.Server, ServerOptions(["valid-token"]), timeout.Token).AsTask();
        var invalidClient = BftClientTunnel.ConnectAsync(invalidPair.Client, ClientOptions("wrong-token"), timeout.Token).AsTask();
        await Assert.ThrowsAsync<BftAuthenticationException>(async () => await invalidClient);
        var rejected = await invalidServer;
        Assert.False(rejected.Accepted);
        Assert.True(rejected.AuthenticationRejected);
    }

    [Fact]
    public async Task SlowStreamDoesNotBlockOtherStreams_AndTransfers64MiB()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var pair = await ConnectAsync(null, [], timeout.Token);
        var slowClient = await pair.Client.OpenStreamAsync(1, timeout.Token);
        var fastClient = await pair.Client.OpenStreamAsync(4, timeout.Token);
        var slowServer = await pair.Server.AcceptStreamAsync(timeout.Token);
        var fastServer = await pair.Server.AcceptStreamAsync(timeout.Token);

        var block = Enumerable.Range(0, 1024).Select(value => (byte)(value * 31)).ToArray();
        var slowWriter = Task.Run(async () =>
        {
            for (var i = 0; i < 4096; i++)
                await slowClient.WriteAsync(block, timeout.Token);
            await slowClient.CompleteWritesAsync();
        }, timeout.Token);

        var marker = "fast"u8.ToArray();
        await fastClient.WriteAsync(marker, timeout.Token);
        await fastClient.CompleteWritesAsync();
        var receivedMarker = new byte[marker.Length];
        await fastServer.ReadExactlyAsync(receivedMarker, timeout.Token);
        Assert.Equal(marker, receivedMarker);
        Assert.False(slowWriter.IsCompleted);

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var receivedBlock = new byte[block.Length];
        var total = 0L;
        while (true)
        {
            var count = await slowServer.ReadAsync(receivedBlock, timeout.Token);
            if (count == 0) break;
            sha.AppendData(receivedBlock.AsSpan(0, count));
            total += count;
        }
        await slowWriter;
        Assert.Equal(4L * 1024 * 1024, total);
        var expectedSlowHash = HashRepeated(block, 4096);
        Assert.Equal(expectedSlowHash, sha.GetHashAndReset());

        var bigClient = await pair.Client.OpenStreamAsync(5, timeout.Token);
        var bigServer = await pair.Server.AcceptStreamAsync(timeout.Token);
        const int blockCount = 65536;
        var bigBlock = Enumerable.Range(0, 1024).Select(value => (byte)(value * 13)).ToArray();
        var expectedBigHash = HashRepeated(bigBlock, blockCount);
        using var bigHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var receivedBigBlock = new byte[bigBlock.Length];
        for (var i = 0; i < blockCount; i++)
        {
            await bigClient.WriteAsync(bigBlock, timeout.Token);
            await bigServer.ReadExactlyAsync(receivedBigBlock, timeout.Token);
            bigHash.AppendData(receivedBigBlock);
        }
        await bigClient.CompleteWritesAsync();
        Assert.Equal(0, await bigServer.ReadAsync(new byte[1], timeout.Token));
        Assert.Equal(expectedBigHash, bigHash.GetHashAndReset());
        Assert.True(pair.Client.OutgoingMessageCount >= 65536);
    }

    [Fact]
    public async Task GoAwayRecordsBoundaryAndRejectsNewOpens()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var pair = await ConnectAsync(null, [], timeout.Token);
        _ = await pair.Client.OpenStreamAsync(1, timeout.Token);
        _ = await pair.Server.AcceptStreamAsync(timeout.Token);
        await pair.Server.SendGoAwayAsync(BftGoAwayCode.Shutdown, timeout.Token);
        Assert.Equal(1u, await pair.Client.WaitForGoAwayAsync(timeout.Token));
        await Assert.ThrowsAsync<BftGoAwayException>(async () => await pair.Client.OpenStreamAsync(4, timeout.Token));
    }

    [Fact]
    public async Task HalfCloseAndConcurrentResetLeaveStreamsClosed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var pair = await ConnectAsync(null, [], timeout.Token);
        var client = await pair.Client.OpenStreamAsync(1, timeout.Token);
        var server = await pair.Server.AcceptStreamAsync(timeout.Token);

        var request = "request"u8.ToArray();
        await client.WriteAsync(request, timeout.Token);
        await client.CompleteWritesAsync();
        var receivedRequest = new byte[request.Length];
        await server.ReadExactlyAsync(receivedRequest, timeout.Token);
        Assert.Equal(request, receivedRequest);
        Assert.Equal(0, await server.ReadAsync(new byte[1], timeout.Token));

        var response = "response"u8.ToArray();
        await server.WriteAsync(response, timeout.Token);
        await server.CompleteWritesAsync();
        var receivedResponse = new byte[response.Length];
        await client.ReadExactlyAsync(receivedResponse, timeout.Token);
        Assert.Equal(response, receivedResponse);
        Assert.Equal(0, await client.ReadAsync(new byte[1], timeout.Token));

        var racingClient = await pair.Client.OpenStreamAsync(4, timeout.Token);
        var racingServer = await pair.Server.AcceptStreamAsync(timeout.Token);
        var incomingBeforeReset = pair.Client.IncomingMessageCount;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var close = Task.Run(async () =>
        {
            await start.Task;
            await racingClient.CompleteWritesAsync();
        }, timeout.Token);
        var reset = Task.Run(async () =>
        {
            await start.Task;
            await racingServer.ResetAsync(BftResetCode.Cancel);
        }, timeout.Token);
        start.TrySetResult();
        await Task.WhenAll(close, reset);
        await pair.Client.Connection.WaitForIncomingMessageCountAsync(incomingBeforeReset + 1, timeout.Token);
        await Assert.ThrowsAsync<IOException>(async () => await racingClient.ReadExactlyAsync(new byte[1], timeout.Token));
    }

    [Fact]
    public async Task ShutdownSendsGoAwayAndDrainsExistingStreams()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var pair = await ConnectAsync(null, [], timeout.Token);
        var clientStream = await pair.Client.OpenStreamAsync(1, timeout.Token);
        var serverStream = await pair.Server.AcceptStreamAsync(timeout.Token);

        var shutdown = pair.Server.ShutdownAsync(TimeSpan.FromSeconds(5), timeout.Token);
        Assert.Equal(1u, await pair.Client.WaitForGoAwayAsync(timeout.Token));
        await Assert.ThrowsAsync<BftGoAwayException>(async () => await pair.Client.OpenStreamAsync(4, timeout.Token));

        await clientStream.CompleteWritesAsync();
        await serverStream.CompleteWritesAsync();
        await shutdown;
    }

    private static async Task<TunnelPair> ConnectAsync(string? token, IReadOnlyCollection<string> accessTokens, CancellationToken cancellationToken)
    {
        var transport = CreateTransportPair();
        var serverTask = BftServerTunnel.TryAcceptAsync(transport.Server, ServerOptions(accessTokens), cancellationToken).AsTask();
        var clientTask = BftClientTunnel.ConnectAsync(transport.Client, ClientOptions(token), cancellationToken).AsTask();
        await Task.WhenAll(serverTask, clientTask);
        var accepted = await serverTask;
        Assert.True(accepted.Accepted, accepted.HandshakeRejection?.ToString());
        return new TunnelPair(await clientTask, accepted.Tunnel!, transport.Client, transport.Server);
    }

    private static (TestDuplexStream Client, TestDuplexStream Server) CreateTransportPair() => TestDuplexStream.CreatePair();

    private static BftClientTunnelOptions ClientOptions(string? token) => new()
    {
        Token = token,
        Handshake = new BftClientHandshakeOptions
        {
            StaticPublicKey = BftCrypto.GetX25519PublicKey(ExitPrivateKey),
            ObfsKey = ObfsKey,
            EphemeralPrivateKeyFactory = () => ClientEphemeral.ToArray()
        }
    };

    private static BftServerTunnelOptions ServerOptions(IReadOnlyCollection<string> accessTokens) => new()
    {
        Handshake = new BftServerHandshakeOptions
        {
            StaticPrivateKey = ExitPrivateKey,
            ObfsKey = ObfsKey,
            EphemeralPrivateKeyFactory = () => ServerEphemeral.ToArray()
        },
        ProxyId = "lv-1",
        ServiceIds = [1, 4, 5],
        AccessTokens = accessTokens
    };

    private static byte[] HashRepeated(byte[] value, int times)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var i = 0; i < times; i++) hash.AppendData(value);
        return hash.GetHashAndReset();
    }

    private sealed class TunnelPair(BftClientTunnel client, BftServerTunnel server, TestDuplexStream clientTransport, TestDuplexStream serverTransport) : IAsyncDisposable
    {
        public BftClientTunnel Client { get; } = client;
        public BftServerTunnel Server { get; } = server;

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
            await clientTransport.DisposeAsync();
            await serverTransport.DisposeAsync();
        }
    }
}
