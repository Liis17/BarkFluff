using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WPF.Services.Notification
{
    /// <summary>
    /// Данные для отображения уведомления
    /// </summary>
    public class NotificationData
    {
        /// <summary>
        /// ID чата для открытия при клике на уведомление
        /// </summary>
        public string ChatId { get; set; } = string.Empty;

        /// <summary>
        /// ID пользователя для открытия чата
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// Заголовок уведомления (имя пользователя/чата)
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Текст сообщения
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// ID файла аватара (для загрузки из кеша)
        /// </summary>
        public string? AvatarFileId { get; set; }

        /// <summary>
        /// URL аватара (резервный вариант)
        /// </summary>
        public string? AvatarUrl { get; set; }

        /// <summary>
        /// ID первого изображения (если есть вложения)
        /// </summary>
        public string? ImageFileId { get; set; }

        /// <summary>
        /// URL первого изображения (резервный вариант)
        /// </summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// Количество файлов во вложениях
        /// </summary>
        public int FileCount { get; set; }

        /// <summary>
        /// Тип уведомления
        /// </summary>
        public NotificationType Type { get; set; } = NotificationType.TextMessage;

        /// <summary>
        /// Является ли это групповым чатом
        /// </summary>
        public bool IsGroupChat { get; set; }

        /// <summary>
        /// ID последнего сообщения в чате
        /// </summary>
        public long LastMessageId { get; set; }
    }

    /// <summary>
    /// Тип уведомления
    /// </summary>
    public enum NotificationType
    {
        /// <summary>
        /// Текстовое сообщение
        /// </summary>
        TextMessage,

        /// <summary>
        /// Сообщение с изображением
        /// </summary>
        ImageMessage,

        /// <summary>
        /// Сообщение с файлами
        /// </summary>
        FileMessage,

        /// <summary>
        /// Сообщение с видео
        /// </summary>
        VideoMessage,

        /// <summary>
        /// Сообщение с GIF
        /// </summary>
        GifMessage
    }
}
