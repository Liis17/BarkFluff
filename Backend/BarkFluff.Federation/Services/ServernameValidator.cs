using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace BarkFluff.Federation.Services;

// Анти-SSRF: единая точка проверки servername/endpoint перед ЛЮБЫМ исходящим запросом
// (docs/rearch/03-discovery.md, "Обязательная валидация servername и endpoint"). Manual-пиры —
// исключение из проверки диапазонов (осознанный сценарий "дружеские ноды в приватной сети"),
// но не из синтаксиса.
public class ServernameValidator
{
    // Синтаксис + punycode-нормализация к lowercase A-label. Применяется всегда, включая manual.
    public static bool TryNormalizeSyntax(string servername, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(servername))
            return false;

        string aLabel;
        try
        {
            aLabel = new IdnMapping().GetAscii(servername.Trim());
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

    // Схема эндпоинта: https/grpc всегда; http — только для manual-пиров.
    public static bool IsSchemeAllowed(string scheme, bool isManual)
    {
        var lower = scheme.ToLowerInvariant();
        if (lower is "https" or "grpc")
            return true;

        return isManual && lower == "http";
    }

    // DNS-резолв + отклонение приватных/зарезервированных диапазонов (кроме manual).
    // Возвращает IP для anti-rebinding пиннинга: соединяемся по НЕМУ, не повторным резолвом.
    public virtual async Task<IPAddress?> ResolveAndValidateAsync(string hostname, bool isManual, CancellationToken ct = default)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(hostname, ct);
        }
        catch (SocketException)
        {
            return null;
        }

        if (isManual)
            return addresses.FirstOrDefault();

        return addresses.FirstOrDefault(ip => !IsPrivateOrReserved(ip));
    }

    public static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        var bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            if (bytes[0] == 10) return true;                                   // 10.0.0.0/8
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return true;    // 172.16.0.0/12
            if (bytes[0] == 192 && bytes[1] == 168) return true;                // 192.168.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254) return true;                // 169.254.0.0/16
            if (bytes[0] == 0) return true;                                     // 0.0.0.0/8
            if (bytes[0] >= 224) return true;                                   // multicast/broadcast
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Loopback)) return true;
            if ((bytes[0] & 0xFE) == 0xFC) return true;                         // fc00::/7 ULA
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;      // fe80::/10 link-local
            if (bytes[0] == 0xFF) return true;                                  // ff00::/8 multicast
            return false;
        }

        return true; // неизвестное семейство — fail-closed
    }
}
