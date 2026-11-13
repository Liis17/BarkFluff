using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace BarkFluff.Bft;

public static class BftCrypto
{
    public static byte[] HkdfExtract(ReadOnlySpan<byte> salt, ReadOnlySpan<byte> inputKeyMaterial) =>
        HMACSHA256.HashData(salt, inputKeyMaterial);

    public static byte[] HkdfExpand32(ReadOnlySpan<byte> pseudorandomKey, ReadOnlySpan<byte> info)
    {
        var input = new byte[info.Length + 1];
        info.CopyTo(input);
        input[^1] = 1;
        try { return HMACSHA256.HashData(pseudorandomKey, input); }
        finally { CryptographicOperations.ZeroMemory(input); }
    }

    internal static void DeriveClientToServerLengthKey(ReadOnlySpan<byte> obfsKey, ReadOnlySpan<byte> salt, Span<byte> output)
    {
        if (obfsKey.Length != 32 || salt.Length != 32 || output.Length < 32)
            throw new ArgumentException("The obfs key, salt, and output must be 32 bytes.");
        Span<byte> pseudorandomKey = stackalloc byte[32];
        Span<byte> info = stackalloc byte[19];
        HMACSHA256.HashData(salt, obfsKey, pseudorandomKey);
        "BFTv1 obfs c2s len"u8.CopyTo(info);
        info[^1] = 1;
        HMACSHA256.HashData(pseudorandomKey, info, output[..32]);
        CryptographicOperations.ZeroMemory(pseudorandomKey);
    }

    public static byte[] GetX25519PublicKey(ReadOnlySpan<byte> privateKey)
    {
        if (privateKey.Length != 32)
            throw new ArgumentException("X25519 private keys must be 32 bytes.", nameof(privateKey));
        var privateBytes = privateKey.ToArray();
        try { return new X25519PrivateKeyParameters(privateBytes).GeneratePublicKey().GetEncoded(); }
        finally { CryptographicOperations.ZeroMemory(privateBytes); }
    }

    public static byte[] X25519(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
    {
        if (privateKey.Length != 32 || publicKey.Length != 32)
            throw new ArgumentException("X25519 keys must be 32 bytes.");
        var result = new byte[32];
        var privateBytes = privateKey.ToArray();
        var publicBytes = publicKey.ToArray();
        try
        {
            new X25519PrivateKeyParameters(privateBytes).GenerateSecret(
                new X25519PublicKeyParameters(publicBytes), result, 0);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            CryptographicOperations.ZeroMemory(result);
            throw new BftInvalidNoiseException("Invalid X25519 public key.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
            CryptographicOperations.ZeroMemory(publicBytes);
        }
        if (CryptographicOperations.FixedTimeEquals(result, new byte[32]))
        {
            CryptographicOperations.ZeroMemory(result);
            throw new BftInvalidNoiseException("X25519 produced an all-zero shared secret.");
        }
        return result;
    }

    internal static byte[] EncryptChaCha(ReadOnlySpan<byte> key, ulong nonceCounter, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> plaintext)
    {
        var output = new byte[plaintext.Length + 16];
        using var cipher = new ChaCha20Poly1305(key);
        EncryptChaCha(cipher, nonceCounter, associatedData, plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length, 16));
        return output;
    }

    internal static void EncryptChaCha(ChaCha20Poly1305 cipher, ulong nonceCounter, ReadOnlySpan<byte> associatedData,
        ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag)
    {
        Span<byte> nonce = stackalloc byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], nonceCounter);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
    }

    internal static byte[] DecryptChaCha(ReadOnlySpan<byte> key, ulong nonceCounter, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> ciphertextAndTag)
    {
        if (ciphertextAndTag.Length < 16)
            throw new BftProtocolException("Truncated ChaCha20-Poly1305 message.");
        var output = new byte[ciphertextAndTag.Length - 16];
        using var cipher = new ChaCha20Poly1305(key);
        try
        {
            DecryptChaCha(cipher, nonceCounter, associatedData, ciphertextAndTag[..^16], ciphertextAndTag[^16..], output);
            return output;
        }
        catch (BftRecordException ex)
        {
            CryptographicOperations.ZeroMemory(output);
            throw new BftProtocolException("ChaCha20-Poly1305 authentication failed.", ex);
        }
    }

    internal static void DecryptChaCha(ChaCha20Poly1305 cipher, ulong nonceCounter, ReadOnlySpan<byte> associatedData,
        ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext)
    {
        Span<byte> nonce = stackalloc byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], nonceCounter);
        try { cipher.Decrypt(nonce, ciphertext, tag, plaintext, associatedData); }
        catch (CryptographicException ex) { throw new BftRecordException("ChaCha20-Poly1305 authentication failed.", ex); }
    }
}

internal sealed class BftClientLengthAuthenticator
{
    private static readonly byte[] Nonce = new byte[12];
    private readonly ChaCha7539Engine _cipher = new();
    private readonly Poly1305 _mac = new();
    private readonly byte[] _lengthKey = new byte[32];
    private readonly byte[] _keystream = new byte[64];
    private readonly byte[] _polyKey = new byte[32];
    private readonly byte[] _plaintext = new byte[2];
    private readonly byte[] _macInput = new byte[32];
    private readonly byte[] _tag = new byte[16];
    private readonly byte[] _zeros = new byte[64];

    public bool TryAuthenticate(byte[] encryptedLength, ReadOnlySpan<byte> obfsKey, ReadOnlySpan<byte> salt, out ushort length)
    {
        BftCrypto.DeriveClientToServerLengthKey(obfsKey, salt, _lengthKey);
        try
        {
            _cipher.Init(true, new ParametersWithIV(new KeyParameter(_lengthKey), Nonce));
            // Block 0 supplies the Poly1305 key; the next two bytes decrypt the encrypted length.
            _cipher.ProcessBytes(_zeros, 0, _zeros.Length, _keystream, 0);
            _cipher.ProcessBytes(encryptedLength, 0, 2, _plaintext, 0);
            _keystream.AsSpan(0, 32).CopyTo(_polyKey);

            _mac.Init(new KeyParameter(_polyKey));
            // RFC 8439 MAC input: ciphertext padded to 16 bytes, then empty AAD and ciphertext lengths.
            _macInput.AsSpan().Clear();
            encryptedLength.AsSpan(0, 2).CopyTo(_macInput);
            BinaryPrimitives.WriteUInt64LittleEndian(_macInput.AsSpan(24, 8), 2);
            _mac.BlockUpdate(_macInput, 0, _macInput.Length);
            _mac.DoFinal(_tag, 0);
            if (!CryptographicOperations.FixedTimeEquals(_tag, encryptedLength.AsSpan(2, 16)))
            {
                length = 0;
                return false;
            }
            length = BinaryPrimitives.ReadUInt16BigEndian(_plaintext);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(_lengthKey);
            CryptographicOperations.ZeroMemory(_keystream);
            CryptographicOperations.ZeroMemory(_polyKey);
            CryptographicOperations.ZeroMemory(_plaintext);
            CryptographicOperations.ZeroMemory(_macInput);
            CryptographicOperations.ZeroMemory(_tag);
        }
    }
}

public sealed class BftObfuscationKeys : IDisposable
{
    private bool _disposed;
    private readonly ChaCha20Poly1305 _c2sLengthCipher;
    private readonly ChaCha20Poly1305 _c2sDataCipher;
    private readonly ChaCha20Poly1305 _s2cLengthCipher;
    private readonly ChaCha20Poly1305 _s2cDataCipher;

    private BftObfuscationKeys(byte[] c2sLength, byte[] c2sData, byte[] s2cLength, byte[] s2cData)
    {
        ClientToServerLength = c2sLength;
        ClientToServerData = c2sData;
        ServerToClientLength = s2cLength;
        ServerToClientData = s2cData;
        _c2sLengthCipher = new ChaCha20Poly1305(c2sLength);
        _c2sDataCipher = new ChaCha20Poly1305(c2sData);
        _s2cLengthCipher = new ChaCha20Poly1305(s2cLength);
        _s2cDataCipher = new ChaCha20Poly1305(s2cData);
    }

    public ReadOnlyMemory<byte> ClientToServerLength { get; }
    public ReadOnlyMemory<byte> ClientToServerData { get; }
    public ReadOnlyMemory<byte> ServerToClientLength { get; }
    public ReadOnlyMemory<byte> ServerToClientData { get; }

    public static BftObfuscationKeys Derive(ReadOnlySpan<byte> obfsKey, ReadOnlySpan<byte> salt)
    {
        if (obfsKey.Length != 32 || salt.Length != 32)
            throw new ArgumentException("The obfs key and salt must be 32 bytes.");
        var prk = BftCrypto.HkdfExtract(salt, obfsKey);
        try
        {
            return new BftObfuscationKeys(
                BftCrypto.HkdfExpand32(prk, "BFTv1 obfs c2s len"u8),
                BftCrypto.HkdfExpand32(prk, "BFTv1 obfs c2s data"u8),
                BftCrypto.HkdfExpand32(prk, "BFTv1 obfs s2c len"u8),
                BftCrypto.HkdfExpand32(prk, "BFTv1 obfs s2c data"u8));
        }
        finally { CryptographicOperations.ZeroMemory(prk); }
    }

    internal ChaCha20Poly1305 LengthCipher(bool clientToServer) => clientToServer ? _c2sLengthCipher : _s2cLengthCipher;
    internal ChaCha20Poly1305 DataCipher(bool clientToServer) => clientToServer ? _c2sDataCipher : _s2cDataCipher;

    public void Dispose()
    {
        if (_disposed) return;
        _c2sLengthCipher.Dispose();
        _c2sDataCipher.Dispose();
        _s2cLengthCipher.Dispose();
        _s2cDataCipher.Dispose();
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(ClientToServerLength).Span);
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(ClientToServerData).Span);
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(ServerToClientLength).Span);
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(ServerToClientData).Span);
        _disposed = true;
    }
}
