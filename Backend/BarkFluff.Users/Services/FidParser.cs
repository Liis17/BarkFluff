using System.Globalization;
using System.Net;

namespace BarkFluff.Users.Services;

// Разбор федеративного адреса @username:servername (этап 2.1, docs/rearch/01-addressing-identity.md).
// Серверная часть нормализуется к punycode A-label lowercase через IdnMapping — тот же подход, что в
// Federation.ServernameValidator (docs/rearch/03-discovery.md). Анти-SSRF/DNS-резолв здесь НЕ делается:
// для сетевых запросов всё равно идём через Federation.ResolveRemoteUser, который применит полный
// ServernameValidator перед походом в сеть.
public static class FidParser
{
    public sealed record Fid(
        bool IsLocal,
        string Username,
        string? ServerName);

    /// <summary>
    /// Разобрать FID. Возвращает false при невалидном формате.
    /// FID без servername (или совпадающий с ownServerName) трактуется как локальный пользователь.
    /// </summary>
    public static bool TryParse(string? raw, string? ownServerName, out Fid? fid)
    {
        fid = null;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('@'))
            trimmed = trimmed[1..];

        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex < 0)
        {
            // Нет servername — локальный пользователь.
            return TryLocal(trimmed, out fid);
        }

        var username = trimmed[..colonIndex];
        var servername = trimmed[(colonIndex + 1)..];

        if (!UsernameFormatValidator.IsValid(username))
            return false;

        if (!TryNormalizeServerName(servername, out var normalized))
            return false;

        // Совпадающий с ownServerName — локальный пользователь (сравнение после канонизации).
        if (!string.IsNullOrWhiteSpace(ownServerName)
            && TryNormalizeServerName(ownServerName, out var ownNormalized)
            && string.Equals(ownNormalized, normalized, StringComparison.Ordinal))
        {
            fid = new Fid(IsLocal: true, Username: username, ServerName: null);
            return true;
        }

        fid = new Fid(IsLocal: false, Username: username, ServerName: normalized);
        return true;
    }

    /// <summary>
    /// Содержится ли в строке FID-паттерн (для ветки резолва в SearchUsers — единичный результат вместо trigram).
    /// </summary>
    public static bool LooksLikeFid(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var trimmed = query.Trim();
        if (trimmed.StartsWith('@'))
            trimmed = trimmed[1..];

        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex <= 0 || colonIndex == trimmed.Length - 1)
            return false;

        return true;
    }

    /// <summary>
    /// Punycode A-label lowercase (для согласованности с KnownServer.ServerName в Federation).
    /// </summary>
    public static bool TryNormalizeServerName(string servername, out string normalized)
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

    private static bool TryLocal(string username, out Fid? fid)
    {
        fid = null;

        if (!UsernameFormatValidator.IsValid(username))
            return false;

        fid = new Fid(IsLocal: true, Username: username, ServerName: null);
        return true;
    }
}
