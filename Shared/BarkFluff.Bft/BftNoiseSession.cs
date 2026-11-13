using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Bft;

public sealed class BftNoiseCipherState : IDisposable
{
    private ChaCha20Poly1305 _cipher;
    private bool _disposed;

    public BftNoiseCipherState(ReadOnlySpan<byte> key, ulong nonce = 0)
    {
        if (key.Length != 32) throw new ArgumentException("Noise cipher keys must be 32 bytes.", nameof(key));
        _cipher = new ChaCha20Poly1305(key);
        Counter = nonce;
    }

    public ulong Counter { get; private set; }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var encrypted = new byte[plaintext.Length + 16];
        BftCrypto.EncryptChaCha(_cipher, Counter, associatedData, plaintext, encrypted.AsSpan(0, plaintext.Length), encrypted.AsSpan(plaintext.Length, 16));
        Advance();
        return encrypted;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> ciphertextAndTag, ReadOnlySpan<byte> associatedData = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ciphertextAndTag.Length < 16) throw new BftRecordException("Truncated ChaCha20-Poly1305 message.");
        var plaintext = new byte[ciphertextAndTag.Length - 16];
        try
        {
            BftCrypto.DecryptChaCha(_cipher, Counter, associatedData, ciphertextAndTag[..^16], ciphertextAndTag[^16..], plaintext);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        Advance();
        return plaintext;
    }

    public void Rekey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Span<byte> plaintext = stackalloc byte[32];
        Span<byte> encrypted = stackalloc byte[48];
        plaintext.Clear();
        BftCrypto.EncryptChaCha(_cipher, ulong.MaxValue, ReadOnlySpan<byte>.Empty, plaintext, encrypted[..32], encrypted[32..]);
        var replacement = new ChaCha20Poly1305(encrypted[..32]);
        _cipher.Dispose();
        _cipher = replacement;
        CryptographicOperations.ZeroMemory(encrypted);
    }

    private void Advance()
    {
        if (Counter == ulong.MaxValue)
            throw new BftRecordException("Noise nonce counter exhausted.");
        Counter++;
        if (Counter != 0 && Counter % 65536 == 0)
            Rekey();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cipher.Dispose();
        _disposed = true;
    }
}

public sealed class BftNoiseSession : IAsyncDisposable
{
    private readonly BftObfuscatedRecordReader _recordReader;
    private readonly BftObfuscatedRecordWriter _recordWriter;
    private readonly BftNoiseCipherState _readCipher;
    private readonly BftNoiseCipherState _writeCipher;
    private readonly BftObfuscationKeys _obfuscationKeys;
    private readonly object _progressGate = new();
    private TaskCompletionSource _writeProgress = NewSignal();
    private TaskCompletionSource _readProgress = NewSignal();
    private bool _disposed;

    internal BftNoiseSession(Stream stream, bool isClient, BftObfuscationKeys keys, byte[] c2sKey, byte[] s2cKey, byte[] handshakeHash, IBftRandomSource randomSource)
    {
        _obfuscationKeys = keys;
        _recordWriter = new BftObfuscatedRecordWriter(stream, keys, isClient, initialSequence: 1, randomSource);
        _recordReader = new BftObfuscatedRecordReader(stream, keys, clientToServer: !isClient, initialSequence: 1);
        _writeCipher = new BftNoiseCipherState(isClient ? c2sKey : s2cKey);
        _readCipher = new BftNoiseCipherState(isClient ? s2cKey : c2sKey);
        HandshakeHash = handshakeHash;
        CryptographicOperations.ZeroMemory(c2sKey);
        CryptographicOperations.ZeroMemory(s2cKey);
    }

    public byte[] HandshakeHash { get; }
    internal ulong WriteNoiseCounter => _writeCipher.Counter;
    internal ulong ReadNoiseCounter => _readCipher.Counter;

    internal Task WaitForWriteCounterAsync(ulong counter, CancellationToken cancellationToken) => WaitForCounterAsync(
        () => WriteNoiseCounter, () => _writeProgress.Task, counter, cancellationToken);

    internal Task WaitForReadCounterAsync(ulong counter, CancellationToken cancellationToken) => WaitForCounterAsync(
        () => ReadNoiseCounter, () => _readProgress.Task, counter, cancellationToken);

    public async ValueTask WriteMessageAsync(ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (plaintext.Length > 16384) throw new ArgumentOutOfRangeException(nameof(plaintext));
        var encrypted = _writeCipher.Encrypt(plaintext.Span);
        try
        {
            if (encrypted.Length < 1024)
                await _recordWriter.WriteRandomPaddingAsync(encrypted, 255, cancellationToken).ConfigureAwait(false);
            else
                await _recordWriter.WriteAsync(encrypted, 0, cancellationToken).ConfigureAwait(false);
            lock (_progressGate)
            {
                var previous = _writeProgress;
                _writeProgress = NewSignal();
                previous.TrySetResult();
            }
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }

    public async ValueTask<byte[]?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var encrypted = await _recordReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (encrypted is null) return null;
        try
        {
            if (encrypted.Length is < 16 or > 16400)
                throw new BftRecordException("Invalid Noise transport message length.");
            var plaintext = _readCipher.Decrypt(encrypted);
            if (plaintext.Length > 16384)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                throw new BftRecordException("Noise transport plaintext exceeds 16384 bytes.");
            }
            lock (_progressGate)
            {
                var previous = _readProgress;
                _readProgress = NewSignal();
                previous.TrySetResult();
            }
            return plaintext;
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _writeCipher.Dispose();
        _readCipher.Dispose();
        _obfuscationKeys.Dispose();
        CryptographicOperations.ZeroMemory(HandshakeHash);
        lock (_progressGate)
        {
            _writeProgress.TrySetResult();
            _readProgress.TrySetResult();
        }
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private async Task WaitForCounterAsync(Func<ulong> getCounter, Func<Task> getSignal, ulong target, CancellationToken cancellationToken)
    {
        while (getCounter() < target)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Task signal;
            lock (_progressGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (getCounter() >= target) return;
                signal = getSignal();
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class BftNoiseHandshakeState : IDisposable
{
    private static readonly byte[] ProtocolName = Encoding.ASCII.GetBytes("Noise_NK_25519_ChaChaPoly_SHA256");
    private byte[] _chainingKey;
    private byte[] _hash;
    private byte[]? _key;
    private ulong _nonce;

    public BftNoiseHandshakeState(ReadOnlySpan<byte> prologue, ReadOnlySpan<byte> responderStaticPublicKey)
    {
        _hash = ProtocolName.ToArray();
        _chainingKey = ProtocolName.ToArray();
        MixHash(prologue);
        MixHash(responderStaticPublicKey);
    }

    public byte[] Hash => _hash.ToArray();

    public void MixHash(ReadOnlySpan<byte> data)
    {
        var input = new byte[_hash.Length + data.Length];
        _hash.CopyTo(input, 0);
        data.CopyTo(input.AsSpan(_hash.Length));
        var next = SHA256.HashData(input);
        CryptographicOperations.ZeroMemory(input);
        CryptographicOperations.ZeroMemory(_hash);
        _hash = next;
    }

    public void MixKey(ReadOnlySpan<byte> inputKeyMaterial)
    {
        var temporary = HMACSHA256.HashData(_chainingKey, inputKeyMaterial);
        var nextChainingKey = HMACSHA256.HashData(temporary, new byte[] { 1 });
        var output = new byte[nextChainingKey.Length + 1];
        nextChainingKey.CopyTo(output, 0);
        output[^1] = 2;
        var nextKey = HMACSHA256.HashData(temporary, output);
        CryptographicOperations.ZeroMemory(temporary);
        CryptographicOperations.ZeroMemory(output);
        CryptographicOperations.ZeroMemory(_chainingKey);
        if (_key is not null) CryptographicOperations.ZeroMemory(_key);
        _chainingKey = nextChainingKey;
        _key = nextKey;
        _nonce = 0;
    }

    public byte[] EncryptAndHash(ReadOnlySpan<byte> plaintext)
    {
        var key = _key ?? throw new InvalidOperationException("Noise handshake key is not initialized.");
        var ciphertext = BftCrypto.EncryptChaCha(key, _nonce++, _hash, plaintext);
        MixHash(ciphertext);
        return ciphertext;
    }

    public byte[] DecryptAndHash(ReadOnlySpan<byte> ciphertext)
    {
        var key = _key ?? throw new InvalidOperationException("Noise handshake key is not initialized.");
        var plaintext = BftCrypto.DecryptChaCha(key, _nonce++, _hash, ciphertext);
        MixHash(ciphertext);
        return plaintext;
    }

    public (byte[] First, byte[] Second) Split()
    {
        var temporary = HMACSHA256.HashData(_chainingKey, ReadOnlySpan<byte>.Empty);
        var first = HMACSHA256.HashData(temporary, new byte[] { 1 });
        var output = new byte[first.Length + 1];
        first.CopyTo(output, 0);
        output[^1] = 2;
        var second = HMACSHA256.HashData(temporary, output);
        CryptographicOperations.ZeroMemory(temporary);
        CryptographicOperations.ZeroMemory(output);
        return (first, second);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_chainingKey);
        CryptographicOperations.ZeroMemory(_hash);
        if (_key is not null) CryptographicOperations.ZeroMemory(_key);
    }
}

public sealed record BftClientFlight(byte[] Salt, byte[] Header, byte[] NoiseMessage, byte[] Record);

public sealed class BftClientHandshakeOptions
{
    public required byte[] StaticPublicKey { get; init; }
    public required byte[] ObfsKey { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public IBftRandomSource RandomSource { get; init; } = BftSystemRandomSource.Shared;
    public Func<byte[]>? EphemeralPrivateKeyFactory { get; init; }
    public byte[]? Salt { get; init; }
    public long? TimestampUnixSeconds { get; init; }
    public int? FirstRecordPaddingLength { get; init; }
}

public sealed record BftServerObfsKey(string Label, byte[] Key);

public sealed class BftServerHandshakeOptions
{
    public required byte[] StaticPrivateKey { get; init; }
    public required IReadOnlyList<BftServerObfsKey> ObfsKeys { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public IBftRandomSource RandomSource { get; init; } = BftSystemRandomSource.Shared;
    public Func<byte[]>? EphemeralPrivateKeyFactory { get; init; }
    public BftSaltReplayCache SaltCache { get; init; } = new();
}

public enum BftServerHandshakeRejection
{
    InvalidClientFlight,
    ReplayedSalt,
    SaltCacheFull,
    InvalidNoiseMessage,
    FirstRecordTimeout
}

public sealed record BftServerHandshakeResult(BftNoiseSession? Session, BftServerHandshakeRejection? Rejection)
{
    public string? MatchedObfsKeyLabel { get; init; }
    public bool Accepted => Session is not null;
}

public static class BftClientHandshake
{
    public static BftClientFlight CreateFlight(BftClientHandshakeOptions options)
    {
        using var material = BuildFlight(options);
        return material.Flight;
    }

    public static async ValueTask<BftNoiseSession> ConnectAsync(Stream stream, BftClientHandshakeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var material = BuildFlight(options);
        var flight = material.Flight;
        try
        {
            await stream.WriteAsync(flight.Salt, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(flight.Record, cancellationToken).ConfigureAwait(false);
            var keys = material.Keys;
            var reader = new BftObfuscatedRecordReader(stream, keys, clientToServer: false);
            var message2 = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new BftProtocolException("Exit closed before its first Noise message.");
            if (message2.Length != 48) throw new BftProtocolException("Invalid Exit Noise message length.");
            material.Noise.MixHash(message2.AsSpan(0, 32));
            var ee = BftCrypto.X25519(material.EphemeralPrivateKey, message2.AsSpan(0, 32));
            try { material.Noise.MixKey(ee); }
            finally { CryptographicOperations.ZeroMemory(ee); }
            var empty = material.Noise.DecryptAndHash(message2.AsSpan(32));
            var emptyPayload = empty.Length == 0;
            CryptographicOperations.ZeroMemory(empty);
            if (!emptyPayload) throw new BftProtocolException("Noise responder payload must be empty.");
            var (c2s, s2c) = material.Noise.Split();
            return new BftNoiseSession(stream, isClient: true, material.TakeKeys(), c2s, s2c, material.Noise.Hash, options.RandomSource);
        }
        finally { CryptographicOperations.ZeroMemory(flight.Record); }
    }

    private static ClientHandshakeMaterial BuildFlight(BftClientHandshakeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options.StaticPublicKey, options.ObfsKey);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ArgumentNullException.ThrowIfNull(options.RandomSource);
        var salt = options.Salt?.ToArray() ?? Fill(options.RandomSource, 32);
        if (salt.Length != 32) throw new ArgumentException("BFT salt must be 32 bytes.", nameof(options));
        var privateKey = GetEphemeralPrivateKey(options.EphemeralPrivateKeyFactory, options.RandomSource);
        BftNoiseHandshakeState? noise = null;
        BftObfuscationKeys? keys = null;
        byte[]? payload = null;
        try
        {
            var ephemeralPublic = BftCrypto.GetX25519PublicKey(privateKey);
            var header = new byte[10];
            header[0] = 1;
            header[1] = 0;
            BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(2), checked((ulong)(options.TimestampUnixSeconds ?? options.TimeProvider.GetUtcNow().ToUnixTimeSeconds())));
            var prologue = new byte[47];
            "BFTv1"u8.CopyTo(prologue);
            salt.CopyTo(prologue, 5);
            header.CopyTo(prologue, 37);
            noise = new BftNoiseHandshakeState(prologue, options.StaticPublicKey);
            noise.MixHash(ephemeralPublic);
            var es = BftCrypto.X25519(privateKey, options.StaticPublicKey);
            try { noise.MixKey(es); }
            finally { CryptographicOperations.ZeroMemory(es); }
            var encryptedEmpty = noise.EncryptAndHash(ReadOnlySpan<byte>.Empty);
            var noiseMessage = new byte[48];
            ephemeralPublic.CopyTo(noiseMessage, 0);
            encryptedEmpty.CopyTo(noiseMessage, 32);
            payload = new byte[58];
            header.CopyTo(payload, 0);
            noiseMessage.CopyTo(payload, 10);
            keys = BftObfuscationKeys.Derive(options.ObfsKey, salt);
            var padLength = options.FirstRecordPaddingLength ?? NextIntInclusive(options.RandomSource, BftObfuscatedRecords.MaxPaddingLength);
            var record = BftObfuscatedRecords.Encrypt(keys, true, 0, payload, padLength);
            var flight = new BftClientFlight(salt, header, noiseMessage, record);
            CryptographicOperations.ZeroMemory(payload);
            return new ClientHandshakeMaterial(flight, privateKey, noise, keys);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(privateKey);
            noise?.Dispose();
            keys?.Dispose();
            if (payload is not null) CryptographicOperations.ZeroMemory(payload);
            throw;
        }
    }

    private static void ValidateOptions(byte[] staticPublicKey, byte[] obfsKey)
    {
        if (staticPublicKey is null || staticPublicKey.Length != 32) throw new ArgumentException("Exit public key must be 32 bytes.");
        if (obfsKey is null || obfsKey.Length != 32) throw new ArgumentException("Obfs key must be 32 bytes.");
    }

    private static byte[] GetEphemeralPrivateKey(Func<byte[]>? factory, IBftRandomSource random)
    {
        var value = factory?.Invoke() ?? Fill(random, 32);
        if (value.Length != 32)
        {
            CryptographicOperations.ZeroMemory(value);
            throw new ArgumentException("X25519 ephemeral private keys must be 32 bytes.");
        }
        return value;
    }

    private static byte[] Fill(IBftRandomSource random, int count)
    {
        var value = new byte[count];
        random.Fill(value);
        return value;
    }

    private static int NextIntInclusive(IBftRandomSource random, int max)
    {
        var range = (uint)max + 1;
        var limit = uint.MaxValue - uint.MaxValue % range;
        Span<byte> bytes = stackalloc byte[4];
        uint value;
        do
        {
            random.Fill(bytes);
            value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        } while (value >= limit);
        return (int)(value % range);
    }

    private sealed class ClientHandshakeMaterial(BftClientFlight flight, byte[] ephemeralPrivateKey, BftNoiseHandshakeState noise, BftObfuscationKeys keys) : IDisposable
    {
        private BftObfuscationKeys? _keys = keys;
        public BftClientFlight Flight { get; } = flight;
        public byte[] EphemeralPrivateKey { get; } = ephemeralPrivateKey;
        public BftNoiseHandshakeState Noise { get; } = noise;
        public BftObfuscationKeys Keys => _keys ?? throw new ObjectDisposedException(nameof(ClientHandshakeMaterial));
        public BftObfuscationKeys TakeKeys() => Interlocked.Exchange(ref _keys, null)!;
        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(EphemeralPrivateKey);
            Noise.Dispose();
            _keys?.Dispose();
            _keys = null;
        }
    }
}

public static class BftServerHandshake
{
    public static async ValueTask<BftServerHandshakeResult> TryAcceptAsync(Stream stream, BftServerHandshakeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        if (options.StaticPrivateKey is null || options.StaticPrivateKey.Length != 32)
            throw new ArgumentException("Exit static private key must be 32 bytes.", nameof(options));
        ValidateObfsKeys(options.ObfsKeys);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ArgumentNullException.ThrowIfNull(options.RandomSource);
        ArgumentNullException.ThrowIfNull(options.SaltCache);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), options.TimeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        byte[]? salt = null;
        byte[]? encryptedLength = null;
        byte[]? encryptedBody = null;
        byte[]? clientPayload = null;
        byte[]? privateEphemeral = null;
        BftObfuscationKeys? keys = null;
        BftNoiseHandshakeState? noise = null;
        try
        {
            salt = new byte[32];
            await stream.ReadExactlyAsync(salt, linked.Token).ConfigureAwait(false);
            encryptedLength = new byte[18];
            await stream.ReadExactlyAsync(encryptedLength, linked.Token).ConfigureAwait(false);
            BftServerObfsKey? matchedKey = null;
            ushort recordLength = 0;
            var lengthAuthenticator = new BftClientLengthAuthenticator();
            foreach (var candidate in options.ObfsKeys)
            {
                if (!lengthAuthenticator.TryAuthenticate(encryptedLength, candidate.Key, salt, out recordLength)) continue;
                matchedKey = candidate;
                break;
            }
            if (matchedKey is null) return Reject(BftServerHandshakeRejection.InvalidClientFlight);

            BftObfuscatedRecords.ValidateEncryptedLength(recordLength);
            keys = BftObfuscationKeys.Derive(matchedKey.Key, salt);
            encryptedBody = new byte[recordLength];
            await stream.ReadExactlyAsync(encryptedBody, linked.Token).ConfigureAwait(false);
            clientPayload = BftObfuscatedRecords.DecryptBody(keys, clientToServer: true, sequence: 0, encryptedBody);
            if (clientPayload.Length != 58)
                return Reject(BftServerHandshakeRejection.InvalidClientFlight);

            var header = clientPayload.AsSpan(0, 10);
            var version = header[0];
            var wrapper = header[1];
            var timestampRaw = BinaryPrimitives.ReadUInt64BigEndian(header[2..]);
            if (version != 1 || wrapper != 0 || timestampRaw > long.MaxValue)
                return Reject(BftServerHandshakeRejection.InvalidClientFlight);
            var timestamp = (long)timestampRaw;
            var now = options.TimeProvider.GetUtcNow().ToUnixTimeSeconds();
            if (Math.Abs((decimal)now - timestamp) > 120)
                return Reject(BftServerHandshakeRejection.InvalidClientFlight);
            var addResult = options.SaltCache.TryAdd(salt);
            if (addResult == BftSaltReplayAddResult.Duplicate)
                return Reject(BftServerHandshakeRejection.ReplayedSalt);
            if (addResult == BftSaltReplayAddResult.Full)
                return Reject(BftServerHandshakeRejection.SaltCacheFull);

            var staticPublic = BftCrypto.GetX25519PublicKey(options.StaticPrivateKey);
            var prologue = new byte[47];
            "BFTv1"u8.CopyTo(prologue);
            salt.CopyTo(prologue, 5);
            header.CopyTo(prologue.AsSpan(37));
            noise = new BftNoiseHandshakeState(prologue, staticPublic);
            var clientEphemeral = clientPayload.AsSpan(10, 32);
            noise.MixHash(clientEphemeral);
            var es = BftCrypto.X25519(options.StaticPrivateKey, clientEphemeral);
            try { noise.MixKey(es); }
            finally { CryptographicOperations.ZeroMemory(es); }
            var clientEmpty = noise.DecryptAndHash(clientPayload.AsSpan(42));
            if (clientEmpty.Length != 0)
            {
                CryptographicOperations.ZeroMemory(clientEmpty);
                return Reject(BftServerHandshakeRejection.InvalidNoiseMessage);
            }
            CryptographicOperations.ZeroMemory(clientEmpty);

            privateEphemeral = options.EphemeralPrivateKeyFactory?.Invoke() ?? RandomKey(options.RandomSource);
            if (privateEphemeral.Length != 32)
                throw new ArgumentException("X25519 ephemeral private keys must be 32 bytes.", nameof(options));
            var serverEphemeralPublic = BftCrypto.GetX25519PublicKey(privateEphemeral);
            noise.MixHash(serverEphemeralPublic);
            var ee = BftCrypto.X25519(privateEphemeral, clientEphemeral);
            try { noise.MixKey(ee); }
            finally { CryptographicOperations.ZeroMemory(ee); }
            var serverEmpty = noise.EncryptAndHash(ReadOnlySpan<byte>.Empty);
            var serverMessage = new byte[serverEphemeralPublic.Length + serverEmpty.Length];
            serverEphemeralPublic.CopyTo(serverMessage, 0);
            serverEmpty.CopyTo(serverMessage, serverEphemeralPublic.Length);
            var writer = new BftObfuscatedRecordWriter(stream, keys, clientToServer: false, randomSource: options.RandomSource);
            await writer.WriteRandomPaddingAsync(serverMessage, BftObfuscatedRecords.MaxPaddingLength, cancellationToken).ConfigureAwait(false);
            var (c2s, s2c) = noise.Split();
            var session = new BftNoiseSession(stream, isClient: false, keys, c2s, s2c, noise.Hash, options.RandomSource);
            keys = null;
            return new BftServerHandshakeResult(session, null) { MatchedObfsKeyLabel = matchedKey.Label };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return Reject(BftServerHandshakeRejection.FirstRecordTimeout);
        }
        catch (Exception ex) when (ex is BftProtocolException or EndOfStreamException or CryptographicException or ArgumentException)
        {
            return Reject(ex is BftInvalidNoiseException
                ? BftServerHandshakeRejection.InvalidNoiseMessage
                : BftServerHandshakeRejection.InvalidClientFlight);
        }
        finally
        {
            if (salt is not null) CryptographicOperations.ZeroMemory(salt);
            if (encryptedLength is not null) CryptographicOperations.ZeroMemory(encryptedLength);
            if (encryptedBody is not null) CryptographicOperations.ZeroMemory(encryptedBody);
            if (clientPayload is not null) CryptographicOperations.ZeroMemory(clientPayload);
            if (privateEphemeral is not null) CryptographicOperations.ZeroMemory(privateEphemeral);
            noise?.Dispose();
            keys?.Dispose();
        }

        static BftServerHandshakeResult Reject(BftServerHandshakeRejection reason) => new(null, reason);
    }

    private static void ValidateObfsKeys(IReadOnlyList<BftServerObfsKey> obfsKeys)
    {
        ArgumentNullException.ThrowIfNull(obfsKeys);
        if (obfsKeys.Count > 1024) throw new ArgumentOutOfRangeException(nameof(obfsKeys), "Exit can configure no more than 1024 obfs keys.");
        foreach (var key in obfsKeys)
        {
            if (key is null || key.Key is null || key.Key.Length != 32)
                throw new ArgumentException("Every Exit obfs key must be 32 bytes.", nameof(obfsKeys));
            if (!IsValidKeyLabel(key.Label))
                throw new ArgumentException("Every Exit obfs key label must contain 1–64 characters.", nameof(obfsKeys));
        }
    }

    private static bool IsValidKeyLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        var count = 0;
        foreach (var _ in label.EnumerateRunes())
            if (++count > 64) return false;
        return count > 0;
    }

    private static byte[] RandomKey(IBftRandomSource random)
    {
        var key = new byte[32];
        random.Fill(key);
        return key;
    }
}
