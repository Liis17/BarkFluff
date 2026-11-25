using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using BarkFluff.Proto.Shared;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Client.Core.Services;

public sealed record MessageReadReceipt(string ChatId, long MessageId, IReadOnlyCollection<long> ReadBy);

public sealed record PrivateMessageReadReceipt(string ChatId, long UserId, long LastReadMessageId);

public sealed record IncomingMessage(string ChatId, MessageModel Message);

public enum MessageUpdateKind { New, Edit, Delete, Pin, Unpin, UnpinAll }

public sealed record MessageUpdate(
    string ChatId, bool IsPrivate, long MessageId, MessageUpdateKind Kind,
    MessageModel? Message = null, EncryptedMessage? Encrypted = null,
    Timestamp? PinnedAt = null, long PinnerId = 0);

public interface IRealtimeMessengerService : IAsyncDisposable
{
    /// <summary>
    /// Новое сообщение в любом чате пользователя, включая эхо собственной отправки:
    /// дедупликация по идентификатору — на подписчике.
    /// </summary>
    event EventHandler<IncomingMessage>? MessageReceived;

    event EventHandler<MessageReadReceipt>? MessageRead;

    event EventHandler<PrivateMessageReadReceipt>? PrivateMessageRead;

    event EventHandler<MessageUpdate>? MessageChanged;

    event EventHandler? ChatFoldersChanged { add { } remove { } }

    /// <summary>True on the primary stream's first item, false on a subscription opening.</summary>
    event EventHandler<bool>? ResyncRequested;

    /// <summary>
    /// Состояние связи: <c>false</c> — стрим оборван, идёт переподключение.
    /// Приходит из фонового потока, маршалить в UI обязан подписчик.
    /// </summary>
    event EventHandler<bool>? ConnectionChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
