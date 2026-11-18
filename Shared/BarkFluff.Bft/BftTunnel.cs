namespace BarkFluff.Bft;

public sealed class BftClientTunnelOptions
{
    public required BftClientHandshakeOptions Handshake { get; init; }
    public string? Token { get; init; }
}

public sealed class BftServerTunnelOptions
{
    public required BftServerHandshakeOptions Handshake { get; init; }
    public required string ProxyId { get; init; }
    public required IReadOnlyList<byte> ServiceIds { get; init; }
    public required byte[] TlsSpkiSha256 { get; init; }
    public IReadOnlyCollection<string> AccessTokens { get; init; } = [];
    public int InitialWindow { get; init; } = 262144;
    public int MaxStreams { get; init; } = 256;
}

public sealed record BftServerTunnelAcceptResult(BftServerTunnel? Tunnel, BftServerHandshakeRejection? HandshakeRejection, bool AuthenticationRejected)
{
    public bool Accepted => Tunnel is not null;
    public bool RejectQuietly => HandshakeRejection is not null;
}

public sealed class BftClientTunnel : IAsyncDisposable
{
    private readonly BftMuxConnection _connection;
    internal BftMuxConnection Connection => _connection;
    internal Task WaitForClosedAsync(CancellationToken cancellationToken) => _connection.WaitForClosedAsync(cancellationToken);

    private BftClientTunnel(BftMuxConnection connection)
    {
        _connection = connection;
        Hello = connection.Hello!;
    }

    public BftHello Hello { get; }
    public uint GoAwayLastStreamId => _connection.GoAwayLastStreamId;
    public bool HasReceivedGoAway => _connection.HasReceivedGoAway;
    public Task Completion => _connection.Completion;
    public Task<uint> WaitForGoAwayAsync(CancellationToken cancellationToken = default) => _connection.WaitForGoAwayAsync(cancellationToken);
    internal ulong OutgoingMessageCount => _connection.OutgoingMessageCount;
    internal ulong IncomingMessageCount => _connection.IncomingMessageCount;

    public static async ValueTask<BftClientTunnel> ConnectAsync(Stream stream, BftClientTunnelOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var session = await BftClientHandshake.ConnectAsync(stream, options.Handshake, cancellationToken).ConfigureAwait(false);
        var mux = new BftMuxConnection(session, isServer: false, proxyId: string.Empty, serviceIds: [],
            initialWindow: 262144, maxStreams: ushort.MaxValue, accessTokens: null,
            timeProvider: options.Handshake.TimeProvider, random: options.Handshake.RandomSource);
        try
        {
            await mux.StartClientAsync(options.Token, cancellationToken).ConfigureAwait(false);
            return new BftClientTunnel(mux);
        }
        catch
        {
            await mux.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask<BftTunnelStream> OpenStreamAsync(byte serviceId, CancellationToken cancellationToken = default) =>
        _connection.OpenStreamAsync(serviceId, cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

public sealed class BftServerTunnel : IAsyncDisposable
{
    private readonly BftMuxConnection _connection;
    internal BftMuxConnection Connection => _connection;

    private BftServerTunnel(BftMuxConnection connection, string matchedObfsKeyLabel)
    {
        _connection = connection;
        MatchedObfsKeyLabel = matchedObfsKeyLabel;
    }

    public string MatchedObfsKeyLabel { get; }
    public uint LastOpenedStreamId => _connection.MaxOpenedStreamId;
    public Task Completion => _connection.Completion;

    public static async ValueTask<BftServerTunnelAcceptResult> TryAcceptAsync(Stream stream, BftServerTunnelOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateOptions(options);
        var handshake = await BftServerHandshake.TryAcceptAsync(stream, options.Handshake, cancellationToken).ConfigureAwait(false);
        if (!handshake.Accepted)
            return new BftServerTunnelAcceptResult(null, handshake.Rejection, false);

        var mux = new BftMuxConnection(handshake.Session!, isServer: true, options.ProxyId, options.ServiceIds,
            options.InitialWindow, options.MaxStreams, options.AccessTokens,
            timeProvider: options.Handshake.TimeProvider, random: options.Handshake.RandomSource,
            tlsSpkiSha256: options.TlsSpkiSha256);
        try
        {
            await mux.StartServerAsync(cancellationToken).ConfigureAwait(false);
            return new BftServerTunnelAcceptResult(new BftServerTunnel(mux, handshake.MatchedObfsKeyLabel!), null, false);
        }
        catch (BftAuthenticationException)
        {
            await mux.DisposeAsync().ConfigureAwait(false);
            return new BftServerTunnelAcceptResult(null, null, true);
        }
        catch
        {
            await mux.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<BftTunnelStream> AcceptStreamAsync(CancellationToken cancellationToken = default) => _connection.AcceptStreamAsync(cancellationToken);

    public Task SendGoAwayAsync(BftGoAwayCode code, CancellationToken cancellationToken = default) => _connection.SendGoAwayAsync(code, cancellationToken);
    public Task WaitForStreamsClosedAsync(CancellationToken cancellationToken = default) => _connection.WaitForStreamsClosedAsync(cancellationToken);
    public Task ShutdownAsync(TimeSpan drainTimeout, CancellationToken cancellationToken = default) => _connection.ShutdownAsync(drainTimeout, cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static void ValidateOptions(BftServerTunnelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.InitialWindow is < 16384 or > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(options.InitialWindow));
        if (options.MaxStreams is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(options.MaxStreams));
        _ = new BftHello(options.ProxyId, options.InitialWindow, (ushort)options.MaxStreams, options.TlsSpkiSha256, options.ServiceIds).ToBody();
        if (options.AccessTokens.Any(token => string.IsNullOrEmpty(token) || System.Text.Encoding.UTF8.GetByteCount(token) > 256))
            throw new ArgumentException("Access tokens must be 1–256 UTF-8 bytes.", nameof(options));
    }
}
