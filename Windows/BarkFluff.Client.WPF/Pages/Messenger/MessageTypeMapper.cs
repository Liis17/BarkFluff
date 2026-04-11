using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Linq;

using MessageAttachmentType = BarkFluff.Proto.Shared.MessageAttachmentType;

namespace BarkFluff.Client.WPF.Pages.Messenger
{
    /// <summary>
    /// Маппинг <see cref="MessageModel"/> -> <see cref="MessageBubble.MessageType"/> по первому вложению.
    /// Используется контроллерами истории / отправки / realtime.
    /// </summary>
    internal static class MessageTypeMapper
    {
        public static MessageBubble.MessageType GetMessageType(MessageModel message)
        {
            if (message.Attachments == null || message.Attachments.Count == 0)
            {
                return MessageBubble.MessageType.Text;
            }

            var firstAttachment = message.Attachments.FirstOrDefault();
            if (firstAttachment == null)
            {
                return MessageBubble.MessageType.Text;
            }

            return firstAttachment.Type switch
            {
                MessageAttachmentType.Image => MessageBubble.MessageType.Image,
                MessageAttachmentType.Video => MessageBubble.MessageType.Video,
                MessageAttachmentType.Gif => MessageBubble.MessageType.Gif,
                MessageAttachmentType.Document => MessageBubble.MessageType.Document,
                MessageAttachmentType.Audio => MessageBubble.MessageType.Audio,
                MessageAttachmentType.Voice => MessageBubble.MessageType.Voice,
                MessageAttachmentType.Sticker => MessageBubble.MessageType.Sticker,
                _ => MessageBubble.MessageType.Text
            };
        }
    }
}
