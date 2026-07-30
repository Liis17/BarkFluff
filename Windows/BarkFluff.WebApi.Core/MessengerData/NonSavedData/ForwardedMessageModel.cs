namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    /// <summary>
    /// Данные пересылаемого сообщения. Приходит во вложении с типом FORWARDED_MESSAGE
    /// и используется как для блока пересылки, так и для цитаты ответа.
    /// </summary>
    public class ForwardedMessageModel
    {
        public string AuthorName { get; set; } = string.Empty;
        public long OriginalMessageId { get; set; } = 0;
        public string Text { get; set; } = string.Empty;
        /// <summary>Вложения оригинального сообщения (без FORWARDED_MESSAGE — рекурсии нет)</summary>
        public List<AttachmentsModel> Attachments { get; set; } = new List<AttachmentsModel>();
        public ForwardedMessageModel() { }
    }
}
