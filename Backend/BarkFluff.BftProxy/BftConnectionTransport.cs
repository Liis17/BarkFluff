using System.IO.Pipelines;
using System.Net;
using System.Threading.Channels;
using BarkFluff.Bft;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace BarkFluff.BftProxy;

public sealed class BftProxyConnectionFeature
{
    internal BftProxyConnectionFeature(byte serviceId, IPAddress clientIp, string keyLabel, string tunnelId, uint streamId,
        TunnelCounters counters, BftTunnelStream stream)
    {
        ServiceId = serviceId;
        ClientIp = clientIp.IsIPv4MappedToIPv6 ? clientIp.MapToIPv4() : clientIp;
        KeyLabel = keyLabel;
        TunnelId = tunnelId;
        StreamId = streamId;
        Counters = counters;
        Stream = stream;
    }

    public byte ServiceId { get; }
    public IPAddress ClientIp { get; }
    public string KeyLabel { get; }
    public string TunnelId { get; }
    public uint StreamId { get; }
    internal TunnelCounters Counters { get; }
    internal BftTunnelStream Stream { get; }
    internal ValueTask ResetUpstreamFailureAsync() => Stream.ResetAsync(BftResetCode.UpstreamFailed);
}

internal sealed class TunnelCounters
{
    private long _bytesFromClient;
    private long _bytesToClient;
    public long BytesFromClient => Interlocked.Read(ref _bytesFromClient);
    public long BytesToClient => Interlocked.Read(ref _bytesToClient);
    public void AddFromClient(int count) => Interlocked.Add(ref _bytesFromClient, count);
    public void AddToClient(int count) => Interlocked.Add(ref _bytesToClient, count);
}

internal sealed record BftStreamConnection(BftTunnelStream Stream, BftProxyConnectionFeature Feature, EndPoint RemoteEndPoint);

internal sealed class BftConnectionTransport : IConnectionListenerFactory, IAsyncDisposable
{
    private readonly Channel<BftStreamConnection> _connections = Channel.CreateBounded<BftStreamConnection>(new BoundedChannelOptions(4096)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });
    private int _bound;

    public ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _bound, 1) != 0)
            throw new InvalidOperationException("The BFT Kestrel transport can bind only one listener.");
        return ValueTask.FromResult<IConnectionListener>(new Listener(this, endpoint));
    }

    public bool TryQueue(BftStreamConnection connection) => _connections.Writer.TryWrite(connection);

    public async ValueTask DisposeAsync()
    {
        _connections.Writer.TryComplete();
        while (_connections.Reader.TryRead(out var connection))
            await connection.Stream.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class Listener(BftConnectionTransport owner, EndPoint endPoint) : IConnectionListener
    {
        public EndPoint EndPoint { get; } = endPoint;

        public async ValueTask<ConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var connection = await owner._connections.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                return new BftConnectionContext(connection);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
        {
            owner._connections.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => UnbindAsync();
    }

    private sealed class BftConnectionContext : DefaultConnectionContext
    {
        private readonly BftStreamConnection _connection;
        private readonly PipeReader _reader;
        private readonly PipeWriter _writer;
        private readonly CancellationTokenSource _closed = new();
        private int _disposed;

        public BftConnectionContext(BftStreamConnection connection) : base($"bft-{connection.Feature.TunnelId}-{connection.Feature.StreamId}")
        {
            _connection = connection;
            _reader = PipeReader.Create(new CountingStream(connection.Stream, connection.Feature.Counters, countReads: true), new StreamPipeReaderOptions(leaveOpen: true));
            _writer = PipeWriter.Create(new CountingStream(connection.Stream, connection.Feature.Counters, countReads: false), new StreamPipeWriterOptions(leaveOpen: true));
            Transport = new DuplexPipe(_reader, _writer);
            ConnectionClosed = _closed.Token;
            RemoteEndPoint = connection.RemoteEndPoint;
            Features.Set(connection.Feature);
        }

        public override void Abort(ConnectionAbortedException abortReason)
        {
            try { _closed.Cancel(); } catch (ObjectDisposedException) { }
            _ = _connection.Stream.DisposeAsync();
            base.Abort(abortReason);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _closed.Cancel(); } catch (ObjectDisposedException) { }
            await _reader.CompleteAsync().ConfigureAwait(false);
            await _writer.CompleteAsync().ConfigureAwait(false);
            await _connection.Stream.DisposeAsync().ConfigureAwait(false);
            _closed.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class CountingStream(Stream inner, TunnelCounters counters, bool countReads) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            CountAsync(inner.ReadAsync(buffer, cancellationToken), countReads);
        private async ValueTask<int> CountAsync(ValueTask<int> read, bool fromClient)
        {
            var count = await read.ConfigureAwait(false);
            if (count > 0)
            {
                if (fromClient) counters.AddFromClient(count);
                else counters.AddToClient(count);
            }
            return count;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!countReads) counters.AddToClient(buffer.Length);
            return inner.WriteAsync(buffer, cancellationToken);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteMemoryAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        private async ValueTask WriteMemoryAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
            await WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;
        public PipeWriter Output { get; } = output;
    }
}
