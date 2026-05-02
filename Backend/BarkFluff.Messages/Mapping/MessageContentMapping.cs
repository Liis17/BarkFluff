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
                };

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
                        });
                    }
                }

                protoAttachment.ForwardedMessage = forwarded;
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
