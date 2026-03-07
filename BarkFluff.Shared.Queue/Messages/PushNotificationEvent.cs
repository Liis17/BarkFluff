namespace BarkFluff.Shared.Queue.Messages;

public class PushNotificationEvent
{
    public Guid ChatId { get; set; }

    public long SenderId { get; set; }

    public long MessageId { get; set; }

    public string? MessageText { get; set; }

    public List<long> RecipientUserIds { get; set; } = [];

    // Аватар отправителя
    public string? SenderAvatarUrl { get; set; }

    // Информация о чате (для групповых)
    public string? ChatTitle { get; set; }
    public string? ChatAvatarUrl { get; set; }
    public bool IsGroupChat { get; set; }

    // Контент сообщения
    public int ContentType { get; set; } // MessageContentType enum value
    public string? ImagePreviewUrl { get; set; } // URL превью картинки для BigPictureStyle
}
