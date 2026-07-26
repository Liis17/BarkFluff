using BarkFluff.Messages.Domain;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Queue.Federation;

namespace BarkFluff.Messages.Features.Federation;

/// <summary>
/// Снапшот метаданных вложений для исходящих fed-событий (этап 3.1).
/// </summary>
/// <remarks>
/// Байты не реплицируются — уезжают только метаданные, чтобы принимающая нода отрисовала
/// сообщение без похода к нам. Данные берутся из уже полученного ответа Files
/// (<c>GetFilesData</c>), второго вызова ради федерации не делаем.
/// </remarks>
public static class FederatedAttachmentMapper
{
    public static List<FederatedFileRefInfo>? Build(
        IEnumerable<MessageAttachment>? attachments,
        IReadOnlyDictionary<string, UploadFileInfo>? filesInfoMap,
        string ownServerName)
    {
        if (attachments is null || string.IsNullOrEmpty(ownServerName))
        {
            return null;
        }

        var refs = new List<FederatedFileRefInfo>();

        foreach (var attachment in attachments)
        {
            // Forwarded-структура федерируется как есть внутри самого сообщения,
            // отдельным файловым ref'ом она не является.
            if (attachment.Type == MessageAttachmentType.ForwardedMessage
                || string.IsNullOrEmpty(attachment.FileId))
            {
                continue;
            }

            var fileInfo = filesInfoMap is not null && filesInfoMap.TryGetValue(attachment.FileId, out var info)
                ? info
                : null;

            refs.Add(new FederatedFileRefInfo
            {
                OriginServer = ownServerName,
                FileId = attachment.FileId,
                FileName = fileInfo?.FileName,
                SizeBytes = attachment.FileSize,
                AttachmentType = (int)attachment.Type,
                PreviewFileId = string.IsNullOrEmpty(fileInfo?.PreviewFileId) ? null : fileInfo.PreviewFileId,
                // 0 в proto означает «не изображение» — в снапшоте это null.
                ImageWidth = fileInfo is { ImageWidth: > 0 } ? fileInfo.ImageWidth : null,
                ImageHeight = fileInfo is { ImageHeight: > 0 } ? fileInfo.ImageHeight : null,
            });
        }

        return refs.Count > 0 ? refs : null;
    }
}
