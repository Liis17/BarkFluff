using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Security.Cryptography;

namespace BarkFluff.Bft;

public static class BftObfuscatedRecords
{
    public const int MaxPayloadLength = 16400;
    public const int MaxPaddingLength = 1024;
    public const int MinEncryptedLength = 18;
    public const int MaxEncryptedLength = 17442;
    public const ulong MaxRecordCount = 1UL << 48;

    public static byte[] Encrypt(BftObfuscationKeys keys, bool clientToServer, ulong sequence, ReadOnlySpan<byte> payload, int paddingLength = 0)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (sequence >= MaxRecordCount)
            throw new BftRecordException("Obfuscation record counter limit reached.");
        if (payload.Length > MaxPayloadLength)
            throw new ArgumentOutOfRangeException(nameof(payload));
        if (paddingLength is < 0 or > MaxPaddingLength)
            throw new ArgumentOutOfRangeException(nameof(paddingLength));

        var plainBodyLength = 2 + payload.Length + paddingLength;
        var plainBody = ArrayPool<byte>.Shared.Rent(plainBodyLength);
        var record = GC.AllocateUninitializedArray<byte>(18 + plainBodyLength + 16);
        try
        {
            plainBody.AsSpan(0, plainBodyLength).Clear();
            BinaryPrimitives.WriteUInt16BigEndian(plainBody, (ushort)payload.Length);
            payload.CopyTo(plainBody.AsSpan(2));
            var encryptedLength = checked((ushort)(plainBodyLength + 16));
            Span<byte> lengthBytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(lengthBytes, encryptedLength);
            BftCrypto.EncryptChaCha(keys.LengthCipher(clientToServer), sequence, ReadOnlySpan<byte>.Empty, lengthBytes,
                record.AsSpan(0, 2), record.AsSpan(2, 16));
            BftCrypto.EncryptChaCha(keys.DataCipher(clientToServer), sequence, ReadOnlySpan<byte>.Empty,
                plainBody.AsSpan(0, plainBodyLength), record.AsSpan(18, plainBodyLength), record.AsSpan(18 + plainBodyLength, 16));
            return record;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(record);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBody.AsSpan(0, plainBodyLength));
            ArrayPool<byte>.Shared.Return(plainBody);
        }
    }

    public static byte[] Decrypt(BftObfuscationKeys keys, bool clientToServer, ulong sequence, ReadOnlySpan<byte> record)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (sequence >= MaxRecordCount)
            throw new BftRecordException("Obfuscation record counter limit reached.");
        if (record.Length < 18)
            throw new BftRecordException("Truncated obfuscation record header.");
        Span<byte> lengthPlain = stackalloc byte[2];
        BftCrypto.DecryptChaCha(keys.LengthCipher(clientToServer), sequence, ReadOnlySpan<byte>.Empty, record[..2], record.Slice(2, 16), lengthPlain);
        var encryptedLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPlain);
        ValidateEncryptedLength(encryptedLength);
        if (record.Length != 18 + encryptedLength)
            throw new BftRecordException("Obfuscation record length does not match its header.");
        return DecryptBody(keys, clientToServer, sequence, record.Slice(18, encryptedLength));
    }

    internal static void ValidateEncryptedLength(int length)
    {
        if (length is < MinEncryptedLength or > MaxEncryptedLength)
            throw new BftRecordException("Obfuscation record length is out of range.");
    }

    internal static byte[] DecryptBody(BftObfuscationKeys keys, bool clientToServer, ulong sequence, ReadOnlySpan<byte> encryptedBody)
    {
        if (encryptedBody.Length < MinEncryptedLength)
            throw new BftRecordException("Invalid obfuscation record body.");
        var plainBodyLength = encryptedBody.Length - 16;
        var plainBody = ArrayPool<byte>.Shared.Rent(plainBodyLength);
        try
        {
            BftCrypto.DecryptChaCha(keys.DataCipher(clientToServer), sequence, ReadOnlySpan<byte>.Empty,
                encryptedBody[..^16], encryptedBody[^16..], plainBody.AsSpan(0, plainBodyLength));
            var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(plainBody);
            if (payloadLength > MaxPayloadLength || 2 + payloadLength > plainBodyLength)
                throw new BftRecordException("Obfuscation payload length exceeds its body.");
            if (plainBody.AsSpan(2 + payloadLength, plainBodyLength - 2 - payloadLength).IndexOfAnyExcept((byte)0) >= 0)
                throw new BftRecordException("Obfuscation padding is not zeroed.");
            return plainBody.AsSpan(2, payloadLength).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBody.AsSpan(0, plainBodyLength));
            ArrayPool<byte>.Shared.Return(plainBody);
        }
    }
}

public sealed class BftObfuscatedRecordWriter
{
    private readonly Stream _stream;
    private readonly BftObfuscationKeys _keys;
    private readonly bool _clientToServer;
    private readonly IBftRandomSource _random;
    private ulong _sequence;

    public BftObfuscatedRecordWriter(Stream stream, BftObfuscationKeys keys, bool clientToServer, ulong initialSequence = 0, IBftRandomSource? randomSource = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _clientToServer = clientToServer;
        _sequence = initialSequence;
        _random = randomSource ?? BftSystemRandomSource.Shared;
    }

    public ulong Sequence => _sequence;

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> payload, int paddingLength = 0, CancellationToken cancellationToken = default)
    {
        var record = BftObfuscatedRecords.Encrypt(_keys, _clientToServer, _sequence, payload.Span, paddingLength);
        _sequence++;
        try { await _stream.WriteAsync(record, cancellationToken).ConfigureAwait(false); }
        finally { CryptographicOperations.ZeroMemory(record); }
    }

    public ValueTask WriteRandomPaddingAsync(ReadOnlyMemory<byte> payload, int maximumPaddingLength, CancellationToken cancellationToken = default)
    {
        if (maximumPaddingLength is < 0 or > BftObfuscatedRecords.MaxPaddingLength)
            throw new ArgumentOutOfRangeException(nameof(maximumPaddingLength));
        return WriteAsync(payload, NextIntInclusive(maximumPaddingLength), cancellationToken);
    }

    private int NextIntInclusive(int max)
    {
        var range = (uint)max + 1;
        var limit = uint.MaxValue - uint.MaxValue % range;
        Span<byte> bytes = stackalloc byte[4];
        uint value;
        do
        {
            _random.Fill(bytes);
            value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        } while (value >= limit);
        return (int)(value % range);
    }
}

public sealed class BftObfuscatedRecordReader
{
    private readonly Stream? _stream;
    private readonly PipeReader? _pipeReader;
    private readonly BftObfuscationKeys _keys;
    private readonly bool _clientToServer;
    private ulong _sequence;

    public BftObfuscatedRecordReader(Stream stream, BftObfuscationKeys keys, bool clientToServer, ulong initialSequence = 0)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _clientToServer = clientToServer;
        _sequence = initialSequence;
    }

    public BftObfuscatedRecordReader(PipeReader pipeReader, BftObfuscationKeys keys, bool clientToServer, ulong initialSequence = 0)
    {
        _pipeReader = pipeReader ?? throw new ArgumentNullException(nameof(pipeReader));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _clientToServer = clientToServer;
        _sequence = initialSequence;
    }

    public ulong Sequence => _sequence;

    public async ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_sequence >= BftObfuscatedRecords.MaxRecordCount)
            throw new BftRecordException("Obfuscation record counter limit reached.");
        var header = ArrayPool<byte>.Shared.Rent(18);
        try
        {
            var hasHeader = _stream is not null
                ? await ReadStreamExactlyAsync(_stream, header.AsMemory(0, 18), allowCleanEof: true, cancellationToken).ConfigureAwait(false)
                : await ReadPipeExactlyAsync(_pipeReader!, header.AsMemory(0, 18), allowCleanEof: true, cancellationToken).ConfigureAwait(false);
            if (!hasHeader) return null;
            var length = DecryptLength(header);
            BftObfuscatedRecords.ValidateEncryptedLength(length);
            var body = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                if (_stream is not null)
                    _ = await ReadStreamExactlyAsync(_stream, body.AsMemory(0, length), allowCleanEof: false, cancellationToken).ConfigureAwait(false);
                else
                    _ = await ReadPipeExactlyAsync(_pipeReader!, body.AsMemory(0, length), allowCleanEof: false, cancellationToken).ConfigureAwait(false);
                var payload = BftObfuscatedRecords.DecryptBody(_keys, _clientToServer, _sequence, body.AsSpan(0, length));
                _sequence++;
                return payload;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(body.AsSpan(0, length));
                ArrayPool<byte>.Shared.Return(body);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(header.AsSpan(0, 18));
            ArrayPool<byte>.Shared.Return(header);
        }
    }

    private ushort DecryptLength(byte[] header)
    {
        Span<byte> lengthPlain = stackalloc byte[2];
        BftCrypto.DecryptChaCha(_keys.LengthCipher(_clientToServer), _sequence, ReadOnlySpan<byte>.Empty,
            header.AsSpan(0, 2), header.AsSpan(2, 16), lengthPlain);
        return BinaryPrimitives.ReadUInt16BigEndian(lengthPlain);
    }

    private static async ValueTask<bool> ReadStreamExactlyAsync(Stream stream, Memory<byte> buffer, bool allowCleanEof, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0 && allowCleanEof) return false;
                throw new BftRecordException("Connection ended in the middle of an obfuscation record.");
            }
            offset += read;
        }
        return true;
    }

    private static async ValueTask<bool> ReadPipeExactlyAsync(PipeReader reader, Memory<byte> destination, bool allowCleanEof, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;
            if (result.IsCanceled)
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                throw new OperationCanceledException(cancellationToken);
            }
            if (buffer.Length >= destination.Length)
            {
                buffer.Slice(0, destination.Length).CopyTo(destination.Span);
                var consumed = buffer.GetPosition(destination.Length);
                reader.AdvanceTo(consumed, consumed);
                return true;
            }

            if (result.IsCompleted)
            {
                reader.AdvanceTo(buffer.End);
                if (buffer.IsEmpty && allowCleanEof) return false;
                throw new BftRecordException("Connection ended in the middle of an obfuscation record.");
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }
}
