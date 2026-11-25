namespace BarkFluff.Client.Core.Services;

public enum ChatNotificationSound
{
    Default,
    Silent,
    InstantMessage,
    Mail,
    Reminder,
    Sms
}

public interface IChatNotificationSoundStore
{
    Task<ChatNotificationSound> LoadAsync(MessageNotificationTarget target, CancellationToken cancellationToken);
    Task SaveAsync(MessageNotificationTarget target, ChatNotificationSound sound, CancellationToken cancellationToken);
}
