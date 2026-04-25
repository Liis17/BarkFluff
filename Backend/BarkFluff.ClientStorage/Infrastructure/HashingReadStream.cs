using System.Security.Cryptography;

namespace BarkFluff.ClientStorage.Infrastructure;

/// <summary>
/// Pass-through читаемый поток, вычисляющий инкрементальный SHA-256
/// одновременно с чтением данных (один проход без лишних копий).
/// </summary>
internal sealed class HashingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash;

    public HashingReadStream(Stream inner, IncrementalHash hash)
    {
        _inner = inner;
        _hash = hash;
    }

    public override bool CanRead  => true;
    public override bool CanSeek  => false;
    public override bool CanWrite => false;

    public override long Length   => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read > 0) _hash.AppendData(buffer, offset, read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var read = await _inner.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);
        if (read > 0) _hash.AppendData(buffer, offset, read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        if (read > 0) _hash.AppendData(buffer.Span[..read]);
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value)                 => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        // Внешний код управляет временем жизни _inner и _hash
        base.Dispose(disposing);
    }
}
