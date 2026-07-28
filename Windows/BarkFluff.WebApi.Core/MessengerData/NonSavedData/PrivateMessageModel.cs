using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    /// <summary>
    /// Расшифрованное сообщение приватного чата.
    /// </summary>
    public class PrivateMessageModel
    {
        public long MessageId { get; set; } = 0;
        public string ChatId { get; set; } = string.Empty;
        public long SenderId { get; set; } = 0;
        public string SenderDeviceId { get; set; } = string.Empty;
        public Timestamp SentAt { get; set; } = new Timestamp();
        public string Text { get; set; } = string.Empty;
        public bool IsEdited { get; set; } = false;
        public Timestamp? EditedAt { get; set; } = null;
        public bool IsDeleted { get; set; } = false;

        /// <summary>
        /// Расшифровать сообщение не удалось: неверная кодовая фраза, чужой AAD
        /// или повреждённый шифротекст. Текст в этом случае пустой — сообщение
        /// стоит показать плейсхолдером, а не прятать.
        /// </summary>
        public bool DecryptionFailed { get; set; } = false;

        public PrivateMessageModel() { }
    }
}
