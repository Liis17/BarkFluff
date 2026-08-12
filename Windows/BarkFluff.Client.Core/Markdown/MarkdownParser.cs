using System.Text;
using System.Text.RegularExpressions;

namespace BarkFluff.Client.Core.Markdown;

/// <summary>
/// Разбор диалекта markdown, принятого в мессенджере. Правила и порядок их применения
/// перенесены из Android-рендера (<c>MarkdownRenderer.kt</c>) — он же эталон для веб-клиента,
/// поэтому одно и то же сообщение выглядит одинаково на всех платформах.
///
/// Поддержка: заголовки, списки, цитаты, разделители, блоки кода, inline
/// (<c>**bold**</c>, <c>*italic*</c>, <c>~~strike~~</c>, <c>`code`</c>, <c>[текст](url)</c>),
/// автолинковка «голых» адресов, GFM-таблицы и HTML-подмножество
/// (p/h1..h6 с align, strong, sub, a[href], img[src, alt, width, height]).
/// Вложенные списки и escape-последовательности не поддерживаются — как и на других клиентах.
/// </summary>
public static partial class MarkdownParser
{
    /// <summary>
    /// Домены верхнего уровня для автолинковки адресов без схемы. Android опирается на
    /// <c>Patterns.WEB_URL</c> с полным списком IANA; в .NET такого списка нет, а линковать
    /// любую точку в тексте нельзя — «file.txt» ссылкой быть не должен.
    /// </summary>
    private const string TopLevelDomains =
        "com|org|net|int|edu|gov|mil|info|biz|pro|name|xyz|online|site|tech|store|shop|cloud|app|dev|ai|io|co|me|tv|cc|" +
        "su|ru|by|kz|ua|uk|de|fr|it|es|pl|nl|se|no|fi|cz|tr|cn|jp|kr|in|br|ca|au|eu|" +
        "top|live|news|blog|art|fun|link|click|space|website|digital|agency|team|games|studio|design|software|group|media|world|life|zone|host|press|wiki|guru|expert";

    [GeneratedRegex(@"^(#{1,6})\s+")]
    private static partial Regex HeadingRegex { get; }

    [GeneratedRegex(@"^\s*(\d+)\.\s+(.*)")]
    private static partial Regex OrderedRegex { get; }

    [GeneratedRegex(@"^\s*[-*+]\s+(.*)")]
    private static partial Regex UnorderedRegex { get; }

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex InlineCodeRegex { get; }

    // Содержимое допускает звёздочки — иначе «**жирный с *курсивом* внутри**» разбирается
    // как курсив со сломанными границами. Правило взято из веб-клиента, где оно корректнее
    // андроидного `[^*]+?`.
    [GeneratedRegex(@"\*\*([\s\S]+?)\*\*")]
    private static partial Regex BoldStarsRegex { get; }

    [GeneratedRegex(@"(?<!\w)__([^_]+?)__(?!\w)")]
    private static partial Regex BoldUnderscoresRegex { get; }

    [GeneratedRegex("~~([^~]+?)~~")]
    private static partial Regex StrikeRegex { get; }

    [GeneratedRegex(@"\*([^*]+?)\*")]
    private static partial Regex ItalicStarRegex { get; }

    [GeneratedRegex(@"(?<!\w)_([^_]+?)_(?!\w)")]
    private static partial Regex ItalicUnderscoreRegex { get; }

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    internal static partial Regex LinkRegex { get; }

    [GeneratedRegex("^:?-+:?$")]
    private static partial Regex TableDelimiterCellRegex { get; }

    [GeneratedRegex("""^\s*<p(?:\s+align\s*=\s*(?:"(left|center|right)"|'(left|center|right)'|(left|center|right)))?\s*>\s*$""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlParagraphOpenRegex { get; }

    [GeneratedRegex(@"^\s*</p>\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlParagraphCloseRegex { get; }

    [GeneratedRegex("""^\s*<h([1-6])(?:\s+align\s*=\s*(?:"(left|center|right)"|'(left|center|right)'|(left|center|right)))?\s*>(.*?)</h([1-6])>\s*$""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlHeadingRegex { get; }

    [GeneratedRegex(@"^\s*<img\s+([^>]+?)/?>\s*$", RegexOptions.IgnoreCase)]
    internal static partial Regex HtmlImageRegex { get; }

    [GeneratedRegex(@"^\s*<a\s+([^>]+)>\s*(<img\s+[^>]+/?>)\s*</a>\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImageLinkRegex { get; }

    [GeneratedRegex("<strong>(.*?)</strong>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlStrongRegex { get; }

    [GeneratedRegex("<sub>(.*?)</sub>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlSubRegex { get; }

    [GeneratedRegex(@"<a\s+([^>]*)>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlLinkRegex { get; }

    [GeneratedRegex($"""(?<![\w@.-])(?:(?:https?://|www\.)[^\s<>"']*[^\s<>"'.,!?;:)\]]|(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(?:{TopLevelDomains})(?![a-z0-9])(?:[/?#][^\s<>"']*[^\s<>"'.,!?;:)\]])?)""", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrlRegex { get; }

    public static MarkdownDocument Parse(string? source)
    {
        var text = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var blocks = new List<MarkdownBlock>();
        foreach (var chunk in SplitTables(text))
        {
            switch (chunk)
            {
                case BlockChunk block:
                    blocks.Add(block.Block);
                    break;
                case TextChunk textChunk:
                    foreach (var htmlChunk in SplitHtml(textChunk))
                    {
                        switch (htmlChunk)
                        {
                            case BlockChunk block:
                                blocks.Add(block.Block);
                                break;
                            case TextChunk plain:
                                AppendTextBlocks(blocks, plain.Source, plain.Alignment);
                                break;
                        }
                    }

                    break;
            }
        }

        return new MarkdownDocument(blocks);
    }

    /// <summary>Таблицы вырезаются первыми: им нужна собственная сетка, а не поток текста.</summary>
    private static List<RawChunk> SplitTables(string source)
    {
        var lines = source.Split('\n');
        var chunks = new List<RawChunk>();
        var textStart = 0;
        var index = 0;
        var inCodeBlock = false;

        while (index < lines.Length)
        {
            if (lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                index++;
                continue;
            }

            var table = inCodeBlock ? null : TryParseTable(lines, index);
            if (table is null)
            {
                index++;
                continue;
            }

            if (index > textStart)
            {
                chunks.Add(new TextChunk(string.Join('\n', lines[textStart..index])));
            }

            chunks.Add(new BlockChunk(table.Table));
            index = table.End;
            textStart = index;
        }

        if (textStart < lines.Length)
        {
            chunks.Add(new TextChunk(string.Join('\n', lines[textStart..])));
        }

        return chunks;
    }

    /// <summary>HTML-подмножество: выравнивание абзацев, заголовки и изображения-блоки.</summary>
    private static List<RawChunk> SplitHtml(TextChunk chunk)
    {
        var chunks = new List<RawChunk>();
        var text = new StringBuilder();
        var alignment = chunk.Alignment;
        var inCodeBlock = false;

        void FlushText()
        {
            if (text.Length > 0)
            {
                chunks.Add(new TextChunk(text.ToString().TrimEnd('\n'), alignment));
                text.Clear();
            }
        }

        void AppendLine(string line)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(line);
        }

        foreach (var line in chunk.Source.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                AppendLine(line);
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                AppendLine(line);
                continue;
            }

            var paragraph = HtmlParagraphOpenRegex.Match(line);
            if (paragraph.Success)
            {
                FlushText();
                alignment = MatchedAlignment(paragraph, 1, 3) ?? MarkdownAlignment.Start;
                continue;
            }

            if (HtmlParagraphCloseRegex.IsMatch(line))
            {
                FlushText();
                alignment = chunk.Alignment;
                continue;
            }

            var heading = HtmlHeadingRegex.Match(line);
            if (heading.Success && heading.Groups[1].Value == heading.Groups[6].Value)
            {
                FlushText();
                var headingAlignment = MatchedAlignment(heading, 2, 4) ?? alignment;
                var level = int.Parse(heading.Groups[1].Value);
                chunks.Add(new TextChunk($"{new string('#', level)} {heading.Groups[5].Value}", headingAlignment));
                continue;
            }

            var image = TryParseHtmlImage(line, alignment);
            if (image is not null)
            {
                FlushText();
                chunks.Add(new BlockChunk(image));
                continue;
            }

            AppendLine(line);
        }

        FlushText();
        return chunks;
    }

    /// <summary>Построчный разбор: код и цитаты становятся отдельными блоками, остальное копится в группу строк.</summary>
    private static void AppendTextBlocks(List<MarkdownBlock> blocks, string source, MarkdownAlignment alignment)
    {
        var lines = source.Split('\n');
        var pending = new List<MarkdownLine>();

        void FlushLines()
        {
            if (pending.Count > 0)
            {
                blocks.Add(new MarkdownTextGroup(pending.ToArray(), alignment));
                pending.Clear();
            }
        }

        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var code = new List<string>();
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[index]);
                    index++;
                }

                if (index < lines.Length)
                {
                    index++;
                }

                FlushLines();
                blocks.Add(new MarkdownCodeBlock(string.Join('\n', code)));
                continue;
            }

            if (IsRule(line))
            {
                pending.Add(new MarkdownLine(MarkdownLineKind.Rule, []));
                index++;
                continue;
            }

            var heading = HeadingRegex.Match(line);
            if (heading.Success)
            {
                pending.Add(new MarkdownLine(
                    MarkdownLineKind.Heading,
                    ParseInline(line[heading.Groups[1].Length..].Trim()),
                    heading.Groups[1].Length));
                index++;
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                var quote = new List<MarkdownLine>();
                while (index < lines.Length && lines[index].TrimStart().StartsWith('>'))
                {
                    var content = lines[index].TrimStart()[1..].TrimStart();
                    quote.Add(new MarkdownLine(MarkdownLineKind.Paragraph, ParseInline(content)));
                    index++;
                }

                FlushLines();
                blocks.Add(new MarkdownQuote(quote));
                continue;
            }

            var ordered = OrderedRegex.Match(line);
            if (ordered.Success)
            {
                pending.Add(new MarkdownLine(
                    MarkdownLineKind.Ordered,
                    ParseInline(ordered.Groups[2].Value),
                    OrderedMarker: ordered.Groups[1].Value));
                index++;
                continue;
            }

            var unordered = UnorderedRegex.Match(line);
            if (unordered.Success)
            {
                pending.Add(new MarkdownLine(MarkdownLineKind.Bullet, ParseInline(unordered.Groups[1].Value)));
                index++;
                continue;
            }

            pending.Add(new MarkdownLine(MarkdownLineKind.Paragraph, ParseInline(line)));
            index++;
        }

        FlushLines();
    }

    private static ParsedTable? TryParseTable(string[] lines, int start)
    {
        if (start + 1 >= lines.Length || !lines[start].Contains('|'))
        {
            return null;
        }

        var header = SplitTableRow(lines[start]);
        var delimiter = SplitTableRow(lines[start + 1]);
        if (header.Count == 0 || header.Count != delimiter.Count
            || delimiter.Any(cell => !TableDelimiterCellRegex.IsMatch(cell.Trim())))
        {
            return null;
        }

        var alignments = delimiter.Select(cell =>
        {
            var value = cell.Trim();
            return value switch
            {
                _ when value.StartsWith(':') && value.EndsWith(':') => MarkdownAlignment.Center,
                _ when value.EndsWith(':') => MarkdownAlignment.End,
                _ => MarkdownAlignment.Start
            };
        }).ToArray();

        var rows = new List<MarkdownTableRow>();
        var end = start + 2;
        while (end < lines.Length && lines[end].Contains('|'))
        {
            var cells = SplitTableRow(lines[end]);
            rows.Add(new MarkdownTableRow(Enumerable.Range(0, header.Count)
                .Select(column => new MarkdownTableCell(ParseInline(column < cells.Count ? cells[column] : string.Empty)))
                .ToArray()));
            end++;
        }

        var headers = header.Select(cell => new MarkdownTableCell(ParseInline(cell))).ToArray();
        return new ParsedTable(new MarkdownTable(headers, alignments, rows), end);
    }

    /// <summary>Внешние <c>|</c> необязательны, экранированные и стоящие внутри кода — не границы ячеек.</summary>
    private static List<string> SplitTableRow(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var inCode = false;
        var index = 0;

        while (index < line.Length)
        {
            var ch = line[index];
            if (ch == '\\' && index + 1 < line.Length && line[index + 1] == '|')
            {
                current.Append('|');
                index++;
            }
            else if (ch == '`')
            {
                inCode = !inCode;
                current.Append(ch);
            }
            else if (ch == '|' && !inCode)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }

            index++;
        }

        cells.Add(current.ToString().Trim());

        if (line.TrimStart().StartsWith('|') && cells.Count > 0 && cells[0].Length == 0)
        {
            cells.RemoveAt(0);
        }

        if (line.TrimEnd().EndsWith('|') && cells.Count > 0 && cells[^1].Length == 0)
        {
            cells.RemoveAt(cells.Count - 1);
        }

        return cells;
    }

    private static MarkdownImage? TryParseHtmlImage(string line, MarkdownAlignment alignment)
    {
        var imageLine = line;
        string? linkUrl = null;

        var wrapped = HtmlImageLinkRegex.Match(line);
        if (wrapped.Success)
        {
            var href = MarkdownSanitizer.HtmlAttributes(wrapped.Groups[1].Value).GetValueOrDefault("href");
            linkUrl = MarkdownSanitizer.IsSafeLinkUrl(href) ? href!.Trim() : null;
            imageLine = wrapped.Groups[2].Value;
        }

        var image = HtmlImageRegex.Match(imageLine);
        if (!image.Success)
        {
            return null;
        }

        var attributes = MarkdownSanitizer.HtmlAttributes(image.Groups[1].Value);
        if (!attributes.TryGetValue("src", out var src))
        {
            return null;
        }

        return new MarkdownImage(
            MarkdownSanitizer.IsSafeImageUrl(src) ? src.Trim() : null,
            attributes.GetValueOrDefault("alt", string.Empty),
            MarkdownSanitizer.ImageSide(attributes.GetValueOrDefault("width")),
            MarkdownSanitizer.ImageSide(attributes.GetValueOrDefault("height")),
            alignment,
            linkUrl);
    }

    private static IReadOnlyList<MarkdownInline> ParseInline(string text)
    {
        var builder = new MarkdownInlineBuilder();
        var last = 0;

        // Inline-код защищается первым: внутри него разметка не интерпретируется.
        foreach (Match match in InlineCodeRegex.Matches(text))
        {
            if (match.Index > last)
            {
                builder.Append(ParseInlineWithoutCode(text[last..match.Index]));
            }

            builder.Append(match.Groups[1].Value, MarkdownInlineStyle.Code);
            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            builder.Append(ParseInlineWithoutCode(text[last..]));
        }

        builder.LinkifyBareUrls(BareUrlRegex);
        return builder.Build();
    }

    /// <summary>Порядок правил повторяет Android: html → жирный → зачёркнутый → курсив → ссылки.</summary>
    private static MarkdownInlineBuilder ParseInlineWithoutCode(string text)
    {
        var builder = new MarkdownInlineBuilder();
        builder.Append(text);
        ApplyHtmlLink(builder);
        ApplyWrap(builder, HtmlStrongRegex, MarkdownInlineStyle.Bold);
        ApplyWrap(builder, HtmlSubRegex, MarkdownInlineStyle.Small);
        ApplyWrap(builder, BoldStarsRegex, MarkdownInlineStyle.Bold);
        ApplyWrap(builder, BoldUnderscoresRegex, MarkdownInlineStyle.Bold);
        ApplyWrap(builder, StrikeRegex, MarkdownInlineStyle.Strikethrough);
        ApplyWrap(builder, ItalicStarRegex, MarkdownInlineStyle.Italic);
        ApplyWrap(builder, ItalicUnderscoreRegex, MarkdownInlineStyle.Italic);
        ApplyLink(builder);
        return builder;
    }

    private static void ApplyWrap(MarkdownInlineBuilder builder, Regex regex, MarkdownInlineStyle style)
    {
        var from = 0;
        while (true)
        {
            var match = regex.Match(builder.Text, from);
            if (!match.Success)
            {
                break;
            }

            var content = match.Groups[1];
            builder.Unwrap(match.Index, match.Length, content.Index, content.Length);
            builder.AddSpan(match.Index, match.Index + content.Length, style);
            from = match.Index + content.Length;
        }
    }

    private static void ApplyLink(MarkdownInlineBuilder builder)
    {
        var from = 0;
        while (true)
        {
            var match = LinkRegex.Match(builder.Text, from);
            if (!match.Success)
            {
                break;
            }

            var label = match.Groups[1];
            var url = match.Groups[2].Value;
            builder.Unwrap(match.Index, match.Length, label.Index, label.Length);
            builder.AddSpan(match.Index, match.Index + label.Length, MarkdownInlineStyle.None, MarkdownSanitizer.NormalizeUrl(url));
            from = match.Index + label.Length;
        }
    }

    private static void ApplyHtmlLink(MarkdownInlineBuilder builder)
    {
        var from = 0;
        while (true)
        {
            var match = HtmlLinkRegex.Match(builder.Text, from);
            if (!match.Success)
            {
                break;
            }

            var label = match.Groups[2];
            var url = MarkdownSanitizer.HtmlAttributes(match.Groups[1].Value).GetValueOrDefault("href");
            var labelLength = label.Length;
            builder.Unwrap(match.Index, match.Length, label.Index, labelLength);
            if (MarkdownSanitizer.IsSafeLinkUrl(url))
            {
                builder.AddSpan(match.Index, match.Index + labelLength, MarkdownInlineStyle.None, url!.Trim());
            }

            from = match.Index + labelLength;
        }
    }

    private static MarkdownAlignment? MatchedAlignment(Match match, int firstGroup, int lastGroup)
    {
        for (var group = firstGroup; group <= lastGroup; group++)
        {
            if (match.Groups[group].Success && match.Groups[group].Value.Length > 0)
            {
                return MarkdownSanitizer.Alignment(match.Groups[group].Value);
            }
        }

        return null;
    }

    internal static bool IsRule(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3
            && (trimmed.All(ch => ch == '-') || trimmed.All(ch => ch == '*') || trimmed.All(ch => ch == '_'));
    }

    private abstract record RawChunk;

    private sealed record TextChunk(string Source, MarkdownAlignment Alignment = MarkdownAlignment.Start) : RawChunk;

    private sealed record BlockChunk(MarkdownBlock Block) : RawChunk;

    private sealed record ParsedTable(MarkdownTable Table, int End);
}
