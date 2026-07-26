using BarkFluff.Shared.Queue.Federation;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

namespace BarkFluff.Messages.Infrastructure;

public class ReadByQueueSender
{
    private readonly IPublishEndpoint _publishEndpoint;

    public ReadByQueueSender(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    /// <summary>
    /// Публикация прочтения. Федеративные поля (этап 2.4) — см. MessageQueueSender.SendEdited про
    /// исходящий/входящий путь: remoteParticipants=[] для apply-пути (прочитано пришло с другой ноды).
    /// </summary>
    public async Task SendEvent(
        Guid chatId,
        long messageId,
        List<long> readBy,
        List<long> newReaders,
        List<long> chatMembers,
        bool isFederated = false,
        Guid? readerUuid = null,
        Guid? upToFederatedMessageId = null,
        List<FederatedParticipant>? remoteParticipants = null,
        DateTimeOffset? lastChangeAt = null)
    {
        var newEvent = new MessageReadEvent()
        {
            ChatId = chatId,
            MessageId = messageId,
            NewReadBy = readBy,
            NewReaders = newReaders,
            ChatMembers = chatMembers,
            IsFederated = isFederated,
            ReaderUuid = readerUuid,
            UpToFederatedMessageId = upToFederatedMessageId,
            RemoteParticipants = remoteParticipants ?? new List<FederatedParticipant>(),
            LastChangeAt = lastChangeAt,
        };

        await _publishEndpoint.Publish(newEvent);
    }
}
