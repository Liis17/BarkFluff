namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class ForwardingLetter
    {
        public string Text { get; set; } = string.Empty;
        public List<string> FilesId { get; set; } = new List<string>();
        /// <summary>Сообщение этого же чата, на которое отвечаем (0 = не ответ).</summary>
        public long ReplyToMessageId { get; set; } = 0;
        /// <summary>Пересылаемые сообщения (до 20), порядок сохраняется.</summary>
        public List<long> ForwardedMessageIds { get; set; } = new List<long>();
        /// <summary>
        /// Устаревшее одиночное поле: до разделения reply/forward им отправлялись оба действия.
        /// Оставлено рабочим для ClientV2.WPF, который ещё на нём. Смешивать с полями выше нельзя —
        /// сервер отвечает InvalidArgument.
        /// </summary>
        public long ForwardedMessageId { get; set; } = 0;
    }
}
