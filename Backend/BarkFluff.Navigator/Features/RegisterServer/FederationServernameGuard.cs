using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace BarkFluff.Navigator.Features.RegisterServer;

// Минимальная анти-SSRF проверка для федеративной регистрации (docs/rearch/03-discovery.md,
// "Обязательная валидация servername и endpoint"). Осознанное дублирование
// BarkFluff.Federation.Services.ServernameValidator — Navigator публичен и не имеет общего кода
// с Federation. Упрощение относительно Federation: без anti-rebinding IP-пиннинга (HttpClient
// сам резолвит хост при фетче) — приемлемо, т.к. это только проверка перед регистрацией в каталоге,
// не привилегированный канал.
public static class FederationServernameGuard
{
    public static bool TryNormalize(string raw, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string aLabel;
        try
        {
            aLabel = new IdnMapping().GetAscii(raw.Trim());
        }
        catch (ArgumentException)
        {
            return false;
        }

        aLabel = aLabel.ToLowerInvariant();

        if (IPAddress.TryParse(aLabel, out _))
            return false;

        if (aLabel == "localhost")
            return false;

        if (Uri.CheckHostName(aLabel) != UriHostNameType.Dns)
            return false;

        normalized = aLabel;
        return true;
    }

    public static async Task<bool> ResolvesToPublicAddressAsync(string hostname, CancellationToken ct = default)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(hostname, ct);
        }
        catch (SocketException)
        {
            return false;
        }

        return addresses.Any(ip => !IsPrivateOrReserved(ip));
    }

    private static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        var bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            if (bytes[0] == 10) return true;
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            if (bytes[0] == 0) return true;
            if (bytes[0] >= 224) return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Loopback)) return true;
            if ((bytes[0] & 0xFE) == 0xFC) return true;
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;
            if (bytes[0] == 0xFF) return true;
            return false;
        }

        return true;
    }
}
