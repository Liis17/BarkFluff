namespace BarkFluff.Messages.Domain;

/// <summary>
/// Durable NewMessageEvent written in the same transaction as Message.
/// Delivery is at-least-once; EventId remains stable across dispatcher retries.
/// </summary>
public class MessageOutboxEntry
{
    public long Id { get; set; }

    public Guid EventId { get; set; }

    public long MessageId { get; set; }

    public byte[] Payload { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public int Attempts { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public MessageOutboxStatus Status { get; set; } = MessageOutboxStatus.Pending;

    public string? LastError { get; set; }
}

public enum MessageOutboxStatus
{
    Pending = 0,
    Delivered = 1,
    DeadLetter = 2,
    Processing = 3,
}
