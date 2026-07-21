using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Features.Federation;

// Общая валидация входящих fed-событий (docs/rearch/05-chat-replication.md, «Валидация импортируемых событий»).
// Используется ImportFederatedChat (2.3), ImportFederatedMessage (2.3), ApplyFederatedEdit/Delete (2.4).
//
// Бросает typed BaseGrpcException: валидационные → permanent (Federation мапит на REJECTED),
// «чат неизвестен» → NotFound → Federation мапит на RETRY (catch-up 2.6).
public static class FederationImportValidator
{
    public const int MaxTextLength = 4096;
    public const int MaxAttachmentsPerMessage = 10;
    public const long MaxFileBytes = 536_870_912L; // 512 МБ — Files upload limit

    // 5 минут — то же окно, что у подписи S2S-запроса (docs/rearch/02-trust-and-certs.md).
    public const int TimestampFutureWindowSeconds = 5 * 60;

    /// <summary>
    /// Отклонить метку из будущего. События LWW с будущей меткой «вечно побеждают» — обязательный clamp.
    /// </summary>
    public static DateTime ClampOriginTs(long originTsMs)
    {
        if (originTsMs <= 0)
            throw new TimestampInFutureException();

        var originTs = DateTimeOffset.FromUnixTimeMilliseconds(originTsMs).UtcDateTime;
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        if (originTs > now.AddSeconds(TimestampFutureWindowSeconds))
            throw new TimestampInFutureException();

        return originTs;
    }

    public static void ValidateText(string? text)
    {
        if (text is { Length: > MaxTextLength })
            throw new MessageTextTooLongException();
    }

    public static void ValidateAttachmentCount(int count)
    {
        if (count > MaxAttachmentsPerMessage)
            throw new TooManyAttachmentsException();
    }
}
