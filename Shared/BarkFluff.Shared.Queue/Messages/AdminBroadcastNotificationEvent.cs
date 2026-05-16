namespace BarkFluff.Shared.Queue.Messages;

public class AdminBroadcastNotificationEvent
{
    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    // Пусто → рассылка на ВСЕ устройства с FCM-токеном.
    // Заполнено → только эти DeviceId.
    public List<Guid> TargetDeviceIds { get; set; } = [];
}
