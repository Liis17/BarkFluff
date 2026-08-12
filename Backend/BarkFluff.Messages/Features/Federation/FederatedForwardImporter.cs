using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Features.Federation;

/// <summary>
/// Валидация и импорт снапшота пересланных сообщений fed-сообщения.
/// </summary>
/// <remarks>
/// Те же правила доверия, что у <see cref="FederatedAttachmentImporter"/>: снапшот приходит с чужой
/// ноды, всё непрошедшее отклоняется <b>permanent</b> (REJECTED), а не RETRY.
/// </remarks>
public static class FederatedForwardImporter
{
    /// <summary>Столько же, сколько разрешено переслать локально (SendMessageCommandHandler).</summary>
    public const int MaxForwards = 20;

    public const int MaxAuthorNameLength = 255;

    /// <summary>
    /// Собрать вложения-пересылки из снапшота. Возвращает пустой список, если пересылок нет.
    /// </summary>
    public static List<Domain.MessageAttachment> Import(IReadOnlyCollection<FederatedForwardFlat> forwards)
    {
        if (forwards.Count == 0)
        {
            return [];
        }

        if (forwards.Count > MaxForwards)
        {
            throw new FederatedForwardInvalidException();
        }

        var imported = new List<Domain.MessageAttachment>(forwards.Count);

        foreach (var forward in forwards)
        {
            if (forward.AuthorName.Length > MaxAuthorNameLength)
            {
                throw new FederatedForwardInvalidException();
            }

            if (forward.Text.Length > FederationImportValidator.MaxTextLength)
            {
                throw new FederatedForwardInvalidException();
            }

            if (forward.Attachments.Count > FederatedForwardAttachmentLimit)
            {
                throw new FederatedForwardInvalidException();
            }

            imported.Add(new Domain.MessageAttachment
            {
                Type = Domain.MessageAttachmentType.ForwardedMessage,
                FileId = string.Empty,
                ForwardedAuthorName = forward.AuthorName,
                ForwardedText = forward.Text,
                // ID оригинала не передаётся: он локален для origin-ноды и у нас никуда не ведёт.
                ForwardedOriginalMessageId = null,
                ForwardedOriginalChatId = null,
                ForwardedOriginalSenderId = null,
                ForwardedOriginalSentAt = forward.OriginalSentAtMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(forward.OriginalSentAtMs).UtcDateTime
                    : null,
                ForwardedOrder = forward.Order,
                ForwardedAttachments = ImportAttachments(forward.Attachments),
            });
        }

        return imported;
    }

    /// <summary>Столько же вложений, сколько у обычного сообщения.</summary>
    private const int FederatedForwardAttachmentLimit = 10;

    private static List<Domain.ForwardedMessageAttachment> ImportAttachments(
        IReadOnlyCollection<FederatedFileRefFlat> attachments)
    {
        var imported = new List<Domain.ForwardedMessageAttachment>(attachments.Count);

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.OriginServer) ||
                !Guid.TryParse(attachment.FileId, out _) ||
                attachment.SizeBytes < 0 ||
                attachment.SizeBytes > FederationImportValidator.MaxFileBytes ||
                !Enum.IsDefined((Domain.MessageAttachmentType)attachment.AttachmentType))
            {
                throw new FederatedForwardInvalidException();
            }

            imported.Add(new Domain.ForwardedMessageAttachment
            {
                Type = (Domain.MessageAttachmentType)attachment.AttachmentType,
                FileId = attachment.FileId,
                FileSize = attachment.SizeBytes,
                OriginServer = attachment.OriginServer,
                // Превью тянется с origin по требованию (этап 3.3), локального URL у него нет.
                PreviewUrl = null,
            });
        }

        return imported;
    }
}
