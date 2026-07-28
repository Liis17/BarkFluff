using System.Threading;

namespace BarkFluff.Files.Host;

/// <summary>Проксирует поток и запоминает байты, реально запрошенные HTTP-ответом.</summary>
public sealed class CountingReadStream(Stream inner) : Stream
{
    private long _bytesRead;

    public long BytesRead => Interlocked.Read(ref _bytesRead);
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Interlocked.Add(ref _bytesRead, read);
        return read;
    }
    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Interlocked.Add(ref _bytesRead, read);
        return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        Interlocked.Add(ref _bytesRead, read);
        return read;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsyncCore(buffer, offset, count, cancellationToken);
    private async Task<int> ReadAsyncCore(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer, offset, count, cancellationToken);
        Interlocked.Add(ref _bytesRead, read);
        return read;
    }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        await base.DisposeAsync();
    }
}
