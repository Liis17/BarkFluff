using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class MessageModel
    {
        public long MessageId { get; set; } = 0;
        public string ChatId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public List<AttachmentsModel> Attachments { get; set; } = new List<AttachmentsModel>();
        public long SenderId { get; set; } = 0;
        public List<long> ReadBy { get; set; } = new List<long>();
        public Timestamp SentAt { get; set; } = new Timestamp();
        public BarkFluff.Proto.Shared.MessageContentType Type { get; set; } = Proto.Shared.MessageContentType.Generic;
        public bool IsSystemMessage { get; set; } = false;
        public bool IsEdited { get; set; } = false;
        public Timestamp? EditedAt { get; set; } = null;
        public MessageModel() { }

    }
}
