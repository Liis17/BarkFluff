using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BarkFluff.Bft;

namespace BarkFluff.BftProxy;

internal enum ProxyMode
{
    Exit,
    Relay
}

internal sealed record ProxyEndpoint(string Host, int Port)
{
    public EndPoint ToListenEndPoint()
    {
        var address = Host switch
        {
            "*" or "+" => IPAddress.Any,
            _ when IPAddress.TryParse(Host, out var parsed) => parsed,
            _ => throw new InvalidOperationException("BFT_LISTEN must use an IP address or wildcard.")
        };
        return new IPEndPoint(address, Port);
    }

    public EndPoint ToConnectEndPoint() => IPAddress.TryParse(Host, out var address)
        ? new IPEndPoint(address, Port)
        : new DnsEndPoint(Host, Port);
}

internal sealed record ExitConfiguration(
    ProxyEndpoint Listen,
    ProxyEndpoint? RelayListen,
    byte[] StaticPrivateKey,
    string ClientKeysFile,
    IReadOnlyList<BftServerObfsKey> ClientKeys,
    IReadOnlyCollection<string> AccessTokens,
    IReadOnlySet<IPAddress> TrustedRelays,
    byte[]? RelayKey,
    string ProxyId,
    string OriginSecret,
    IReadOnlyDictionary<byte, Uri> Upstreams);

internal sealed record RelayConfiguration(
    ProxyEndpoint Listen,
    ProxyEndpoint NextHop,
    string RelayId,
    byte[] RelayKey,
    int MaxConnectionsPerIp = 64,
    int MaxConnections = 4096,
    TimeSpan? ConnectTimeout = null);

internal sealed record ProxyConfiguration(ProxyMode Mode, ExitConfiguration? Exit, RelayConfiguration? Relay)
{
    private static readonly (byte Id, string Name, string Variable)[] ServiceVariables =
    [
        (1, "beacon", "BFT_UPSTREAM_BEACON"),
        (2, "identity", "BFT_UPSTREAM_IDENTITY"),
        (3, "users", "BFT_UPSTREAM_USERS"),
        (4, "messages", "BFT_UPSTREAM_MESSAGES"),
        (5, "files", "BFT_UPSTREAM_FILES"),
        (6, "files-media", "BFT_UPSTREAM_FILES_MEDIA"),
        (7, "updates", "BFT_UPSTREAM_UPDATES"),
        (8, "onliner", "BFT_UPSTREAM_ONLINER"),
        (9, "fastauth", "BFT_UPSTREAM_FASTAUTH"),
        (10, "calls", "BFT_UPSTREAM_CALLS"),
        (11, "bots", "BFT_UPSTREAM_BOTS"),
        (12, "storage", "BFT_UPSTREAM_STORAGE")
    ];

    public static ProxyConfiguration FromEnvironment() => Parse(Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>()
        .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value, StringComparer.Ordinal));

    internal static ProxyConfiguration Parse(IReadOnlyDictionary<string, string?> values)
    {
        string Required(string name) => !string.IsNullOrWhiteSpace(values.GetValueOrDefault(name))
            ? values[name]!.Trim()
            : throw new InvalidOperationException($"{name} is required.");

        var mode = Required("BFT_MODE") switch
        {
            "Exit" => ProxyMode.Exit,
            "Relay" => ProxyMode.Relay,
            _ => throw new InvalidOperationException("BFT_MODE must be Exit or Relay.")
        };

        var listen = ParseEndpoint(Required("BFT_LISTEN"), "BFT_LISTEN", allowZero: false);
        if (mode == ProxyMode.Relay)
        {
            var relayId = Required("BFT_RELAY_ID");
            ValidateRelayId(relayId);
            return new ProxyConfiguration(mode, null, new RelayConfiguration(
                listen,
                ParseEndpoint(Required("BFT_NEXT_HOP"), "BFT_NEXT_HOP", allowZero: false),
                relayId,
                DecodeBase64UrlKey(Required("BFT_RELAY_KEY"), "BFT_RELAY_KEY")));
        }

        var staticPrivateKey = DecodeBase64UrlKey(Required("BFT_STATIC_PRIVATE_KEY"), "BFT_STATIC_PRIVATE_KEY");
        var keyFile = Path.GetFullPath(Required("BFT_CLIENT_KEYS_FILE"));
        var clientKeysBytes = File.ReadAllBytes(keyFile);
        IReadOnlyList<BftServerObfsKey> clientKeys;
        try { clientKeys = ClientKeyFile.Parse(clientKeysBytes); }
        finally { CryptographicOperations.ZeroMemory(clientKeysBytes); }

        var proxyId = Required("BFT_PROXY_ID");
        if (!Regex.IsMatch(proxyId, "^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("BFT_PROXY_ID must contain 1–64 ASCII letters, digits, dots, underscores, or hyphens.");
        if (proxyId.Length > 32)
            throw new InvalidOperationException("BFT_PROXY_ID must be at most 32 characters because the BFTv1 HELLO proxyId field is limited to 32 bytes.");
        var originSecret = Required("BFT_ORIGIN_SECRET");
        if (originSecret.Length < 64 || (originSecret.Length & 1) != 0 || !originSecret.All(Uri.IsHexDigit))
            throw new InvalidOperationException("BFT_ORIGIN_SECRET must be hexadecimal and at least 64 characters long.");
        originSecret = originSecret.ToLowerInvariant();

        var accessTokens = SplitCsv(values.GetValueOrDefault("BFT_ACCESS_TOKENS"));
        if (accessTokens.Any(token => System.Text.Encoding.UTF8.GetByteCount(token) is < 1 or > 256))
            throw new InvalidOperationException("BFT_ACCESS_TOKENS entries must be 1–256 UTF-8 bytes.");

        var relayListen = Optional(values, "BFT_RELAY_LISTEN") is { } relayListenText
            ? ParseEndpoint(relayListenText, "BFT_RELAY_LISTEN", allowZero: false)
            : null;
        var trustedRelays = SplitCsv(values.GetValueOrDefault("BFT_TRUSTED_RELAYS"))
            .Select(ParseIpAddress)
            .ToHashSet();
        byte[]? relayKey = null;
        if (relayListen is not null)
        {
            relayKey = DecodeBase64UrlKey(Required("BFT_RELAY_KEY"), "BFT_RELAY_KEY");
            if (trustedRelays.Count == 0)
                throw new InvalidOperationException("BFT_TRUSTED_RELAYS must contain at least one address when BFT_RELAY_LISTEN is set.");
        }

        var upstreams = new Dictionary<byte, Uri>();
        foreach (var (id, _, variable) in ServiceVariables)
        {
            if (Optional(values, variable) is not { } address) continue;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException($"{variable} must be an absolute HTTPS URL without credentials, query, or fragment.");
            upstreams.Add(id, uri);
        }

        return new ProxyConfiguration(mode, new ExitConfiguration(listen, relayListen, staticPrivateKey, keyFile,
            clientKeys, accessTokens, trustedRelays, relayKey, proxyId, originSecret, upstreams), null);
    }

    public static string ServiceName(byte serviceId) => ServiceVariables.FirstOrDefault(item => item.Id == serviceId).Name ?? "unknown";

    private static string? Optional(IReadOnlyDictionary<string, string?> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static IReadOnlyList<string> SplitCsv(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static ProxyEndpoint ParseEndpoint(string value, string name, bool allowZero)
    {
        string host;
        string portText;
        if (value.StartsWith('['))
        {
            var end = value.IndexOf(']');
            if (end < 2 || end + 1 >= value.Length || value[end + 1] != ':')
                throw new InvalidOperationException($"{name} must be host:port, with IPv6 in brackets.");
            host = value[1..end];
            if (!IPAddress.TryParse(host, out var ipv6) || ipv6.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                throw new InvalidOperationException($"{name} contains an invalid IPv6 address.");
            portText = value[(end + 2)..];
        }
        else
        {
            var separator = value.LastIndexOf(':');
            if (separator < 1 || separator != value.IndexOf(':'))
                throw new InvalidOperationException($"{name} must be host:port, with IPv6 in brackets.");
            host = value[..separator];
            portText = value[(separator + 1)..];
            if (!IPAddress.TryParse(host, out _) && host is not "*" and not "+" && !Uri.CheckHostName(host).Equals(UriHostNameType.Dns))
                throw new InvalidOperationException($"{name} contains an invalid host.");
        }
        if (!int.TryParse(portText, out var port) || port < (allowZero ? 0 : 1) || port > 65535)
            throw new InvalidOperationException($"{name} contains an invalid port.");
        return new ProxyEndpoint(host, port);
    }

    private static IPAddress ParseIpAddress(string text) => IPAddress.TryParse(text, out var address)
        ? address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address
        : throw new InvalidOperationException("BFT_TRUSTED_RELAYS contains an invalid IP address.");

    internal static byte[] DecodeBase64UrlKey(string value, string name)
    {
        if (value.Contains('=') || value.Length != 43 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new InvalidOperationException($"{name} must be a 32-byte base64url value without padding.");
        try
        {
            var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length != 32) throw new FormatException();
            return bytes;
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{name} must be a 32-byte base64url value without padding.", ex);
        }
    }

    internal static byte[] DecodeKeyFileValue(string value) => DecodeBase64UrlKey(value, "client obfuscation key");

    private static void ValidateRelayId(string relayId)
    {
        if (relayId.Length is < 1 or > 16 || relayId.Any(c => c is < (char)0x20 or > (char)0x7E))
            throw new InvalidOperationException("BFT_RELAY_ID must contain 1–16 printable ASCII characters.");
    }
}

internal static class ClientKeyFile
{
    public static IReadOnlyList<BftServerObfsKey> Parse(ReadOnlySpan<byte> contents)
    {
        var text = new System.Text.UTF8Encoding(false, true).GetString(contents);
        var result = new List<BftServerObfsKey>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var commentIndex = line.IndexOf('#');
            if (commentIndex >= 0) line = line[..commentIndex].TrimEnd();
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !Regex.IsMatch(parts[0], "^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Client key file contains an invalid line.");
            if (!labels.Add(parts[0])) throw new InvalidOperationException("Client key labels must be unique.");
            if (result.Count == 1024) throw new InvalidOperationException("Client key file may contain at most 1024 keys.");
            var key = ProxyConfiguration.DecodeKeyFileValue(parts[1]);
            result.Add(new BftServerObfsKey(parts[0], key));
        }
        return result;
    }
}
