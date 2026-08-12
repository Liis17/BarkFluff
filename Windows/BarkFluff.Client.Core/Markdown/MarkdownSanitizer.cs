using System.Text.RegularExpressions;

namespace BarkFluff.Client.Core.Markdown;

/// <summary>
/// Правила безопасности HTML-подмножества. Перенесены без послаблений из
/// <c>MarkdownRenderer.kt</c> (Android) и <c>utils.js</c> (Web): ссылки — только
/// http/https/mailto, изображения — только http/https, размеры — 1..2048.
/// </summary>
internal static partial class MarkdownSanitizer
{
    private const int MaxImageSide = 2048;

    [GeneratedRegex("""([a-z][a-z0-9-]*)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+))""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlAttributeRegex { get; }

    [GeneratedRegex("^(https?://|mailto:)", RegexOptions.IgnoreCase)]
    private static partial Regex SafeLinkSchemeRegex { get; }

    [GeneratedRegex("^https?://", RegexOptions.IgnoreCase)]
    private static partial Regex SafeImageSchemeRegex { get; }

    public static IReadOnlyDictionary<string, string> HtmlAttributes(string raw)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HtmlAttributeRegex.Matches(raw))
        {
            var value = new[] { match.Groups[2], match.Groups[3], match.Groups[4] }
                .FirstOrDefault(group => group.Success && group.Value.Length > 0)?.Value ?? string.Empty;
            attributes[match.Groups[1].Value] = value;
        }

        return attributes;
    }

    public static bool IsSafeLinkUrl(string? url) => url is not null && SafeLinkSchemeRegex.IsMatch(url.Trim());

    public static bool IsSafeImageUrl(string? url) => url is not null && SafeImageSchemeRegex.IsMatch(url.Trim());

    /// <summary>Адрес без схемы считается http, как в <c>normalizeUrl</c> на Android.</summary>
    public static string NormalizeUrl(string url) =>
        url.Contains("://", StringComparison.Ordinal) || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? url
            : "http://" + url;

    public static int? ImageSide(string? raw) =>
        int.TryParse(raw, out var value) && value is >= 1 and <= MaxImageSide ? value : null;

    public static MarkdownAlignment? Alignment(string? value) => value?.ToLowerInvariant() switch
    {
        "center" => MarkdownAlignment.Center,
        "right" => MarkdownAlignment.End,
        "left" => MarkdownAlignment.Start,
        _ => null
    };
}
