using System.Globalization;
using System.Net;
using System.Text;

namespace BarkFluff.Bft;

public sealed record BftConnectionSettings(byte[] StaticPublicKey, byte[] ObfsKey, string Host, int Port, string? Token)
{
    public string? Name { get; init; }
    public override string ToString() => BftConnectionString.Format(this);
}

public static class BftConnectionString
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static BftConnectionSettings Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.StartsWith("bft://", StringComparison.Ordinal) || value.Contains('#') || value.IndexOf('/', 6) >= 0)
            throw new FormatException("Invalid BFT connection string.");

        var queryIndex = value.IndexOf('?', 6);
        var authority = queryIndex < 0 ? value[6..] : value[6..queryIndex];
        string? token = null;
        string? name = null;
        if (queryIndex >= 0)
        {
            var seenToken = false;
            var seenName = false;
            foreach (var parameter in value[(queryIndex + 1)..].Split('&'))
            {
                var parameterSeparator = parameter.IndexOf('=');
                if (parameterSeparator <= 0) throw new FormatException("Invalid BFT connection string query.");
                var key = parameter[..parameterSeparator];
                var encodedValue = parameter[(parameterSeparator + 1)..];
                switch (key)
                {
                    case "token" when !seenToken:
                        seenToken = true;
                        token = DecodeParameter(encodedValue, 1, 256, "Token");
                        break;
                    case "name" when !seenName:
                        seenName = true;
                        name = DecodeName(encodedValue);
                        break;
                    default:
                        throw new FormatException("Unknown or repeated BFT connection string parameter.");
                }
            }
        }

        var at = authority.IndexOf('@');
        if (at <= 0 || at != authority.LastIndexOf('@') || at == authority.Length - 1)
            throw new FormatException("Invalid BFT connection string authority.");

        var keyPart = authority[..at];
        var separator = keyPart.IndexOf('.');
        if (separator <= 0 || separator != keyPart.LastIndexOf('.') || separator == keyPart.Length - 1)
            throw new FormatException("Invalid BFT key pair.");

        var staticPublicKey = DecodeKey(keyPart[..separator]);
        var obfsKey = DecodeKey(keyPart[(separator + 1)..]);
        var (host, port) = ParseHostPort(authority[(at + 1)..]);
        return new BftConnectionSettings(staticPublicKey, obfsKey, host, port, token) { Name = name };
    }

    public static string Format(BftConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.StaticPublicKey.Length != 32 || settings.ObfsKey.Length != 32)
            throw new ArgumentException("BFT keys must be 32 bytes.", nameof(settings));
        if (settings.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(settings));
        var host = settings.Host.Contains(':') ? $"[{settings.Host}]" : settings.Host;
        var parameters = new List<string>(2);
        if (settings.Token is not null) parameters.Add($"token={EncodeParameter(settings.Token)}");
        if (settings.Name is not null) parameters.Add($"name={EncodeName(settings.Name)}");
        var query = parameters.Count == 0 ? string.Empty : $"?{string.Join('&', parameters)}";
        return $"bft://{EncodeKey(settings.StaticPublicKey)}.{EncodeKey(settings.ObfsKey)}@{host}:{settings.Port}{query}";
    }

    private static (string Host, int Port) ParseHostPort(string value)
    {
        string host;
        string portText;
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var end = value.IndexOf(']');
            if (end <= 1 || end + 1 >= value.Length || value[end + 1] != ':')
                throw new FormatException("Invalid IPv6 host.");
            host = value[1..end];
            if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                throw new FormatException("Invalid IPv6 host.");
            portText = value[(end + 2)..];
        }
        else
        {
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || separator != value.IndexOf(':'))
                throw new FormatException("A host and port are required; IPv6 hosts must be bracketed.");
            host = value[..separator];
            portText = value[(separator + 1)..];
            if (host.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
                throw new FormatException("Invalid host.");
        }

        if (portText.Length == 0 || !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new FormatException("Invalid port.");
        return (host, port);
    }

    private static byte[] DecodeKey(string value)
    {
        if (value.Length != 43 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new FormatException("BFT keys must use unpadded base64url encoding.");
        var base64 = value.Replace('-', '+').Replace('_', '/') + "=";
        byte[] result;
        try { result = Convert.FromBase64String(base64); }
        catch (FormatException ex) { throw new FormatException("Invalid BFT key encoding.", ex); }
        if (result.Length != 32 || EncodeKey(result) != value)
            throw new FormatException("BFT keys must be 32 bytes.");
        return result;
    }

    private static string EncodeKey(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string DecodeParameter(string value, int minimumBytes, int maximumBytes, string label)
    {
        if (value.Length > maximumBytes * 3) throw new FormatException($"{label} encoding is too long.");
        using var bytes = new MemoryStream(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '%')
            {
                if (i + 2 >= value.Length || !byte.TryParse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                    throw new FormatException("Invalid percent-encoded token.");
                bytes.WriteByte(b);
                i += 2;
            }
            else if (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~')
            {
                bytes.WriteByte((byte)c);
            }
            else
            {
                throw new FormatException("Token characters outside the unreserved set must be percent-encoded.");
            }
        }

        var tokenBytes = bytes.ToArray();
        if (tokenBytes.Length < minimumBytes || tokenBytes.Length > maximumBytes)
            throw new FormatException($"{label} length is outside the permitted UTF-8 byte range.");
        try { return StrictUtf8.GetString(tokenBytes); }
        catch (DecoderFallbackException ex) { throw new FormatException($"{label} is not valid UTF-8.", ex); }
    }

    private static string DecodeName(string value)
    {
        var name = DecodeParameter(value, 1, 256, "Name");
        if (!IsValidName(name)) throw new FormatException("Name must contain 1–64 Unicode characters and no control characters.");
        return name;
    }

    private static string EncodeName(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException("Name must contain 1–64 Unicode characters and no control characters.", nameof(name));
        return EncodeParameter(name);
    }

    private static bool IsValidName(string name)
    {
        var count = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || ++count > 64) return false;
        }
        return count > 0;
    }

    private static string EncodeParameter(string token)
    {
        var bytes = StrictUtf8.GetBytes(token);
        if (bytes.Length is < 1 or > 256)
            throw new ArgumentException("Token length must be 1–256 UTF-8 bytes.", nameof(token));
        var builder = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            if (b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~')
                builder.Append((char)b);
            else
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}
