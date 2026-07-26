namespace BarkFluff.Files.Features.FetchFileStream;

/// <summary>
/// Стриминг содержимого файла ноде-партнёру (этап 3.2). Не MediatR: результат — поток,
/// а не сообщение, и держать его в pipeline-обвязке смысла нет.
/// </summary>
public class FetchFileStreamQuery
{
    public required Guid FileId { get; init; }

    /// <summary>Смещение первого байта, inclusive.</summary>
    public long RangeFrom { get; init; }

    /// <summary>Граница диапазона, exclusive. 0 вместе с <see cref="RangeFrom"/> = 0 — весь файл.</summary>
    public long RangeTo { get; init; }
}
