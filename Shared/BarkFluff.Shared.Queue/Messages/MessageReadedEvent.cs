using BarkFluff.Shared.Queue.Federation;

namespace BarkFluff.Shared.Queue.Messages;

public class MessageReadEvent
{
    public Guid ChatId { get; set; }

    public long MessageId { get; set; }

    public List<long> NewReadBy { get; set; } = [];

    public List<long> NewReaders { get; set; } = [];

    public List<long> ChatMembers { get; set; } = [];

    // Федеративный контекст (этап 2.2). Заполняет Messages с 2.3.
    // ReaderUuid — кто прочитал (remote), UpToFederatedMessageId — «прочитано до» сообщения на origin.
    public bool IsFederated { get; set; }

    public List<FederatedParticipant> RemoteParticipants { get; set; } = [];

    public Guid? ReaderUuid { get; set; }

    public Guid? UpToFederatedMessageId { get; set; }

    public DateTimeOffset? LastChangeAt { get; set; }
}
