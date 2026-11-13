using BarkFluff.Bft;
using Xunit;

namespace BarkFluff.Bft.Tests;

public sealed class BftMuxFailureTests
{
    private static readonly byte[] ExitPrivateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] ClientEphemeral = Enumerable.Range(0x61, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] ServerEphemeral = Enumerable.Range(0x81, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] ObfsKey = Enumerable.Range(0x20, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] TlsPin = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public async Task CorruptRecordAbortsWithoutWritingGoAway()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (clientTransport, serverTransport) = TestDuplexStream.CreatePair();
        var serverTask = BftServerTunnel.TryAcceptAsync(serverTransport, ServerOptions(), timeout.Token).AsTask();
        var clientTask = BftClientTunnel.ConnectAsync(clientTransport, ClientOptions(), timeout.Token).AsTask();
        await Task.WhenAll(serverTask, clientTask);
        var accepted = await serverTask;
        Assert.True(accepted.Accepted, accepted.HandshakeRejection?.ToString());
        await using var server = accepted.Tunnel!;
        await using var client = await clientTask;
        var serverBytesBeforeCorruption = serverTransport.TotalBytesWritten;

        clientTransport.CorruptNextOutputAt(0);
        _ = await client.OpenStreamAsync(1, timeout.Token);
        await server.Completion.WaitAsync(timeout.Token);

        Assert.Equal(serverBytesBeforeCorruption, serverTransport.TotalBytesWritten);
        await client.DisposeAsync();
        await clientTransport.DisposeAsync();
        await serverTransport.DisposeAsync();
    }

    [Fact]
    public async Task FrameErrorSendsProtocolGoAway()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var pair = await StartServerMuxAsync(TimeProvider.System, timeout.Token);
        var malformedFrame = new byte[] { 0x0B, 0, 0, 0, 0, 0, 0 };
        await pair.Client.WriteMessageAsync(malformedFrame, cancellationToken: timeout.Token);

        var reply = await pair.Client.ReadMessageAsync(timeout.Token);
        Assert.NotNull(reply);
        var goAway = BftFrameCodec.DecodeSingle(reply);
        Assert.Equal(BftFrameType.GoAway, goAway.Type);
        Assert.Equal(BftGoAwayCode.ProtocolError, (BftGoAwayCode)goAway.Body[4]);
        await pair.Server.Completion.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task PingHandlingDoesNotWaitForBlockedTunnelWriter()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pair = await StartServerMuxAsync(TimeProvider.System, timeout.Token);
        await pair.Client.WriteMessageAsync(BftFrameCodec.Encode(BftFrame.Open(1, 1)), cancellationToken: timeout.Token);
        var stream = await pair.Server.AcceptStreamAsync(timeout.Token);
        pair.ServerTransport.BlockOutputWrites();
        await stream.WriteAsync(new byte[] { 0x42 }, timeout.Token);
        await pair.ServerTransport.WaitForBlockedOutputAsync(timeout.Token);
        Assert.True(pair.Server.OutgoingMessageCount >= 2);

        var action = await pair.Server.ProcessFrameAsync(new BftFrame(BftFrameType.Ping, 0, new byte[8]), timeout.Token).WaitAsync(timeout.Token);
        Assert.Equal(BftMuxFrameAction.Processed, action);
    }

    [Fact]
    public async Task ProtocolGoAwayWriteStopsAfterFiveSecondProviderDeadline()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var pair = await StartServerMuxAsync(clock, timeout.Token);
        var serverBytesBeforeError = pair.ServerTransport.TotalBytesWritten;
        pair.ServerTransport.BlockOutputWrites();
        await pair.Client.WriteMessageAsync(new byte[] { 0x0B, 0, 0, 0, 0, 0, 0 }, cancellationToken: timeout.Token);

        await clock.WaitForActiveTimerAsync(TimeSpan.FromSeconds(5), timeout.Token);
        clock.Advance(TimeSpan.FromSeconds(5));
        await pair.Server.Completion.WaitAsync(timeout.Token);
        Assert.Equal(serverBytesBeforeError, pair.ServerTransport.TotalBytesWritten);
    }

    [Fact]
    public void LowOrderDhFailureHasTypedNoiseReason()
    {
        var privateKey = ExitPrivateKey;
        Assert.Throws<BftInvalidNoiseException>(() => BftCrypto.X25519(privateKey, new byte[32]));
    }

    [Fact]
    public void TokenCheckUsesFixedTimeComparisonForEveryCandidate()
    {
        var comparisons = 0;
        IEnumerable<byte[]> Candidates()
        {
            foreach (var value in new[] { "valid"u8.ToArray(), "other"u8.ToArray(), "last"u8.ToArray() })
            {
                comparisons++;
                yield return value;
            }
        }

        Assert.True(BftMuxConnection.FixedTimeEqualsAny("valid"u8, Candidates()));
        Assert.Equal(3, comparisons);
        Assert.False(BftMuxConnection.FixedTimeEqualsAny("wrong"u8, Candidates()));
        Assert.Equal(6, comparisons);
    }

    private static async Task<DirectPair> StartServerMuxAsync(TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var (clientTransport, serverTransport) = TestDuplexStream.CreatePair();
        var serverHandshake = BftServerHandshake.TryAcceptAsync(serverTransport, new BftServerHandshakeOptions
        {
            StaticPrivateKey = ExitPrivateKey,
            ObfsKeys = [new("failure-user", ObfsKey)],
            TimeProvider = timeProvider,
            EphemeralPrivateKeyFactory = () => ServerEphemeral.ToArray()
        }, cancellationToken).AsTask();
        var clientHandshake = BftClientHandshake.ConnectAsync(clientTransport, new BftClientHandshakeOptions
        {
            StaticPublicKey = BftCrypto.GetX25519PublicKey(ExitPrivateKey),
            ObfsKey = ObfsKey,
            EphemeralPrivateKeyFactory = () => ClientEphemeral.ToArray()
        }, cancellationToken).AsTask();
        await Task.WhenAll(serverHandshake, clientHandshake);
        var accepted = await serverHandshake;
        Assert.True(accepted.Accepted, accepted.Rejection?.ToString());
        var client = await clientHandshake;
        var server = new BftMuxConnection(accepted.Session!, true, "lv-1", [1, 4, 5], 262144, 256, [], timeProvider,
            BftSystemRandomSource.Shared, TlsPin);
        await server.StartServerAsync(cancellationToken);
        var hello = await client.ReadMessageAsync(cancellationToken);
        Assert.NotNull(hello);
        Assert.Equal(BftFrameType.Hello, BftFrameCodec.DecodeSingle(hello).Type);
        return new DirectPair(client, server, clientTransport, serverTransport);
    }

    private static BftServerTunnelOptions ServerOptions() => new()
    {
        Handshake = new BftServerHandshakeOptions
        {
            StaticPrivateKey = ExitPrivateKey,
            ObfsKeys = [new("failure-user", ObfsKey)],
            EphemeralPrivateKeyFactory = () => ServerEphemeral.ToArray()
        },
        ProxyId = "lv-1",
        ServiceIds = [1, 4, 5],
        TlsSpkiSha256 = TlsPin
    };

    private static BftClientTunnelOptions ClientOptions() => new()
    {
        Handshake = new BftClientHandshakeOptions
        {
            StaticPublicKey = BftCrypto.GetX25519PublicKey(ExitPrivateKey),
            ObfsKey = ObfsKey,
            EphemeralPrivateKeyFactory = () => ClientEphemeral.ToArray()
        }
    };

    private sealed class DirectPair : IAsyncDisposable
    {
        private readonly TestDuplexStream _clientTransport;
        private readonly TestDuplexStream _serverTransport;

        public DirectPair(BftNoiseSession client, BftMuxConnection server, TestDuplexStream clientTransport,
            TestDuplexStream serverTransport)
        {
            Client = client;
            Server = server;
            _clientTransport = clientTransport;
            _serverTransport = serverTransport;
        }

        public BftNoiseSession Client { get; }
        public BftMuxConnection Server { get; }
        public TestDuplexStream ServerTransport => _serverTransport;

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            await Client.DisposeAsync();
            await _clientTransport.DisposeAsync();
            await _serverTransport.DisposeAsync();
        }
    }
}
