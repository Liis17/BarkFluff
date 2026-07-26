using BarkFluff.Proto.Federation;
using BarkFluff.Shared.Queue.Federation;

namespace BarkFluff.Federation.Infrastructure;

/// <summary>
/// Снапшот вложений из внутреннего события → <c>FederatedFileRef</c> исходящего S2S-события
/// (этап 3.1). Байты не передаются — только метаданные.
/// </summary>
public static class FederatedFileRefMapper
{
    public static IEnumerable<FederatedFileRef> ToProto(IEnumerable<FederatedFileRefInfo>? attachments)
    {
        if (attachments is null)
        {
            yield break;
        }

        foreach (var attachment in attachments)
        {
            yield return new FederatedFileRef
            {
                OriginServer = attachment.OriginServer,
                FileId = attachment.FileId,
                Filename = attachment.FileName ?? string.Empty,
                SizeBytes = attachment.SizeBytes,
                AttachmentType = attachment.AttachmentType,
                PreviewFileId = attachment.PreviewFileId ?? string.Empty,
                // 0 в proto = «не изображение», ровно как в снапшоте null.
                ImageWidth = attachment.ImageWidth ?? 0,
                ImageHeight = attachment.ImageHeight ?? 0,
            };
        }
    }
}
