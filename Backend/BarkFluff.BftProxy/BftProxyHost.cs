using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using BarkFluff.Bft;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BarkFluff.BftProxy;

internal sealed class BftProxyHost : IAsyncDisposable
{
    private readonly ExitConfiguration _configuration;
    private readonly BftConnectionTransport _transport;
    private readonly WebApplication _app;
    private readonly BftForwarder _forwarder;
    private readonly X509Certificate2 _certificate;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IBftRandomSource _random;
    private readonly byte[] _tlsSpkiSha256;
    private readonly BftSaltReplayCache _saltCache;
    private readonly PendingConnectionLimiter _pending = new(16, 1024);
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private readonly ConcurrentDictionary<string, ActiveTunnel> _tunnels = new(StringComparer.Ordinal);
    private readonly List<TcpListener> _listeners = [];
    private readonly CancellationTokenSource _shutdown = new();
    private IReadOnlyList<BftServerObfsKey> _clientKeys;
    private byte[] _observedKeyFileHash;
    private Task[] _acceptTasks = [];
    private Task? _reloadTask;
    private long _connectionSequence;
    private int _disposed;

    private BftProxyHost(ExitConfiguration configuration, BftConnectionTransport transport, WebApplication app,
        X509Certificate2 certificate, TimeProvider timeProvider, IBftRandomSource random, byte[] tlsSpkiSha256,
        BftSaltReplayCache saltCache, BftForwarder forwarder)
    {
        _configuration = configuration;
        _transport = transport;
        _app = app;
        _certificate = certificate;
        _timeProvider = timeProvider;
        _random = random;
        _tlsSpkiSha256 = tlsSpkiSha256;
        _saltCache = saltCache;
        _forwarder = forwarder;
        _clientKeys = configuration.ClientKeys;
        _observedKeyFileHash = HashKeyFile(configuration.ClientKeysFile);
        _logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<BftProxyHost>();
    }

    public IPEndPoint ListenEndPoint => (IPEndPoint)_listeners[0].LocalEndpoint;
    public IPEndPoint? RelayListenEndPoint => _listeners.Count > 1 ? (IPEndPoint)_listeners[1].LocalEndpoint : null;
    public byte[] TlsSpkiSha256 => _tlsSpkiSha256.ToArray();

    public static async Task<BftProxyHost> StartExitAsync(ExitConfiguration configuration, TimeProvider? timeProvider = null,
        IBftRandomSource? random = null, RemoteCertificateValidationCallback? testCertificateValidation = null,
        CancellationToken cancellationToken = default)
    {
        timeProvider ??= TimeProvider.System;
        random ??= BftSystemRandomSource.Shared;
        var certificate = CreateTlsCertificate();
        var tlsPin = SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo());
        var transport = new BftConnectionTransport();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();
        builder.Services.AddHttpForwarder();
        builder.Services.RemoveAll<IConnectionListenerFactory>();
        builder.Services.AddSingleton<IConnectionListenerFactory>(transport);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = null;
            options.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
            options.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(20);
            options.Listen(new IPEndPoint(IPAddress.Loopback, 0), listen =>
            {
                listen.Protocols = HttpProtocols.Http1AndHttp2;
                listen.UseHttps(certificate, ConfigureTls);
            });
        });
        var app = builder.Build();
        var proxy = new BftForwarder(app.Services, configuration, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BftForwarder"), testCertificateValidation);
        app.Run(proxy.ForwardAsync);
        var host = new BftProxyHost(configuration, transport, app, certificate, timeProvider, random, tlsPin,
            new BftSaltReplayCache(timeProvider: timeProvider), proxy);
        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            host.StartListeners();
            host._reloadTask = host.ReloadClientKeysAsync(host._shutdown.Token);
            return host;
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void ConfigureTls(HttpsConnectionAdapterOptions options)
    {
        options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
    }

    private void StartListeners()
    {
        var direct = StartListener(_configuration.Listen);
        _listeners.Add(direct);
        if (_configuration.RelayListen is { } relayEndpoint) _listeners.Add(StartListener(relayEndpoint));
        _acceptTasks = _listeners.Select((listener, index) => AcceptLoopAsync(listener, index == 1, _shutdown.Token)).ToArray();
    }

    private static TcpListener StartListener(ProxyEndpoint endpoint)
    {
        var address = (IPEndPoint)endpoint.ToListenEndPoint();
        var listener = new TcpListener(address.Address, address.Port);
        listener.Start(512);
        return listener;
    }

    private async Task AcceptLoopAsync(TcpListener listener, bool relayPort, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
            client.NoDelay = true;
            var id = Interlocked.Increment(ref _connectionSequence);
            var task = HandleConnectionAsync(client, relayPort, cancellationToken);
            _connections[id] = task;
            _ = ObserveConnectionAsync(id, task);
        }
    }

    private async Task ObserveConnectionAsync(long id, Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogError(ex, "BFT connection handler failed"); }
        finally { _connections.TryRemove(id, out _); }
    }

    private async Task HandleConnectionAsync(TcpClient client, bool relayPort, CancellationToken cancellationToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            if (client.Client.RemoteEndPoint is not IPEndPoint remote) return;
            var peerIp = Normalize(remote.Address);
            if (relayPort && !_configuration.TrustedRelays.Contains(peerIp))
            {
                _logger.LogWarning("BFT relay connection rejected from {ClientIp}: relay address is not trusted", peerIp);
                return;
            }
            var pending = relayPort ? _pending.TryAcquireUnattributed() : _pending.TryAcquire(peerIp);
            if (pending is null)
            {
                _logger.LogWarning("BFT connection rejected from {ClientIp}: pending connection limit reached", peerIp);
                return;
            }

            using (pending)
            {
                IPAddress clientIp = peerIp;
                EndPoint clientEndPoint = remote;
                Stream handshakeStream = stream;
                if (relayPort)
                {
                    var firstBytes = new byte[BftRelayPreamble.Length + 32];
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), _timeProvider);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                        await stream.ReadExactlyAsync(firstBytes, linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("BFT relay connection rejected from {ClientIp}: preamble read timeout", peerIp);
                        await QuietCloseAsync(stream, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex) when (ex is IOException or SocketException)
                    {
                        _logger.LogWarning("BFT relay connection rejected from {ClientIp}: incomplete preamble", peerIp);
                        await QuietCloseAsync(stream, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    BftRelayPreambleInfo preamble;
                    try
                    {
                        preamble = BftRelayPreamble.Verify(_configuration.RelayKey!, firstBytes.AsSpan(BftRelayPreamble.Length, 32),
                            firstBytes.AsSpan(0, BftRelayPreamble.Length), _timeProvider.GetUtcNow().ToUnixTimeSeconds());
                    }
                    catch (Exception ex) when (ex is BftProtocolException or CryptographicException or ArgumentException)
                    {
                        _logger.LogWarning("BFT relay connection rejected from {ClientIp}: invalid preamble", peerIp);
                        await QuietCloseAsync(stream, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    clientIp = Normalize(preamble.ClientIp);
                    if (!pending.TryChangeAddress(clientIp))
                    {
                        _logger.LogWarning("BFT relay connection rejected from {ClientIp}: pending connection limit reached", clientIp);
                        return;
                    }
                    clientEndPoint = new IPEndPoint(clientIp, preamble.ClientPort);
                    handshakeStream = new PrefixStream(stream, firstBytes.AsMemory(BftRelayPreamble.Length, 32));
                }

                BftServerTunnelAcceptResult result;
                try
                {
                    var configuration = _configuration;
                    var handshakeOptions = new BftServerHandshakeOptions
                    {
                        StaticPrivateKey = configuration.StaticPrivateKey,
                        ObfsKeys = Volatile.Read(ref _clientKeys),
                        TimeProvider = _timeProvider,
                        RandomSource = _random,
                        SaltCache = _saltCache
                    };
                    var options = new BftServerTunnelOptions
                    {
                        Handshake = handshakeOptions,
                        ProxyId = configuration.ProxyId,
                        ServiceIds = configuration.Upstreams.Keys.Order().ToArray(),
                        TlsSpkiSha256 = _tlsSpkiSha256,
                        AccessTokens = configuration.AccessTokens
                    };
                    result = await BftServerTunnel.TryAcceptAsync(handshakeStream, options, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (Exception ex) when (ex is IOException or CryptographicException or SocketException or ArgumentException)
                {
                    _logger.LogWarning("BFT connection rejected from {ClientIp}: handshake failed", clientIp);
                    await QuietCloseAsync(handshakeStream, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!result.Accepted)
                {
                    if (result.AuthenticationRejected)
                    {
                        _logger.LogWarning("BFT tunnel authentication rejected from {ClientIp}", clientIp);
                        await QuietCloseAsync(handshakeStream, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    _logger.LogWarning("BFT connection rejected from {ClientIp}: {Reason}", clientIp, result.HandshakeRejection);
                    await QuietCloseAsync(handshakeStream, cancellationToken).ConfigureAwait(false);
                    return;
                }

                pending.Dispose();
                var tunnel = result.Tunnel!;
                var tunnelId = Guid.NewGuid().ToString("N");
                var active = new ActiveTunnel(tunnelId, clientIp, tunnel.MatchedObfsKeyLabel, tunnel);
                _tunnels[tunnelId] = active;
                var counters = new TunnelCounters();
                _logger.LogInformation("BFT tunnel accepted from {ClientIp} with key {KeyLabel}, tunnel {TunnelId}", clientIp, tunnel.MatchedObfsKeyLabel, tunnelId);
                try
                {
                    while (!cancellationToken.IsCancellationRequested && !active.Revoked.IsCancellationRequested)
                    {
                        var bftStream = await tunnel.AcceptStreamAsync(active.Revoked.Token).ConfigureAwait(false);
                        var feature = new BftProxyConnectionFeature(bftStream.ServiceId, clientIp, tunnel.MatchedObfsKeyLabel,
                            tunnelId, bftStream.StreamId, counters, bftStream);
                        var connection = new BftStreamConnection(bftStream, feature, clientEndPoint);
                        if (!_transport.TryQueue(connection))
                        {
                            await bftStream.ResetAsync(BftResetCode.Internal).ConfigureAwait(false);
                            await bftStream.DisposeAsync().ConfigureAwait(false);
                            _logger.LogWarning("BFT stream rejected from {ClientIp}: Kestrel transport queue is full, tunnel {TunnelId}, stream {StreamId}",
                                clientIp, tunnelId, bftStream.StreamId);
                        }
                    }
                }
                catch (OperationCanceledException) when (active.Revoked.IsCancellationRequested || cancellationToken.IsCancellationRequested) { }
                catch (Exception ex) when (ex is IOException or ChannelClosedException or ObjectDisposedException)
                {
                    _logger.LogDebug("BFT tunnel {TunnelId} closed: {Reason}", tunnelId, ex.GetType().Name);
                }
                finally
                {
                    _tunnels.TryRemove(tunnelId, out _);
                    await tunnel.DisposeAsync().ConfigureAwait(false);
                    active.Dispose();
                    _logger.LogInformation("BFT tunnel closed from {ClientIp}, key {KeyLabel}, tunnel {TunnelId}, bytes from client {BytesFromClient}, bytes to client {BytesToClient}",
                        clientIp, tunnel.MatchedObfsKeyLabel, tunnelId, counters.BytesFromClient, counters.BytesToClient);
                }
            }
        }
    }

    private async Task QuietCloseAsync(Stream stream, CancellationToken cancellationToken)
    {
        Span<byte> randomByte = stackalloc byte[1];
        _random.Fill(randomByte);
        var delay = TimeSpan.FromSeconds(1 + randomByte[0] % 10);
        using var timeout = new CancellationTokenSource(delay, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var discard = new byte[4096];
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(discard, linked.Token).ConfigureAwait(false);
                if (count == 0) return;
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (IOException) { }
        catch (SocketException) { }
    }

    private async Task ReloadClientKeysAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), _timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            byte[] content;
            try { content = await File.ReadAllBytesAsync(_configuration.ClientKeysFile, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (IOException ex)
            {
                _logger.LogError(ex, "BFT client key file could not be read; retaining prior key set");
                continue;
            }
            var hash = SHA256.HashData(content);
            if (CryptographicOperations.FixedTimeEquals(hash, _observedKeyFileHash))
            {
                CryptographicOperations.ZeroMemory(content);
                CryptographicOperations.ZeroMemory(hash);
                continue;
            }

            IReadOnlyList<BftServerObfsKey> next;
            try { next = ClientKeyFile.Parse(content); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Text.DecoderFallbackException or FormatException)
            {
                _logger.LogError(ex, "BFT client key file is invalid; retaining prior key set");
                CryptographicOperations.ZeroMemory(content);
                CryptographicOperations.ZeroMemory(_observedKeyFileHash);
                _observedKeyFileHash = hash;
                continue;
            }
            CryptographicOperations.ZeroMemory(content);
            var prior = Volatile.Read(ref _clientKeys);
            Volatile.Write(ref _clientKeys, next);
            CryptographicOperations.ZeroMemory(_observedKeyFileHash);
            _observedKeyFileHash = hash;
            var revoked = prior.Select(key => key.Label).Except(next.Select(key => key.Label), StringComparer.Ordinal).ToArray();
            foreach (var label in revoked)
            {
                _logger.LogInformation("BFT client key revoked: {KeyLabel}", label);
                foreach (var active in _tunnels.Values.Where(tunnel => tunnel.KeyLabel == label))
                    _ = RevokeTunnelAsync(active, cancellationToken);
            }
        }
    }

    private async Task RevokeTunnelAsync(ActiveTunnel active, CancellationToken cancellationToken)
    {
        try { await active.Tunnel.ShutdownAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogDebug("Revoked BFT tunnel {TunnelId} could not drain: {Reason}", active.Id, ex.GetType().Name);
        }
        finally { active.Revoked.Cancel(); }
    }

    private static byte[] HashKeyFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return SHA256.HashData(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static X509Certificate2 CreateTlsCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=bft.internal", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("bft.internal");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        var eku = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, false));
        var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        if (!OperatingSystem.IsWindows()) return generated;
        var pfx = generated.Export(X509ContentType.Pfx);
        generated.Dispose();
        try { return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        foreach (var listener in _listeners) listener.Stop();
        try { await Task.WhenAll(_acceptTasks).ConfigureAwait(false); } catch (OperationCanceledException) { }
        var active = _tunnels.Values.ToArray();
        foreach (var tunnel in active) tunnel.Revoked.Cancel();
        try { await Task.WhenAll(_connections.Values).ConfigureAwait(false); } catch { }
        if (_reloadTask is not null) try { await _reloadTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        _forwarder.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
        _certificate.Dispose();
        _shutdown.Dispose();
    }

    private sealed class ActiveTunnel(string id, IPAddress clientIp, string keyLabel, BftServerTunnel tunnel) : IDisposable
    {
        public string Id { get; } = id;
        public IPAddress ClientIp { get; } = clientIp;
        public string KeyLabel { get; } = keyLabel;
        public BftServerTunnel Tunnel { get; } = tunnel;
        public CancellationTokenSource Revoked { get; } = new();
        public void Dispose() => Revoked.Dispose();
    }
}

internal sealed class PendingConnectionLimiter(int perIpLimit, int totalLimit)
{
    private readonly object _gate = new();
    private readonly Dictionary<IPAddress, int> _byIp = [];
    private int _total;

    public Lease? TryAcquire(IPAddress ip)
    {
        lock (_gate)
        {
            var count = _byIp.GetValueOrDefault(ip);
            if (count >= perIpLimit || _total >= totalLimit) return null;
            _byIp[ip] = count + 1;
            _total++;
            return new Lease(this, ip);
        }
    }

    public Lease? TryAcquireUnattributed()
    {
        lock (_gate)
        {
            if (_total >= totalLimit) return null;
            _total++;
            return new Lease(this, null);
        }
    }

    private bool TryChange(Lease lease, IPAddress next)
    {
        lock (_gate)
        {
            if (lease.Address?.Equals(next) == true) return true;
            var count = _byIp.GetValueOrDefault(next);
            if (count >= perIpLimit) return false;
            if (lease.Address is { } current) Decrement(current);
            _byIp[next] = count + 1;
            lease.Address = next;
            return true;
        }
    }

    private void Release(Lease lease)
    {
        lock (_gate)
        {
            if (lease.Released) return;
            lease.Released = true;
            if (lease.Address is { } address) Decrement(address);
            _total--;
        }
    }

    private void Decrement(IPAddress address)
    {
        var count = _byIp[address] - 1;
        if (count == 0) _byIp.Remove(address);
        else _byIp[address] = count;
    }

    internal sealed class Lease(PendingConnectionLimiter owner, IPAddress? address) : IDisposable
    {
        internal IPAddress? Address { get; set; } = address;
        internal bool Released { get; set; }
        public bool TryChangeAddress(IPAddress next) => owner.TryChange(this, next);
        public void Dispose() => owner.Release(this);
    }
}

internal sealed class PrefixStream(Stream inner, ReadOnlyMemory<byte> prefix) : Stream
{
    private int _position;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < prefix.Length)
        {
            var count = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.Span.Slice(_position, count).CopyTo(buffer.Span);
            _position += count;
            return count;
        }
        return await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    public override ValueTask DisposeAsync() => inner.DisposeAsync();
}
