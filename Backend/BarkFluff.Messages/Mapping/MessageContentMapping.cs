
namespace BarkFluff.Messages.Mapping;

using Proto.Shared;

public static class MessageContentMapping
{
    public static MessageContent ToGrpc(this Domain.MessageContent messageContent)
    {
        var content =  new MessageContent()
        {
            Text = messageContent.Text,
        };


        if (messageContent.Attachments == null)
        {
            return content;
        }
        
        foreach (var attachment in messageContent.Attachments)
        {
            content.Attachments.Add(new MessageAttachment()
            {
                FileId = attachment.FileId,
                PreviewUrl = attachment.PreviewUrl ?? string.Empty,
                AttachmentSize = attachment.FileSize,
                Id = attachment.Id,
                Type = (MessageAttachmentType)(int)attachment.Type
            });
        }

        return content;
    }
}