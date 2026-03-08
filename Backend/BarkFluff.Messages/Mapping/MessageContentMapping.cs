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
            var fileInfo = filesInfoMap?.GetValueOrDefault(attachment.FileId);

            content.Attachments.Add(new MessageAttachment()
            {
                FileId = attachment.FileId,
                PreviewUrl = attachment.PreviewUrl ?? string.Empty,
                PreviewFileId = fileInfo?.PreviewFileId ?? string.Empty,
                FileName = fileInfo?.FileName ?? string.Empty,
                AttachmentSize = attachment.FileSize,
                Id = attachment.Id,
                Type = (MessageAttachmentType)(int)attachment.Type
            });
        }

        return content;
    }
}