using System.Text;
using System.Text.RegularExpressions;

namespace BarkFluff.Client.Core.Markdown;

/// <summary>
/// Аналог <c>SpannableStringBuilder</c> из Android-рендера: текст правится на месте
/// (маркеры разметки удаляются), а стили держатся диапазонами и переезжают вместе с текстом.
/// Без этого пришлось бы писать полноценный inline-парсер вместо порта тех же правил.
/// </summary>
internal sealed class MarkdownInlineBuilder
{
    private sealed class StyleSpan
    {
        public int Start;
        public int End;
        public MarkdownInlineStyle Style;
        public string? Link;
    }

    private readonly StringBuilder _text = new();
    private readonly List<StyleSpan> _spans = [];

    public string Text => _text.ToString();

    public void Append(string text) => _text.Append(text);

    /// <summary>Присоединяет разобранный кусок вместе с его стилями, сдвигая их диапазоны.</summary>
    public void Append(MarkdownInlineBuilder other)
    {
        var offset = _text.Length;
        _text.Append(other._text);
        foreach (var span in other._spans)
        {
            _spans.Add(new StyleSpan
            {
                Start = span.Start + offset,
                End = span.End + offset,
                Style = span.Style,
                Link = span.Link
            });
        }
    }

    public void Append(string text, MarkdownInlineStyle style, string? link = null)
    {
        var start = _text.Length;
        _text.Append(text);
        AddSpan(start, _text.Length, style, link);
    }

    public void AddSpan(int start, int end, MarkdownInlineStyle style, string? link = null)
    {
        if (end > start)
        {
            _spans.Add(new StyleSpan { Start = start, End = end, Style = style, Link = link });
        }
    }

    /// <summary>
    /// Убирает обрамление разметки, оставляя содержимое: диапазоны внутри содержимого
    /// сохраняются со сдвигом, всё, что правее, — сдвигается на разницу длин.
    /// </summary>
    public void Unwrap(int matchStart, int matchLength, int contentStart, int contentLength)
    {
        var content = _text.ToString(contentStart, contentLength);
        _text.Remove(matchStart, matchLength).Insert(matchStart, content);

        var matchEnd = matchStart + matchLength;
        var contentEnd = contentStart + contentLength;
        var shift = contentStart - matchStart;
        var delta = contentLength - matchLength;

        foreach (var span in _spans)
        {
            span.Start = AdjustPosition(span.Start);
            span.End = AdjustPosition(span.End);
        }

        _spans.RemoveAll(span => span.End <= span.Start);

        int AdjustPosition(int position)
        {
            if (position <= matchStart)
            {
                return position;
            }

            if (position >= matchEnd)
            {
                return position + delta;
            }

            return position < contentStart
                ? matchStart
                : Math.Min(position, contentEnd) - shift;
        }
    }

    /// <summary>Линкует «голые» адреса, не трогая участки, где ссылка уже проставлена.</summary>
    public void LinkifyBareUrls(Regex pattern)
    {
        var linked = _spans.Where(span => span.Link is not null).Select(span => (span.Start, span.End)).ToArray();
        foreach (Match match in pattern.Matches(_text.ToString()))
        {
            var start = match.Index;
            var end = match.Index + match.Length;
            if (linked.Any(range => start < range.End && end > range.Start))
            {
                continue;
            }

            AddSpan(start, end, MarkdownInlineStyle.None, MarkdownSanitizer.NormalizeUrl(match.Value));
        }
    }

    /// <summary>Сводит пересекающиеся диапазоны в плоский список отрезков с итоговым стилем.</summary>
    public IReadOnlyList<MarkdownInline> Build()
    {
        var text = _text.ToString();
        if (text.Length == 0)
        {
            return [];
        }

        var boundaries = new SortedSet<int> { 0, text.Length };
        foreach (var span in _spans)
        {
            boundaries.Add(Math.Clamp(span.Start, 0, text.Length));
            boundaries.Add(Math.Clamp(span.End, 0, text.Length));
        }

        var points = boundaries.ToArray();
        var result = new List<MarkdownInline>();
        for (var index = 0; index + 1 < points.Length; index++)
        {
            var start = points[index];
            var end = points[index + 1];
            var style = MarkdownInlineStyle.None;
            string? link = null;
            foreach (var span in _spans.Where(span => span.Start <= start && span.End >= end))
            {
                style |= span.Style;
                link ??= span.Link;
            }

            var fragment = text[start..end];
            if (result.Count > 0 && result[^1].Style == style && result[^1].Link == link)
            {
                result[^1] = result[^1] with { Text = result[^1].Text + fragment };
            }
            else
            {
                result.Add(new MarkdownInline(fragment, style, link));
            }
        }

        return result;
    }
}
