namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class ForwardingLetter
    {
        public string Text { get; set; } = string.Empty;
        public List<string> FilesId { get; set; } = new List<string>();
        /// <summary>Идентификатор пересылаемого сообщения (0 = не пересылка/не ответ)</summary>
        public long ForwardedMessageId { get; set; } = 0;
    }
}
