namespace BarkFluff.Messages.Infrastructure;

using Domain;

using Google.Protobuf;

using Mapping;

using MassTransit;

using Proto.Files;

using Shared.Queue.Federation;
using Shared.Queue.Messages;

public class MessageQueueSender
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MessageQueueSender(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task SendMessage(Message message, Guid chatId, List<long> chatMembers, Dictionary<string, UploadFileInfo>? filesInfoMap = null)
    {
        var newMessageEvent = new NewMessageEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            Message = message.ToGrpc(filesInfoMap).ToByteArray()
        };

        await _publishEndpoint.Publish(newMessageEvent);
    }

    /// <summary>
    /// Публикация импортированного fed-сообщения (этап 2.3). Заполняет федеративные поля:
    /// Updates/CloudMessaging стримят/пушат только локальным получателям, на удалённую ноду сообщение
    /// не пересылается (оно пришло оттуда). SenderDisplayName/FID — этап 2.8.
    /// </summary>
    public async Task SendImportedMessage(
        Message message,
        Guid chatId,
        List<long> localChatMembers,
        Guid senderUuid,
        string senderUsername,
        string senderServerName,
        string ownServerName)
    {
        var newMessageEvent = new NewMessageEvent
        {
            ChatId = chatId,
            ChatMembers = localChatMembers,
            Message = message.ToGrpc().ToByteArray(),
            IsFederated = true,
            SenderUuid = senderUuid,
            SenderFid = $"@{senderUsername}:{senderServerName}",
            // RemoteParticipants для импортированного сообщения не нужен (отправлять никуда не надо),
            // но без них консюмер Federation просто не войдёт вpublish-ветку — что и требуется.
            RemoteParticipants = new List<FederatedParticipant>(),
        };

        await _publishEndpoint.Publish(newMessageEvent);
    }

    /// <summary>
    /// Публикация исходящего fed-сообщения (этап 2.3): локальная рассылка + федеративные поля
    /// для консюмера Federation (→ outbox → нода-партнёр).
    /// </summary>
    public async Task SendFederatedMessage(
        Message message,
        Guid chatId,
        List<long> localChatMembers,
        Dictionary<string, UploadFileInfo>? filesInfoMap,
        Guid federatedId,
        Guid senderUuid,
        List<FederatedParticipant> remoteParticipants,
        bool isFirstMessageInChat,
        Guid? initiatorUuid,
        Guid? inviteeUuid,
        string? senderFid)
    {
        var newMessageEvent = new NewMessageEvent
        {
            ChatId = chatId,
            ChatMembers = localChatMembers,
            Message = message.ToGrpc(filesInfoMap).ToByteArray(),
            IsFederated = true,
            FederatedId = federatedId,
            SenderUuid = senderUuid,
            RemoteParticipants = remoteParticipants,
            IsFirstMessageInChat = isFirstMessageInChat,
            InitiatorUuid = initiatorUuid,
            InviteeUuid = inviteeUuid,
            SenderFid = senderFid,
        };

        await _publishEndpoint.Publish(newMessageEvent);
    }

    /// <summary>
    /// Публикация правки (этап 2.4). Федеративные поля заполняются как для исходящего пути (локальная
    /// правка в fed-чате → remoteParticipants непустой, консюмер Federation положит в outbox), так и для
    /// входящего apply-пути (правка пришла с другой ноды → remoteParticipants=[] осознанно пусто,
    /// консюмер Federation её пропустит — переотправлять её обратно не нужно).
    /// </summary>
    public async Task SendEdited(
        Message message,
        Guid chatId,
        List<long> chatMembers,
        Dictionary<string, UploadFileInfo>? filesInfoMap = null,
        bool isFederated = false,
        Guid? federatedId = null,
        Guid? senderUuid = null,
        List<FederatedParticipant>? remoteParticipants = null,
        DateTimeOffset? lastChangeAt = null)
    {
        var editedEvent = new MessageEditedEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            Message = message.ToGrpc(filesInfoMap).ToByteArray(),
            IsFederated = isFederated,
            FederatedId = federatedId,
            SenderUuid = senderUuid,
            RemoteParticipants = remoteParticipants ?? new List<FederatedParticipant>(),
            LastChangeAt = lastChangeAt,
        };

        await _publishEndpoint.Publish(editedEvent);
    }

    /// <summary>Публикация удаления (этап 2.4) — см. SendEdited про исходящий/входящий путь.</summary>
    public async Task SendDeleted(
        Guid chatId,
        long messageId,
        List<long> chatMembers,
        bool isFederated = false,
        Guid? federatedId = null,
        List<FederatedParticipant>? remoteParticipants = null,
        DateTimeOffset? lastChangeAt = null)
    {
        var deletedEvent = new MessageDeletedEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            MessageId = messageId,
            IsFederated = isFederated,
            FederatedId = federatedId,
            RemoteParticipants = remoteParticipants ?? new List<FederatedParticipant>(),
            LastChangeAt = lastChangeAt,
        };

        await _publishEndpoint.Publish(deletedEvent);
    }

    public async Task SendPinned(Guid chatId, long messageId, long pinnerUserId, DateTime pinnedAt, List<long> chatMembers)
    {
        var pinnedEvent = new MessagePinnedEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            MessageId = messageId,
            PinnerUserId = pinnerUserId,
            PinnedAt = pinnedAt
        };

        await _publishEndpoint.Publish(pinnedEvent);
    }

    public async Task SendUnpinned(Guid chatId, long messageId, List<long> chatMembers)
    {
        var unpinnedEvent = new MessageUnpinnedEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers,
            MessageId = messageId
        };

        await _publishEndpoint.Publish(unpinnedEvent);
    }

    public async Task SendAllUnpinned(Guid chatId, List<long> chatMembers)
    {
        var allUnpinnedEvent = new AllMessagesUnpinnedEvent()
        {
            ChatId = chatId,
            ChatMembers = chatMembers
        };

        await _publishEndpoint.Publish(allUnpinnedEvent);
    }
}
