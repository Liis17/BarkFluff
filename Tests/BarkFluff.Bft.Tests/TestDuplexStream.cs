using System.IO.Pipelines;

namespace BarkFluff.Bft.Tests;

internal sealed class TestDuplexStream(Stream input, Stream output) : Stream
{
    private readonly object _outputGate = new();
    private readonly TaskCompletionSource _outputBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _writesUntilDrop = -1;
    private int _corruptNextByte = -1;
    private bool _blockOutputWrites;
    private long _totalBytesWritten;

    public static (TestDuplexStream Client, TestDuplexStream Server) CreatePair()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var client = new TestDuplexStream(serverToClient.Reader.AsStream(leaveOpen: true), clientToServer.Writer.AsStream(leaveOpen: true));
        var server = new TestDuplexStream(clientToServer.Reader.AsStream(leaveOpen: true), serverToClient.Writer.AsStream(leaveOpen: true));
        return (client, server);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public long TotalBytesWritten => Interlocked.Read(ref _totalBytesWritten);
    public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => input.ReadAsync(buffer, cancellationToken);
    public void DropOutputAfter(int allowedWrites)
    {
        lock (_outputGate) _writesUntilDrop = allowedWrites;
    }

    public void CorruptNextOutputAt(int byteOffset)
    {
        lock (_outputGate) _corruptNextByte = byteOffset;
    }

    public void BlockOutputWrites()
    {
        lock (_outputGate) _blockOutputWrites = true;
    }

    public Task WaitForBlockedOutputAsync(CancellationToken cancellationToken) => _outputBlocked.Task.WaitAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!ShouldDropOutput())
        {
            output.Write(buffer, offset, count);
            Interlocked.Add(ref _totalBytesWritten, count);
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (ShouldDropOutput()) return;
        int corruptAt;
        bool block;
        lock (_outputGate)
        {
            corruptAt = _corruptNextByte;
            _corruptNextByte = -1;
            block = _blockOutputWrites;
        }
        if (block)
        {
            _outputBlocked.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        if (corruptAt >= 0 && buffer.Length > 0)
        {
            var copy = buffer.ToArray();
            copy[corruptAt % copy.Length] ^= 0x01;
            await output.WriteAsync(copy, cancellationToken);
        }
        else await output.WriteAsync(buffer, cancellationToken);
        Interlocked.Add(ref _totalBytesWritten, buffer.Length);
    }
    public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
    public override void Flush() => output.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    private bool ShouldDropOutput()
    {
        lock (_outputGate)
        {
            if (_writesUntilDrop == 0) return true;
            if (_writesUntilDrop > 0) _writesUntilDrop--;
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            input.Dispose();
            output.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class RecordingStream(Stream inner) : Stream
{
    private readonly object _gate = new();
    private readonly MemoryStream _written = new();

    public byte[] WrittenBytes
    {
        get { lock (_gate) return _written.ToArray(); }
    }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
        lock (_gate) _written.Write(buffer, offset, count);
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken);
        lock (_gate) _written.Write(buffer.Span);
    }
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
