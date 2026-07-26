using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Features.Federation;

/// <summary>
/// Валидация и импорт снапшота вложений fed-сообщения (этап 3.1).
/// </summary>
/// <remarks>
/// Снапшот приходит с чужой ноды, поэтому доверять ему нельзя: всё, что не проходит валидацию,
/// отклоняется <b>permanent</b> (REJECTED), а не RETRY — повторная доставка того же битого
/// события ничего не исправит и только зациклит outbox отправителя.
/// </remarks>
public static class FederatedAttachmentImporter
{
    public const int MaxFileNameLength = 255;

    /// <summary>Собрать строки MessageAttachments из снапшота, попутно провалидировав его.</summary>
    public static List<Domain.MessageAttachment> Import(IReadOnlyCollection<FederatedFileRefFlat> attachments)
    {
        FederationImportValidator.ValidateAttachmentCount(attachments.Count);

        var imported = new List<Domain.MessageAttachment>(attachments.Count);

        foreach (var attachment in attachments)
        {
            Validate(attachment);

            imported.Add(new Domain.MessageAttachment
            {
                Type = (Domain.MessageAttachmentType)attachment.AttachmentType,
                FileId = attachment.FileId,
                OriginServer = attachment.OriginServer,
                FileName = string.IsNullOrEmpty(attachment.Filename) ? null : attachment.Filename,
                FileSize = attachment.SizeBytes,
                PreviewFileId = string.IsNullOrEmpty(attachment.PreviewFileId) ? null : attachment.PreviewFileId,
                ImageWidth = attachment.ImageWidth > 0 ? attachment.ImageWidth : null,
                ImageHeight = attachment.ImageHeight > 0 ? attachment.ImageHeight : null,
                // Превью тянется с origin по требованию (этап 3.3), локального URL у него нет.
                PreviewUrl = null,
            });
        }

        return imported;
    }

    private static void Validate(FederatedFileRefFlat attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.OriginServer))
        {
            throw new FederatedAttachmentInvalidException();
        }

        if (!Guid.TryParse(attachment.FileId, out _))
        {
            throw new FederatedAttachmentInvalidException();
        }

        // Пустой preview допустим — он есть не у всех типов вложений.
        if (!string.IsNullOrEmpty(attachment.PreviewFileId) && !Guid.TryParse(attachment.PreviewFileId, out _))
        {
            throw new FederatedAttachmentInvalidException();
        }

        if (attachment.SizeBytes < 0 || attachment.SizeBytes > FederationImportValidator.MaxFileBytes)
        {
            throw new FederatedAttachmentInvalidException();
        }

        if (!Enum.IsDefined((Domain.MessageAttachmentType)attachment.AttachmentType))
        {
            throw new FederatedAttachmentInvalidException();
        }

        if (attachment.Filename.Length > MaxFileNameLength)
        {
            throw new FederatedAttachmentInvalidException();
        }
    }
}
