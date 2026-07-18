using BarkFluff.Shared.Queue.Federation;

namespace BarkFluff.Shared.Queue.Messages;

public class MessageEditedEvent
{
    public Guid ChatId { get; set; }

    public List<long> ChatMembers { get; set; }

    public byte[] Message { get; set; }

    // Федеративный контекст (этап 2.2). Заполняет Messages с 2.3.
    public bool IsFederated { get; set; }

    public List<FederatedParticipant> RemoteParticipants { get; set; } = [];

    public Guid? FederatedId { get; set; }

    public Guid? SenderUuid { get; set; }

    public DateTimeOffset? LastChangeAt { get; set; }

    public string? SenderDisplayName { get; set; }
    public string? SenderFid { get; set; }
}
