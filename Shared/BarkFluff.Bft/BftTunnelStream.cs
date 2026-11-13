using System.IO.Pipelines;

namespace BarkFluff.Bft;

public sealed class BftTunnelStream : Stream
{
    private readonly BftMuxConnection _connection;
    private readonly BftMuxStreamState _state;
    private byte[]? _currentRead;
    private bool _disposed;

    internal BftTunnelStream(BftMuxConnection connection, BftMuxStreamState state)
    {
        _connection = connection;
        _state = state;
    }

    public uint StreamId => _state.Id;
    public byte ServiceId => _state.ServiceId;
    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public IDuplexPipe AsDuplexPipe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new StreamDuplexPipe(this);
    }

    public ValueTask CompleteWritesAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _connection.CompleteWrite(_state);
        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(BftResetCode code = BftResetCode.Cancel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _connection.Reset(_state, code);
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty) return 0;
        try { _connection.CheckStreamReadable(_state); }
        catch
        {
            if (_currentRead is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(_currentRead);
            _currentRead = null;
            throw;
        }
        if (_currentRead is null)
        {
            _currentRead = await _connection.TakeChunkForStreamAsync(_state, cancellationToken).ConfigureAwait(false);
            if (_currentRead is null) return 0;
        }
        var count = Math.Min(buffer.Length, _currentRead.Length);
        _currentRead.AsMemory(0, count).CopyTo(buffer);
        if (count == _currentRead.Length)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(_currentRead);
            _currentRead = null;
        }
        else
        {
            var remaining = _currentRead.AsSpan(count).ToArray();
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(_currentRead);
            _currentRead = remaining;
        }
        _connection.ReportStreamRead(_state, count);
        return count;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _connection.WriteAsync(_state, buffer, cancellationToken);
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public override void Flush() => ObjectDisposedException.ThrowIf(_disposed, this);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("BFT streams require asynchronous reads.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("BFT streams require asynchronous writes.");
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            if (_currentRead is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(_currentRead);
            _connection.DisposeStream(_state);
        }
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_currentRead is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(_currentRead);
            _connection.DisposeStream(_state);
        }
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private sealed class StreamDuplexPipe : IDuplexPipe, IDisposable
    {
        public StreamDuplexPipe(Stream stream)
        {
            Input = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
            Output = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        }

        public PipeReader Input { get; }
        public PipeWriter Output { get; }

        public void Dispose()
        {
            Input.Complete();
            Output.Complete();
        }
    }
}
