using BarkFluff.Proto.Federation;
using BarkFluff.Proto.Shared;

namespace BarkFluff.Federation.Infrastructure;

/// <summary>
/// Пересланные сообщения из wire-представления сообщения → <c>FederatedForward</c> исходящего
/// S2S-события.
///
/// Источник — тот же разобранный <c>barkfluff.shared.Message</c>, из которого консюмер уже берёт
/// текст: снапшот пересылки лежит прямо в его вложениях, отдельного запроса к Messages не нужно.
/// </summary>
public static class FederatedForwardMapper
{
    public static IEnumerable<FederatedForward> FromWireMessage(Message? wireMessage, string originServer)
    {
        var attachments = wireMessage?.Content?.Attachments;

        if (attachments is null)
        {
            yield break;
        }

        foreach (var attachment in attachments)
        {
            if (attachment.Type != MessageAttachmentType.ForwardedMessage || attachment.ForwardedMessage is null)
            {
                continue;
            }

            var forwarded = attachment.ForwardedMessage;

            var federated = new FederatedForward
            {
                AuthorName = forwarded.AuthorName,
                Text = forwarded.Text,
                Order = forwarded.Order,
            };

            if (forwarded.OriginalSentAt is not null)
            {
                federated.OriginalSentAt = forwarded.OriginalSentAt;
            }

            foreach (var file in forwarded.Attachments)
            {
                federated.Attachments.Add(new FederatedFileRef
                {
                    // Пустой origin_server = файл наш: байты за ним придут к нам. Непустой означает,
                    // что переслали уже чужое вложение, и владельцем остаётся исходная нода.
                    OriginServer = string.IsNullOrEmpty(file.OriginServer) ? originServer : file.OriginServer,
                    FileId = file.FileId,
                    Filename = file.FileName,
                    SizeBytes = file.AttachmentSize,
                    AttachmentType = (int)file.Type,
                    PreviewFileId = file.PreviewFileId,
                    ImageWidth = file.ImageWidth,
                    ImageHeight = file.ImageHeight,
                });
            }

            yield return federated;
        }
    }
}
