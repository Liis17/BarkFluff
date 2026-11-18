using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Bft;

public sealed record BftRelayPreambleInfo(IPAddress ClientIp, int ClientPort, long TimestampUnixSeconds, string RelayId, byte[] RelayNonce);

public static class BftRelayPreamble
{
    public const int RelayNonceLength = 16;
    public const int Length = 74;
    private static readonly UTF8Encoding StrictAscii = new(false, true);

    public static byte[] Create(ReadOnlySpan<byte> relayKey, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> relayNonce,
        IPAddress clientIp, int clientPort, long timestampUnixSeconds, string relayId)
    {
        ValidateInputs(relayKey, salt, relayNonce);
        ArgumentNullException.ThrowIfNull(clientIp);
        if (clientPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(clientPort));
        var relayIdBytes = EncodeRelayId(relayId);
        var address = clientIp.IsIPv4MappedToIPv6 ? clientIp : clientIp.MapToIPv6();
        var plaintext = new byte[42];
        address.GetAddressBytes().CopyTo(plaintext, 0);
        BinaryPrimitives.WriteUInt16BigEndian(plaintext.AsSpan(16), (ushort)clientPort);
        BinaryPrimitives.WriteUInt64BigEndian(plaintext.AsSpan(18), checked((ulong)timestampUnixSeconds));
        relayIdBytes.CopyTo(plaintext, 26);
        var key = DeriveKey(relayKey, salt, relayNonce);
        try
        {
            var encrypted = BftCrypto.EncryptChaCha(key, 0, ReadOnlySpan<byte>.Empty, plaintext);
            var output = new byte[Length];
            relayNonce.CopyTo(output);
            encrypted.CopyTo(output, RelayNonceLength);
            CryptographicOperations.ZeroMemory(encrypted);
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(relayIdBytes);
        }
    }

    public static BftRelayPreambleInfo Verify(ReadOnlySpan<byte> relayKey, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> preamble, long nowUnixSeconds)
    {
        if (relayKey.Length != 32 || salt.Length != 32) throw new ArgumentException("Relay key and salt must be 32 bytes.");
        if (preamble.Length != Length) throw new BftProtocolException("Relay preamble must be 74 bytes.");
        var nonce = preamble[..RelayNonceLength];
        var key = DeriveKey(relayKey, salt, nonce);
        byte[] plaintext;
        try { plaintext = BftCrypto.DecryptChaCha(key, 0, ReadOnlySpan<byte>.Empty, preamble[RelayNonceLength..]); }
        finally { CryptographicOperations.ZeroMemory(key); }

        try
        {
            if (plaintext.Length != 42) throw new BftProtocolException("Invalid relay preamble payload.");
            var timestampRaw = BinaryPrimitives.ReadUInt64BigEndian(plaintext.AsSpan(18));
            if (timestampRaw > long.MaxValue || Math.Abs((decimal)nowUnixSeconds - (long)timestampRaw) > 120)
                throw new BftProtocolException("Relay preamble timestamp is outside the allowed window.");
            var relayIdField = plaintext.AsSpan(26, 16);
            var relayIdLength = relayIdField.IndexOf((byte)0);
            if (relayIdLength < 0) relayIdLength = relayIdField.Length;
            if (relayIdLength == 0 || relayIdField[relayIdLength..].IndexOfAnyExcept((byte)0) >= 0)
                throw new BftProtocolException("Invalid relay identifier padding.");
            var relayIdBytes = relayIdField[..relayIdLength];
            if (relayIdBytes.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
                throw new BftProtocolException("Relay identifier must be ASCII.");
            string relayId;
            try { relayId = StrictAscii.GetString(relayIdBytes); }
            catch (DecoderFallbackException ex) { throw new BftProtocolException("Relay identifier must be ASCII.", ex); }
            return new BftRelayPreambleInfo(
                new IPAddress(plaintext.AsSpan(0, 16)),
                BinaryPrimitives.ReadUInt16BigEndian(plaintext.AsSpan(16)),
                (long)timestampRaw,
                relayId,
                nonce.ToArray());
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static byte[] DeriveKey(ReadOnlySpan<byte> relayKey, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> relayNonce)
    {
        var prk = BftCrypto.HkdfExtract(salt, relayKey);
        var label = "BFTv1 relay preamble"u8;
        var info = new byte[label.Length + relayNonce.Length];
        label.CopyTo(info);
        relayNonce.CopyTo(info.AsSpan(label.Length));
        try { return BftCrypto.HkdfExpand32(prk, info); }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
            CryptographicOperations.ZeroMemory(info);
        }
    }

    private static byte[] EncodeRelayId(string relayId)
    {
        ArgumentNullException.ThrowIfNull(relayId);
        if (relayId.Length is < 1 or > 16 || relayId.Any(c => c is < (char)0x20 or > (char)0x7E))
            throw new ArgumentException("Relay ID must be 1–16 printable ASCII bytes.", nameof(relayId));
        var bytes = Encoding.ASCII.GetBytes(relayId);
        var result = new byte[16];
        bytes.CopyTo(result, 0);
        CryptographicOperations.ZeroMemory(bytes);
        return result;
    }

    private static void ValidateInputs(ReadOnlySpan<byte> relayKey, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> relayNonce)
    {
        if (relayKey.Length != 32 || salt.Length != 32) throw new ArgumentException("Relay key and salt must be 32 bytes.");
        if (relayNonce.Length != RelayNonceLength) throw new ArgumentException("Relay nonce must be 16 bytes.", nameof(relayNonce));
    }
}
