namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    /// <summary>
    /// Превью цитируемого сообщения. В отличие от <see cref="ForwardedMessageModel"/> это не
    /// снапшот: сервер резолвит его из живого оригинала на каждой выдаче, поэтому правка
    /// оригинала видна, а у удалённого текст пуст.
    /// </summary>
    public class ReplyPreviewModel
    {
        public long MessageId { get; set; } = 0;
        public long SenderId { get; set; } = 0;
        public string SenderName { get; set; } = string.Empty;
        public string TextPreview { get; set; } = string.Empty;
        public Proto.Shared.MessageAttachmentType FirstAttachmentType { get; set; } = Proto.Shared.MessageAttachmentType.Unknown;
        public bool IsDeleted { get; set; } = false;
    }
}
