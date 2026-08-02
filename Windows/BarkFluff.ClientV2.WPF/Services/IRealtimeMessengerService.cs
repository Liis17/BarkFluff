namespace BarkFluff.ClientV2.WPF.Services;

public sealed record MessageReadReceipt(string ChatId, long MessageId, IReadOnlyCollection<long> ReadBy);

public sealed record PrivateMessageReadReceipt(string ChatId, long UserId, long LastReadMessageId);

public interface IRealtimeMessengerService : IAsyncDisposable
{
    event EventHandler<MessageReadReceipt>? MessageRead;

    event EventHandler<PrivateMessageReadReceipt>? PrivateMessageRead;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
