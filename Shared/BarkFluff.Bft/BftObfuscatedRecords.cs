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
            throw new BftProtocolException("Obfuscation record counter limit reached.");
        if (payload.Length > MaxPayloadLength)
            throw new ArgumentOutOfRangeException(nameof(payload));
        if (paddingLength is < 0 or > MaxPaddingLength)
            throw new ArgumentOutOfRangeException(nameof(paddingLength));

        var plainBody = new byte[2 + payload.Length + paddingLength];
        try
        {
            BinaryPrimitives.WriteUInt16BigEndian(plainBody, (ushort)payload.Length);
            payload.CopyTo(plainBody.AsSpan(2));
            var encryptedBody = BftCrypto.EncryptChaCha(keys.DataKey(clientToServer), sequence, ReadOnlySpan<byte>.Empty, plainBody);
            var encryptedLength = checked((ushort)encryptedBody.Length);
            Span<byte> lengthBytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(lengthBytes, encryptedLength);
            var encryptedHeader = BftCrypto.EncryptChaCha(keys.LengthKey(clientToServer), sequence, ReadOnlySpan<byte>.Empty, lengthBytes);
            var record = new byte[encryptedHeader.Length + encryptedBody.Length];
            encryptedHeader.CopyTo(record, 0);
            encryptedBody.CopyTo(record, encryptedHeader.Length);
            CryptographicOperations.ZeroMemory(encryptedBody);
            CryptographicOperations.ZeroMemory(encryptedHeader);
            return record;
        }
        finally { CryptographicOperations.ZeroMemory(plainBody); }
    }

    public static byte[] Decrypt(BftObfuscationKeys keys, bool clientToServer, ulong sequence, ReadOnlySpan<byte> record)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (sequence >= MaxRecordCount)
            throw new BftProtocolException("Obfuscation record counter limit reached.");
        if (record.Length < 18)
            throw new BftProtocolException("Truncated obfuscation record header.");
        var lengthPlain = BftCrypto.DecryptChaCha(keys.LengthKey(clientToServer), sequence, ReadOnlySpan<byte>.Empty, record[..18]);
        if (lengthPlain.Length != 2)
            throw new BftProtocolException("Invalid encrypted record length.");
        var encryptedLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPlain);
        CryptographicOperations.ZeroMemory(lengthPlain);
        ValidateEncryptedLength(encryptedLength);
        if (record.Length != 18 + encryptedLength)
            throw new BftProtocolException("Obfuscation record length does not match its header.");
        return DecryptBody(keys, clientToServer, sequence, record.Slice(18, encryptedLength));
    }

    internal static void ValidateEncryptedLength(int length)
    {
        if (length is < MinEncryptedLength or > MaxEncryptedLength)
            throw new BftProtocolException("Obfuscation record length is out of range.");
    }

    internal static byte[] DecryptBody(BftObfuscationKeys keys, bool clientToServer, ulong sequence, ReadOnlySpan<byte> encryptedBody)
    {
        var plainBody = BftCrypto.DecryptChaCha(keys.DataKey(clientToServer), sequence, ReadOnlySpan<byte>.Empty, encryptedBody);
        if (plainBody.Length < 2)
            throw new BftProtocolException("Invalid obfuscation record body.");
        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(plainBody);
        if (payloadLength > MaxPayloadLength || 2 + payloadLength > plainBody.Length)
            throw new BftProtocolException("Obfuscation payload length exceeds its body.");
        if (plainBody.AsSpan(2 + payloadLength).IndexOfAnyExcept((byte)0) >= 0)
        {
            CryptographicOperations.ZeroMemory(plainBody);
            throw new BftProtocolException("Obfuscation padding is not zeroed.");
        }
        var payload = plainBody.AsSpan(2, payloadLength).ToArray();
        CryptographicOperations.ZeroMemory(plainBody);
        return payload;
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

    public ValueTask WriteAsync(ReadOnlyMemory<byte> payload, int paddingLength = 0, CancellationToken cancellationToken = default)
    {
        var record = BftObfuscatedRecords.Encrypt(_keys, _clientToServer, _sequence, payload.Span, paddingLength);
        _sequence++;
        return _stream.WriteAsync(record, cancellationToken);
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
            throw new BftProtocolException("Obfuscation record counter limit reached.");
        var header = _stream is not null
            ? await ReadStreamExactlyAsync(_stream, 18, allowCleanEof: true, cancellationToken).ConfigureAwait(false)
            : await ReadPipeExactlyAsync(_pipeReader!, 18, allowCleanEof: true, cancellationToken).ConfigureAwait(false);
        if (header is null) return null;
        var lengthPlain = BftCrypto.DecryptChaCha(_keys.LengthKey(_clientToServer), _sequence, ReadOnlySpan<byte>.Empty, header);
        if (lengthPlain.Length != 2)
            throw new BftProtocolException("Invalid encrypted record length.");
        var length = BinaryPrimitives.ReadUInt16BigEndian(lengthPlain);
        CryptographicOperations.ZeroMemory(lengthPlain);
        BftObfuscatedRecords.ValidateEncryptedLength(length);
        var body = _stream is not null
            ? await ReadStreamExactlyAsync(_stream, length, allowCleanEof: false, cancellationToken).ConfigureAwait(false)
            : await ReadPipeExactlyAsync(_pipeReader!, length, allowCleanEof: false, cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = BftObfuscatedRecords.DecryptBody(_keys, _clientToServer, _sequence, body!);
            _sequence++;
            return payload;
        }
        finally { CryptographicOperations.ZeroMemory(body!); }
    }

    private static async ValueTask<byte[]?> ReadStreamExactlyAsync(Stream stream, int length, bool allowCleanEof, CancellationToken cancellationToken)
    {
        var bytes = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0 && allowCleanEof) return null;
                throw new BftProtocolException("Connection ended in the middle of an obfuscation record.");
            }
            offset += read;
        }
        return bytes;
    }

    private static async ValueTask<byte[]?> ReadPipeExactlyAsync(PipeReader reader, int length, bool allowCleanEof, CancellationToken cancellationToken)
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
            if (buffer.Length >= length)
            {
                var bytes = buffer.Slice(0, length).ToArray();
                var consumed = buffer.GetPosition(length);
                reader.AdvanceTo(consumed, consumed);
                return bytes;
            }

            if (result.IsCompleted)
            {
                reader.AdvanceTo(buffer.End);
                if (buffer.IsEmpty && allowCleanEof) return null;
                throw new BftProtocolException("Connection ended in the middle of an obfuscation record.");
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }
}
