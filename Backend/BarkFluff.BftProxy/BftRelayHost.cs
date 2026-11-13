using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using BarkFluff.Bft;

namespace BarkFluff.BftProxy;

internal sealed class BftRelayHost : IAsyncDisposable
{
    private static readonly TimeSpan ClientReadTimeout = TimeSpan.FromSeconds(10);
    private readonly RelayConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly IBftRandomSource _random;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly TcpListener _listener;
    private readonly PendingConnectionLimiter _limiter;
    private readonly ConcurrentDictionary<long, (TcpClient Client, Task Task)> _connections = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _acceptTask;
    private long _connectionSequence;
    private int _lastInitialWriteLength;
    private int _disposed;

    private BftRelayHost(RelayConfiguration configuration, TimeProvider timeProvider, IBftRandomSource random,
        ILoggerFactory loggerFactory, TcpListener listener)
    {
        _configuration = configuration;
        _timeProvider = timeProvider;
        _random = random;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<BftRelayHost>();
        _listener = listener;
        _limiter = new PendingConnectionLimiter(configuration.MaxConnectionsPerIp, configuration.MaxConnections);
    }

    public IPEndPoint ListenEndPoint => (IPEndPoint)_listener.LocalEndpoint;
    internal int LastInitialWriteLength => Volatile.Read(ref _lastInitialWriteLength);
    internal Task WaitForActiveConnectionsAsync() => Task.WhenAll(_connections.Values.Select(connection => connection.Task));

    public static Task<BftRelayHost> StartAsync(RelayConfiguration configuration, TimeProvider? timeProvider = null,
        IBftRandomSource? random = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        timeProvider ??= TimeProvider.System;
        random ??= BftSystemRandomSource.Shared;
        var listen = (IPEndPoint)configuration.Listen.ToListenEndPoint();
        var listener = new TcpListener(listen.Address, listen.Port);
        listener.Start(512);
        var loggerFactory = LoggerFactory.Create(logging => logging.AddJsonConsole());
        var host = new BftRelayHost(configuration, timeProvider, random, loggerFactory, listener);
        host._acceptTask = host.AcceptLoopAsync(cancellationToken);
        return Task.FromResult(host);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        while (!linked.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (linked.IsCancellationRequested) { break; }
            catch (SocketException) when (linked.IsCancellationRequested) { break; }
            client.NoDelay = true;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            var id = Interlocked.Increment(ref _connectionSequence);
            var task = HandleConnectionAsync(client, linked.Token);
            _connections[id] = (client, task);
            _ = ObserveConnectionAsync(id, task);
        }
    }

    private async Task ObserveConnectionAsync(long id, Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogError(ex, "BFT relay connection handler failed"); }
        finally { _connections.TryRemove(id, out _); }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            if (client.Client.RemoteEndPoint is not IPEndPoint remote) return;
            var clientIp = Normalize(remote.Address);
            using var lease = _limiter.TryAcquire(clientIp);
            if (lease is null)
            {
                _logger.LogWarning("BFT relay connection rejected from {ClientIp}: connection limit reached", clientIp);
                return;
            }

            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var closeOnCancel = operation.Token.Register(static state =>
            {
                var sockets = ((TcpClient Client, TcpClient? NextHop))state!;
                sockets.Client.Dispose();
                sockets.NextHop?.Dispose();
            }, (client, (TcpClient?)null));

            var clientStream = client.GetStream();
            var salt = new byte[32];
            using var saltTimeout = new CancellationTokenSource(ClientReadTimeout, _timeProvider);
            using var readSalt = CancellationTokenSource.CreateLinkedTokenSource(operation.Token, saltTimeout.Token);
            try { await clientStream.ReadExactlyAsync(salt, readSalt.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!operation.IsCancellationRequested)
            {
                _logger.LogWarning("BFT relay connection rejected from {ClientIp}: salt read timed out", clientIp);
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                _logger.LogDebug("BFT relay connection from {ClientIp} ended before its salt", clientIp);
                return;
            }

            using var nextHop = new TcpClient { NoDelay = true };
            using var connectTimeout = new CancellationTokenSource(_configuration.ConnectTimeout ?? TimeSpan.FromSeconds(15), _timeProvider);
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(operation.Token, connectTimeout.Token);
            try
            {
                await ConnectAsync(nextHop, _configuration.NextHop, connect.Token).ConfigureAwait(false);
                nextHop.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            catch (OperationCanceledException) when (!operation.IsCancellationRequested)
            {
                _logger.LogWarning("BFT relay connection from {ClientIp} failed: next-hop connection timed out", clientIp);
                return;
            }
            catch (Exception ex) when (ex is SocketException or IOException or ArgumentException)
            {
                _logger.LogWarning("BFT relay connection from {ClientIp} failed: next-hop is unavailable ({Reason})", clientIp, ex.GetType().Name);
                return;
            }

            using var closeBothOnCancel = operation.Token.Register(static state =>
            {
                var sockets = ((TcpClient Client, TcpClient NextHop))state!;
                sockets.Client.Dispose();
                sockets.NextHop.Dispose();
            }, (client, nextHop));

            var initialBytes = ArrayPool<byte>.Shared.Rent(16 * 1024);
            var initialLength = 0;
            try
            {
                using var firstFlightTimeout = new CancellationTokenSource(ClientReadTimeout, _timeProvider);
                using var firstFlight = CancellationTokenSource.CreateLinkedTokenSource(operation.Token, firstFlightTimeout.Token);
                initialLength = await clientStream.ReadAsync(initialBytes.AsMemory(0, initialBytes.Length), firstFlight.Token).ConfigureAwait(false);
                if (initialLength == 0) return;

                var relayNonce = new byte[BftRelayPreamble.RelayNonceLength];
                _random.Fill(relayNonce);
                byte[] preamble;
                try
                {
                    preamble = BftRelayPreamble.Create(_configuration.RelayKey, salt, relayNonce, clientIp, remote.Port,
                        _timeProvider.GetUtcNow().ToUnixTimeSeconds(), _configuration.RelayId);
                }
                finally { CryptographicOperations.ZeroMemory(relayNonce); }

                var initialWrite = new byte[preamble.Length + salt.Length + initialLength];
                preamble.CopyTo(initialWrite, 0);
                salt.CopyTo(initialWrite, preamble.Length);
                initialBytes.AsSpan(0, initialLength).CopyTo(initialWrite.AsSpan(preamble.Length + salt.Length));
                CryptographicOperations.ZeroMemory(preamble);
                Interlocked.Exchange(ref _lastInitialWriteLength, initialWrite.Length);
                await nextHop.GetStream().WriteAsync(initialWrite, operation.Token).ConfigureAwait(false);
                CryptographicOperations.ZeroMemory(initialWrite);
            }
            catch (OperationCanceledException) when (!operation.IsCancellationRequested)
            {
                _logger.LogWarning("BFT relay connection from {ClientIp} ended before its first record", clientIp);
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or CryptographicException or ArgumentException)
            {
                _logger.LogWarning("BFT relay connection from {ClientIp} could not forward its first record ({Reason})", clientIp, ex.GetType().Name);
                return;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(initialBytes);
                ArrayPool<byte>.Shared.Return(initialBytes);
                CryptographicOperations.ZeroMemory(salt);
            }

            var upstream = nextHop.GetStream();
            long fromClient = initialLength;
            long toClient = 0;
            _logger.LogInformation("BFT relay connection accepted from {ClientIp} via relay {RelayId}", clientIp, _configuration.RelayId);
            var clientToExit = PumpAsync(clientStream, nextHop, operation.Token, count => Interlocked.Add(ref fromClient, count));
            var exitToClient = PumpAsync(upstream, client, operation.Token, count => Interlocked.Add(ref toClient, count));
            var first = await Task.WhenAny(clientToExit, exitToClient).ConfigureAwait(false);
            if (first.IsFaulted || first.IsCanceled) operation.Cancel();
            else
            {
                var remaining = ReferenceEquals(first, clientToExit) ? exitToClient : clientToExit;
                using var halfCloseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), _timeProvider);
                var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, halfCloseTimeout.Token);
                if (await Task.WhenAny(remaining, timeoutTask).ConfigureAwait(false) == timeoutTask)
                    operation.Cancel();
            }
            try { await Task.WhenAll(clientToExit, exitToClient).ConfigureAwait(false); }
            catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                _logger.LogDebug("BFT relay connection from {ClientIp} closed after forwarding error ({Reason})", clientIp, ex.GetType().Name);
            }
            _logger.LogInformation("BFT relay connection closed from {ClientIp} via relay {RelayId}, bytes from client {BytesFromClient}, bytes to client {BytesToClient}",
                clientIp, _configuration.RelayId, Interlocked.Read(ref fromClient), Interlocked.Read(ref toClient));
        }
    }

    private static async Task ConnectAsync(TcpClient client, ProxyEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(endpoint.Host, out var address))
            await client.ConnectAsync(address, endpoint.Port, cancellationToken).ConfigureAwait(false);
        else
            await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).ConfigureAwait(false);
    }

    private static async Task PumpAsync(Stream source, TcpClient destination, CancellationToken cancellationToken, Action<int> count)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            var destinationSocket = destination.Client;
            var target = destination.GetStream();
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    try { destinationSocket.Shutdown(SocketShutdown.Send); }
                    catch (SocketException) { }
                    catch (ObjectDisposedException) { }
                    return;
                }
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                count(read);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        _listener.Stop();
        foreach (var connection in _connections.Values) connection.Client.Dispose();
        if (_acceptTask is not null)
        {
            try { await _acceptTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        try { await Task.WhenAll(_connections.Values.Select(connection => connection.Task)).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _loggerFactory.Dispose();
        _shutdown.Dispose();
    }
}
