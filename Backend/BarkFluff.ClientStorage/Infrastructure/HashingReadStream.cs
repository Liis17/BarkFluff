using System.Security.Cryptography;

namespace BarkFluff.ClientStorage.Infrastructure;

/// <summary>
/// Read-only обёртка над потоком, которая параллельно с чтением накапливает SHA-256.
/// Используется для однопроходной заливки в S3 без повторного прохода по файлу.
/// </summary>
public sealed class HashingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash;
    private long _position;

    public HashingReadStream(Stream inner, IncrementalHash hash)
    {
        _inner = inner;
        _hash  = hash;
    }

    public byte[] GetHashAndReset() => _hash.GetHashAndReset();

    public override bool CanRead  => _inner.CanRead;
    public override bool CanSeek  => false;
    public override bool CanWrite => false;
    public override long Length   => _inner.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read > 0)
        {
            _hash.AppendData(buffer, offset, read);
            _position += read;
        }
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        if (read > 0)
        {
            _hash.AppendData(buffer.Span[..read]);
            _position += read;
        }
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value)               => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
