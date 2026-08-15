namespace BarkFluff.Messages.Mapping;

using BarkFluff.Proto.Files;

using Proto.Shared;

public static class MessageContentMapping
{
    public static MessageContent ToGrpc(this Domain.MessageContent messageContent)
    {
        return ToGrpc(messageContent, null);
    }

    public static MessageContent ToGrpc(this Domain.MessageContent messageContent, Dictionary<string, UploadFileInfo>? filesInfoMap)
    {
        var content = new MessageContent()
        {
            Text = messageContent.Text,
        };

        if (messageContent.Attachments == null)
        {
            return content;
        }

        foreach (var attachment in messageContent.Attachments)
        {
            var protoAttachment = new MessageAttachment
            {
                Id = attachment.Id,
                Type = (MessageAttachmentType)(int)attachment.Type,
                FileId = attachment.FileId ?? string.Empty,
                PreviewUrl = attachment.PreviewUrl ?? string.Empty,
                AttachmentSize = attachment.FileSize,
            };

            if (attachment.Type == Domain.MessageAttachmentType.ForwardedMessage)
            {
                var forwarded = new ForwardedMessageAttachment
                {
                    AuthorName = attachment.ForwardedAuthorName ?? string.Empty,
                    OriginalMessageId = attachment.ForwardedOriginalMessageId ?? 0,
                    Text = attachment.ForwardedText ?? string.Empty,
                    // Снапшоты, созданные до разделения reply/forward, этих полей не имеют —
                    // отдаём нули/пустые, клиент трактует их как «источник неизвестен».
                    OriginalChatId = attachment.ForwardedOriginalChatId?.ToString() ?? string.Empty,
                    OriginalSenderId = attachment.ForwardedOriginalSenderId ?? 0,
                    Order = attachment.ForwardedOrder ?? 0,
                };

                if (attachment.ForwardedOriginalSentAt is { } originalSentAt)
                {
                    forwarded.OriginalSentAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(
                        DateTime.SpecifyKind(originalSentAt, DateTimeKind.Utc));
                }

                if (attachment.ForwardedAttachments != null)
                {
                    foreach (var fa in attachment.ForwardedAttachments)
                    {
                        var faFileInfo = filesInfoMap?.GetValueOrDefault(fa.FileId);
                        forwarded.Attachments.Add(new MessageAttachment
                        {
                            Id = fa.Id,
                            Type = (MessageAttachmentType)(int)fa.Type,
                            FileId = fa.FileId,
                            PreviewUrl = fa.PreviewUrl ?? string.Empty,
                            AttachmentSize = fa.FileSize,
                            PreviewFileId = faFileInfo?.PreviewFileId ?? string.Empty,
                            FileName = faFileInfo?.FileName ?? string.Empty,
                            // Колонка заведена на этапе 3.3 ради проверки доступа, но наружу не
                            // отдавалась. Без неё клиент не отличит форварднутое fed-вложение от
                            // локального, а федерация не знает, чью ноду указывать владельцем байтов.
                            OriginServer = fa.OriginServer ?? string.Empty,
                        });
                    }
                }

                protoAttachment.ForwardedMessage = forwarded;
            }
            else if (attachment.OriginServer is { Length: > 0 })
            {
                // Federated-вложение (этап 3.1): всё берётся из снапшота в БД — Files не дёргаем
                // вообще, файла у нас нет. preview_url не заполняется: превью тянется с origin
                // по требованию, контракт ссылки — этап 3.3.
                protoAttachment.OriginServer = attachment.OriginServer;
                protoAttachment.FileName = attachment.FileName ?? string.Empty;
                protoAttachment.PreviewFileId = attachment.PreviewFileId ?? string.Empty;
                protoAttachment.ImageWidth = attachment.ImageWidth ?? 0;
                protoAttachment.ImageHeight = attachment.ImageHeight ?? 0;
            }
            else
            {
                var fileInfo = filesInfoMap?.GetValueOrDefault(attachment.FileId ?? string.Empty);
                protoAttachment.PreviewFileId = fileInfo?.PreviewFileId ?? string.Empty;
                protoAttachment.FileName = fileInfo?.FileName ?? string.Empty;
            }

            content.Attachments.Add(protoAttachment);
        }

        return content;
    }
}
