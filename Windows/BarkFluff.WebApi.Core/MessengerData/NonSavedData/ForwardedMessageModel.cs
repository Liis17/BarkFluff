namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    /// <summary>
    /// Снапшот пересылаемого сообщения. Приходит во вложении с типом FORWARDED_MESSAGE.
    /// Цитата ответа сюда больше не попадает — у неё свой тип <see cref="ReplyPreviewModel"/>.
    /// </summary>
    public class ForwardedMessageModel
    {
        public string AuthorName { get; set; } = string.Empty;
        public long OriginalMessageId { get; set; } = 0;
        public string Text { get; set; } = string.Empty;
        /// <summary>Вложения оригинального сообщения (без FORWARDED_MESSAGE — рекурсии нет)</summary>
        public List<AttachmentsModel> Attachments { get; set; } = new List<AttachmentsModel>();
        /// <summary>Чат оригинала. Пусто у снапшотов, созданных до разделения reply/forward.</summary>
        public string OriginalChatId { get; set; } = string.Empty;
        public long OriginalSenderId { get; set; } = 0;
        public Google.Protobuf.WellKnownTypes.Timestamp? OriginalSentAt { get; set; } = null;
        /// <summary>Порядок внутри пересылки нескольких сообщений.</summary>
        public int Order { get; set; } = 0;
        public ForwardedMessageModel() { }
    }
}
