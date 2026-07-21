namespace BarkFluff.Messages.Domain;

// Прочтение remote-участником fed-DM (этап 2.4, docs/rearch/05-chat-replication.md, «Read receipts»).
// UserUuid — кто прочитал (всегда remote для этой ноды: свои читатели пишутся в Message.ReadBy как обычно).
// LastReadFederatedMessageId — «прочитано до» включительно; NULL, если ещё ничего не прочитано.
public class FederatedReadState
{
    public Guid ChatId { get; set; }

    public Guid UserUuid { get; set; }

    public Guid? LastReadFederatedMessageId { get; set; }

    public DateTime ReadAt { get; set; }
}
