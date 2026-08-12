using System.Text.RegularExpressions;

namespace BarkFluff.Client.Core.Markdown;

/// <summary>
/// Приведение сообщения к чистому тексту для превью — порт <c>MarkdownRenderer.strip</c>.
/// Нужен везде, где сообщение показывается строкой: список чатов, цитата ответа,
/// закреп, подсказка композера.
/// </summary>
public static partial class MarkdownText
{
    [GeneratedRegex(@"^#{1,6}\s*")]
    private static partial Regex HeadingPrefixRegex { get; }

    [GeneratedRegex(@"^>\s*")]
    private static partial Regex QuotePrefixRegex { get; }

    [GeneratedRegex(@"^[-*+]\s+")]
    private static partial Regex BulletPrefixRegex { get; }

    [GeneratedRegex(@"^\d+\.\s+")]
    private static partial Regex OrderedPrefixRegex { get; }

    [GeneratedRegex("</?[a-z][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagRegex { get; }

    [GeneratedRegex(@"(\*\*|__|~~|`|\*|_)")]
    private static partial Regex InlineMarkerRegex { get; }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex { get; }

    public static string Strip(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(raw =>
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || MarkdownParser.IsRule(line))
            {
                return string.Empty;
            }

            line = HeadingPrefixRegex.Replace(line, string.Empty);
            line = QuotePrefixRegex.Replace(line, string.Empty);
            line = BulletPrefixRegex.Replace(line, string.Empty);
            return OrderedPrefixRegex.Replace(line, string.Empty);
        });

        var joined = string.Join(' ', lines);
        joined = MarkdownParser.LinkRegex.Replace(joined, "$1");
        joined = MarkdownParser.HtmlImageRegex.Replace(joined, match =>
            MarkdownSanitizer.HtmlAttributes(match.Groups[1].Value).GetValueOrDefault("alt", string.Empty));
        joined = HtmlTagRegex.Replace(joined, string.Empty);
        joined = InlineMarkerRegex.Replace(joined, string.Empty);
        return WhitespaceRegex.Replace(joined, " ").Trim();
    }
}
