namespace BarkFluff.Files.Helpers;

/// <summary>
/// Разбор заголовка <c>Range</c> для fed-скачивания (этап 3.3).
/// </summary>
/// <remarks>
/// Нужен перемотке видео. Стандартный <c>File(..., enableRangeProcessing: true)</c> не подходит:
/// он требует seekable-поток, а у нас поток приходит чанками с чужой ноды.
///
/// Поддерживается один диапазон. Множественные (<c>multipart/byteranges</c>) намеренно не
/// поддерживаются: клиентам они не нужны, а реализация заметно сложнее.
/// </remarks>
public static class ByteRangeHeader
{
    /// <summary>Результат разбора: <see cref="From"/> inclusive, <see cref="To"/> exclusive.</summary>
    public readonly record struct Result(long From, long To)
    {
        public long Length => To - From;
    }

    public enum Status
    {
        /// <summary>Заголовка нет или он не распознан — отдаём файл целиком (200).</summary>
        NoRange,

        /// <summary>Диапазон корректен — 206 + Content-Range.</summary>
        Satisfiable,

        /// <summary>Диапазон вне файла — 416.</summary>
        Unsatisfiable,
    }

    /// <summary>
    /// Разобрать заголовок относительно известного размера файла.
    /// Нераспознанный синтаксис трактуется как отсутствие Range (отдаём целиком) — так
    /// рекомендует RFC 9110: сервер вправе игнорировать некорректный Range.
    /// </summary>
    public static Status TryParse(string? headerValue, long totalSize, out Result range)
    {
        range = default;

        if (string.IsNullOrWhiteSpace(headerValue) || totalSize <= 0)
        {
            return Status.NoRange;
        }

        var value = headerValue.Trim();

        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return Status.NoRange;
        }

        var spec = value["bytes=".Length..].Trim();

        // Множественные диапазоны не поддерживаем — отдаём целиком, это законное поведение.
        if (spec.Contains(','))
        {
            return Status.NoRange;
        }

        var dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return Status.NoRange;
        }

        var fromPart = spec[..dash].Trim();
        var toPart = spec[(dash + 1)..].Trim();

        // Суффиксная форма "bytes=-N" — последние N байт.
        if (fromPart.Length == 0)
        {
            if (!long.TryParse(toPart, out var suffixLength) || suffixLength <= 0)
            {
                return Status.NoRange;
            }

            var from = Math.Max(0, totalSize - suffixLength);
            range = new Result(from, totalSize);
            return Status.Satisfiable;
        }

        if (!long.TryParse(fromPart, out var start) || start < 0)
        {
            return Status.NoRange;
        }

        if (start >= totalSize)
        {
            return Status.Unsatisfiable;
        }

        // "bytes=a-" — от смещения до конца.
        if (toPart.Length == 0)
        {
            range = new Result(start, totalSize);
            return Status.Satisfiable;
        }

        if (!long.TryParse(toPart, out var endInclusive) || endInclusive < start)
        {
            return Status.NoRange;
        }

        // Верхняя граница за пределами файла обрезается по нему (RFC 9110).
        var endExclusive = Math.Min(endInclusive + 1, totalSize);

        range = new Result(start, endExclusive);
        return Status.Satisfiable;
    }
}
