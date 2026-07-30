using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.Core.Services;

public sealed record MessageReadReceipt(string ChatId, long MessageId, IReadOnlyCollection<long> ReadBy);

public sealed record PrivateMessageReadReceipt(string ChatId, long UserId, long LastReadMessageId);

public sealed record IncomingMessage(string ChatId, MessageModel Message);

public interface IRealtimeMessengerService : IAsyncDisposable
{
    /// <summary>
    /// Новое сообщение в любом чате пользователя, включая эхо собственной отправки:
    /// дедупликация по идентификатору — на подписчике.
    /// </summary>
    event EventHandler<IncomingMessage>? MessageReceived;

    event EventHandler<MessageReadReceipt>? MessageRead;

    event EventHandler<PrivateMessageReadReceipt>? PrivateMessageRead;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
