namespace BarkFluff.Client.Core.Markdown;

/// <summary>
/// Разобранное сообщение. Слой без UI: разбор живёт здесь, потому что тем же парсером
/// пользуется <see cref="MarkdownText.Strip"/> для plain-text превью, а превью строятся
/// во вьюмоделях.
/// </summary>
public sealed record MarkdownDocument(IReadOnlyList<MarkdownBlock> Blocks)
{
    /// <summary>
    /// Сообщение без разметки: одна строка обычного текста без стилей и ссылок.
    /// Такие сообщения составляют основную массу ленты, и рендер отдаёт их простым
    /// <c>TextBlock</c> вместо дерева блоков.
    /// </summary>
    public bool TryGetPlainText(out string text)
    {
        text = string.Empty;
        if (Blocks is not [MarkdownTextGroup { Alignment: MarkdownAlignment.Start } group]
            || group.Lines is not [{ Kind: MarkdownLineKind.Paragraph } line])
        {
            return false;
        }

        if (line.Inlines.Any(inline => inline.Style is not MarkdownInlineStyle.None || inline.Link is not null))
        {
            return false;
        }

        text = string.Concat(line.Inlines.Select(inline => inline.Text));
        return true;
    }
}

public abstract record MarkdownBlock;

/// <summary>Подряд идущие строки обычного текста, заголовков, списков и разделителей.</summary>
public sealed record MarkdownTextGroup(IReadOnlyList<MarkdownLine> Lines, MarkdownAlignment Alignment) : MarkdownBlock;

/// <summary>Ограждённый блок кода. Info-string после <c>```</c> отбрасывается: подсветки нет ни в одном клиенте.</summary>
public sealed record MarkdownCodeBlock(string Code) : MarkdownBlock;

/// <summary>Подряд идущие строки цитаты.</summary>
public sealed record MarkdownQuote(IReadOnlyList<MarkdownLine> Lines) : MarkdownBlock;

public sealed record MarkdownTable(
    IReadOnlyList<MarkdownTableCell> Headers,
    IReadOnlyList<MarkdownAlignment> Alignments,
    IReadOnlyList<MarkdownTableRow> Rows) : MarkdownBlock;

public sealed record MarkdownTableRow(IReadOnlyList<MarkdownTableCell> Cells);

public sealed record MarkdownTableCell(IReadOnlyList<MarkdownInline> Inlines);

/// <summary>Изображение из HTML-подмножества. <c>Url</c> равен null, если адрес не прошёл allowlist.</summary>
public sealed record MarkdownImage(
    string? Url,
    string Alt,
    int? Width,
    int? Height,
    MarkdownAlignment Alignment,
    string? LinkUrl) : MarkdownBlock;

/// <summary>Строка внутри <see cref="MarkdownTextGroup"/> или <see cref="MarkdownQuote"/>.</summary>
public sealed record MarkdownLine(
    MarkdownLineKind Kind,
    IReadOnlyList<MarkdownInline> Inlines,
    int HeadingLevel = 0,
    string OrderedMarker = "");

/// <summary>Отрезок текста с наложенными стилями. Стили — флаги, как span'ы в Android.</summary>
public sealed record MarkdownInline(string Text, MarkdownInlineStyle Style, string? Link);

public enum MarkdownAlignment
{
    Start,
    Center,
    End
}

public enum MarkdownLineKind
{
    Paragraph,
    Heading,
    Bullet,
    Ordered,
    Rule
}

[Flags]
public enum MarkdownInlineStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strikethrough = 4,
    Code = 8,
    Small = 16
}
